using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices.JavaScript;
using System.Security.Cryptography;
using System.Text.Json;
using osuArchiveToolkit.Models;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Converters;

namespace osuArchiveToolkit.Services;

public class HashManagerService(string osuMascotArchivePath)
{
    private string _globalHashPath = "";
    private string _localHashPath = "";
    private string _reconciliationLogPath = "";
    private string _localHashFileString = "";
    private List<HashEntry> _localHashList = [];
    private List<HashEntry> _globalHashList = [];
    private List<ReconciliationLogEntry> _reconciliationLogEntries = [];
    public string LastDuplicatedFilePath { get; private set; } = "";
    public int LastReconciledCount { get; private set; }

    public bool CreateListFiles()
    {
        if (!IsListsPathsAvailable())
        {
            Directory.CreateDirectory(GetGlobalListsPath());
            Directory.CreateDirectory(GetLocalHashPath());
        }
        _globalHashPath = EnsureGlobalHashList();
        _localHashPath = EnsureLocalHashList();
        _reconciliationLogPath = EnsureGlobalLogList();

        if (!File.Exists(_globalHashPath)) 
            File.WriteAllText(_globalHashPath, "[]");
        
        if (!File.Exists(_localHashPath)) 
            File.WriteAllText(_localHashPath, "[]");
        
        if (!File.Exists(_reconciliationLogPath)) 
            File.WriteAllText(_reconciliationLogPath, "[]");
        return true;
    }

    public bool IsHashFileDuplicated(string filePath)
    {
        LastReconciledCount = ReconcileHashListsWithCurrentAssets();

        var localHashList = LoadHashList(_localHashPath);
        var globalHashList = LoadHashList(_globalHashPath);
        var localFileHash = CalculateFileHash(filePath);
        var duplicatedEntry = localHashList.FirstOrDefault(entry => entry.Hash == localFileHash);
        
        if (duplicatedEntry != null)
        {
            LastDuplicatedFilePath = duplicatedEntry.LastFilePath;
            return true;
        }

        duplicatedEntry = globalHashList.FirstOrDefault(entry => entry.Hash == localFileHash);
        if (duplicatedEntry == null) return false;
        LastDuplicatedFilePath = duplicatedEntry.LastFilePath;
        return true;
    }

    public int ReconcileHashListsWithCurrentAssets()
    {
        try
        {
            _localHashList = LoadHashList(_localHashPath);
            _globalHashList = LoadHashList(_globalHashPath);
            var reconciliationList = new Dictionary<HashEntry, ReconciliationState>();

            // 1. Reconciliación local: corrige las rutas de los assets locales contra el disco.
            var localSearchDirectory = Path.Combine(osuMascotArchivePath, "osu-mascot-workspace", "local_entries");
            var correctedCount = ReconcileEntryList(_localHashList, localSearchDirectory, reconciliationList);

            // Las entradas locales que no se pudieron resolver se eliminan de la lista local.
            var unresolvedLocal = reconciliationList
                .Where(r => r.Value == ReconciliationState.Unresolved)
                .Select(r => r.Key)
                .ToList();
            _localHashList.RemoveAll(e => unresolvedLocal.Any(u => u.Hash == e.Hash));
            foreach (var entry in unresolvedLocal)
            {
                reconciliationList[entry] = ReconciliationState.Removed;
                WriteReconciliationLog(nameof(EventType.Deleted), entry.Hash, entry.LastFilePath);
            }
            correctedCount += unresolvedLocal.Count;

            // Se persiste la lista local aquí: el trabajo local es válido aunque el pull falle después.
            PersistHashList(_localHashList, _localHashPath);

            // 2. git pull. Si no se logra sincronizar con remoto, se aborta antes de lo global.
            if (!CheckGitState()) return correctedCount;

            // 3. Reconciliación global: ya con el repo actualizado desde remoto (doble chequeo).
            var globalSearchDirectory = Path.Combine(osuMascotArchivePath, "osu-mascot-workspace");
            correctedCount += ReconcileEntryList(_globalHashList, globalSearchDirectory, reconciliationList);

            CollectUnlistedGlobalAssets();
            PersistHashList(_globalHashList, _globalHashPath);
            return correctedCount;
        }
        finally
        {
            PersistReconciliationLog(_reconciliationLogEntries); //qué pasa si la función no puede escribir? Todos los cambios de archivos quedan en memoria y se pierden. Revisar esto más adelante.
            _reconciliationLogEntries = [];
        }
    }
    public void WriteLocalHashEntry(string gitUser, string lastImportedAssetPath, string lastEntryPath)
    {
        var entry = new HashEntry()
        {
            Hash = _localHashFileString,
            HashCreatedAt = DateTime.UtcNow.ToString("u"),
            SubmittedBy = gitUser,
            LastFilePath = lastImportedAssetPath,
            ByteSize = new FileInfo(lastImportedAssetPath).Length,
            LastEntryPath = lastEntryPath
        };

        var hashEntries = LoadHashList(_localHashPath);
        hashEntries.Add(entry);
        PersistHashList(hashEntries, _localHashPath);
    }

    private string CalculateFileHash(string filePath)
    {
        if (!File.Exists(filePath)) return "";
        using var openFile = File.OpenRead(filePath);
        var hashFileBytes = SHA256.HashData(openFile);
        var hashBytesText = Convert.ToHexString(hashFileBytes).ToLowerInvariant();
        _localHashFileString = hashBytesText;

        return hashBytesText;
    }

    // Reconcilia una lista de hashes contra el disco. Devuelve cuántas entradas se reubicaron.
    // Comparte el diccionario de estados con el llamador para no reclamar dos veces el mismo archivo.
    private int ReconcileEntryList(
        List<HashEntry> hashList,
        string externalSearchDirectory,
        Dictionary<HashEntry, ReconciliationState> reconciliationList)
    {
        var relocatedCount = 0;

        foreach (var entry in hashList)
        {
            var currentPath = entry.LastFilePath;
            var currentDirectory = Path.GetDirectoryName(currentPath) ?? "";
            var checkHash = CalculateFileHash(currentPath);

            // Filtro 1: el archivo sigue donde lo esperamos y su contenido no cambió.
            if (File.Exists(currentPath) && checkHash == entry.Hash)
            {
                reconciliationList[entry] = ReconciliationState.Correct;
                continue;
            }

            // El archivo existe en su ruta pero su contenido cambió: se registra el evento.
            if (checkHash != entry.Hash)
            {
                reconciliationList[entry] = ReconciliationState.Unknown;
                if (File.Exists(currentPath))
                    WriteReconciliationLog(nameof(EventType.Changed), entry.Hash, currentPath);
            }

            // Filtro 2: mismo directorio. Filtro 3: en el directorio de búsqueda externo.
            // Ambos emparejan por tamaño en bytes.
            if (TryRelocateBySize(entry, currentDirectory, reconciliationList, recursive: false)
                || TryRelocateBySize(entry, externalSearchDirectory, reconciliationList, recursive: true))
            {
                relocatedCount++;
                WriteReconciliationLog(nameof(EventType.Moved), entry.Hash, entry.LastFilePath);
                continue;
            }

            if (!reconciliationList.ContainsKey(entry)
                || reconciliationList[entry] != ReconciliationState.Correct)
                reconciliationList[entry] = ReconciliationState.Unresolved;
        }

        return relocatedCount;
    }

    // Busca en searchDirectory un archivo soportado, aún no reclamado, del mismo tamaño que la entrada.
    // Si lo encuentra, actualiza su LastFilePath y lo marca como Correct.
    private bool TryRelocateBySize(
        HashEntry entry,
        string searchDirectory,
        Dictionary<HashEntry, ReconciliationState> reconciliationList,
        bool recursive)
    {
        if (!Directory.Exists(searchDirectory)) return false;

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        foreach (var candidate in Directory.GetFiles(searchDirectory, "*", searchOption))
        {
            if (!LocalImportService.IsSupportedImageFile(candidate)
                || reconciliationList.Any(r => r.Key.LastFilePath == candidate)) continue;

            if (new FileInfo(candidate).Length != entry.ByteSize) continue;

            entry.LastFilePath = candidate;
            reconciliationList[entry] = ReconciliationState.Correct;
            return true;
        }

        return false;
    }

    private static List<HashEntry> LoadHashList(string path) =>
        JsonSerializer.Deserialize<List<HashEntry>>(File.ReadAllText(path)) ?? [];

    private static void PersistHashList(List<HashEntry> hashList, string path)
    {
        var json = JsonSerializer.Serialize(hashList, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    private void CollectUnlistedGlobalAssets()
    {
        //refactorizar esto para que sea más dinámico (soportará cambios en la estructura de directorios en el futuro)
        var unverifiedDirectory = Path.Combine(
            osuMascotArchivePath, "osu-mascot-workspace", "99_Staging", "05_unverified", "unverified-assets");
        Directory.CreateDirectory(unverifiedDirectory);

        var allImages = Directory
            .GetFiles(Path.Combine(osuMascotArchivePath, "osu-mascot-workspace"), "*.*", SearchOption.AllDirectories)
            .Where(f => !f.Contains("local_entries") && LocalImportService.IsSupportedImageFile(f));

        foreach (var nextUnresolvedFile in allImages)
        {
            var hash = CalculateFileHash(nextUnresolvedFile);
            var isListed = _localHashList.Any(e => e.Hash == hash)
                           || _globalHashList.Any(e => e.Hash == hash);
            if (isListed) continue;

            var unverifiedAssetPath = Path.Combine(unverifiedDirectory, Path.GetFileName(nextUnresolvedFile));
            try
            {
                File.Copy(nextUnresolvedFile, unverifiedAssetPath);
            }
            catch (Exception error)
            {
                WriteReconciliationLog(Convert.ToString(error) ?? "Unknown unregistered exception", hash, nextUnresolvedFile);
                continue;
            }

            if (File.Exists(unverifiedAssetPath)) File.Delete(nextUnresolvedFile);
        }
    }
    //hash lists directory checking helpers
    private bool CheckGitState()
    {
        try
        {
            var process = new Process();

            process.StartInfo.FileName = "git";
            process.StartInfo.Arguments = "pull";
            process.StartInfo.WorkingDirectory = osuMascotArchivePath;

            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.UseShellExecute = false;

            process.Start();

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            if (process.ExitCode == 0) return true;

            LocalImportService.WriteLog(output, osuMascotArchivePath);
        }
        catch (Exception error)
        {
            var textError = Convert.ToString(error) ?? "Unknown unregistered exception";
            LocalImportService.WriteLog(textError, osuMascotArchivePath);
        }
        
        return false;
    }
    
    
    private void WriteReconciliationLog(string message, string relatedHash, string relatedPath)
    {
        var logEntry = new ReconciliationLogEntry()
        {
            LastKnownPath = relatedPath,
            Hash = relatedHash,
            EventType = message,
            OccurredAt = DateTime.Now.ToString("u")
        };
        _reconciliationLogEntries.Add(logEntry);
    }
    private void PersistReconciliationLog(List<ReconciliationLogEntry> unsavedLogEntries)
    {
        if (unsavedLogEntries.Count == 0) return;
        var currentLogList = File.ReadAllText(_reconciliationLogPath);
        var deserializedLog = JsonSerializer.Deserialize<List<ReconciliationLogEntry>>(currentLogList) ?? [];

        deserializedLog.AddRange(unsavedLogEntries);
        
        var logJson = JsonSerializer.Serialize(deserializedLog, new JsonSerializerOptions()
            { WriteIndented = true });
        File.WriteAllText(_reconciliationLogPath, logJson);
    }
    
    private bool IsListsPathsAvailable()
    {
        return !string.IsNullOrWhiteSpace(osuMascotArchivePath)
            && Directory.Exists(GetLocalHashPath())
            && Directory.Exists(GetGlobalListsPath());
    }
    private string EnsureGlobalHashList()
    {
        var globalHashFolder = GetGlobalListsPath();

        return Path.Combine(
            globalHashFolder,
            $"GlobalAssetsHashList.json"
        );
    }
    private string EnsureGlobalLogList()
    {
        var globalHashFolder = GetGlobalListsPath();

        return Path.Combine(
            globalHashFolder,
            $"GlobalReconciliationLog.json"
        );
    }
    private string EnsureLocalHashList()
    {
        var localHashFolder = GetLocalHashPath();

        return Path.Combine(
            localHashFolder,
            $"LocalAssetsHashList.json"
        );
        
    }
    private string GetLocalHashPath()
    {
        return Path.Combine(
            osuMascotArchivePath,
            "osu-mascot-workspace",
            "local_entries",
            "local_assets",
            ".local_hashes"
        );
    }
    private string GetGlobalListsPath()
    {
        return Path.Combine(
            osuMascotArchivePath,
            ".Models"
        );
    }

    private enum ReconciliationState { Correct, Unresolved, Unknown, Removed }
    private enum EventType { Deleted, Moved, Changed, } //TODO capturar otros movimientos y acciones del reconciliador
}