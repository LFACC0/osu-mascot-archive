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
    public string LastDuplicatedFilePath { get; private set; } = "";
    
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

    public bool IsHashFileDuplicated(string filePath) //TODO: queda pendiente hacer que la función revise y elimine hashes que no tengan un asset antes de asignar un nuevo hash.
    {
        ReconcileHashListsWithCurrentAssets(); //El nuevo sistema tolerará hasta 1888 assets relacionados a un solo colaborador (teniendo en cuenta como funciona reconcileHashLists(...)).
        
        var localHashFile = File.ReadAllText(_localHashPath);
        var globalHashFile = File.ReadAllText(_globalHashPath);
        var globalHashList = JsonSerializer.Deserialize<List<HashEntry>>(globalHashFile) ?? [];
        var localHashList = JsonSerializer.Deserialize<List<HashEntry>>(localHashFile) ?? [];
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

    private string CalculateFileHash(string filePath)
    {
        if (!File.Exists(filePath)) return "";
        using var openFile = File.OpenRead(filePath);
        var hashFileBytes = SHA256.HashData(openFile);
        var hashBytesText = Convert.ToHexString(hashFileBytes).ToLowerInvariant();
        _localHashFileString = hashBytesText;

        return hashBytesText;
    }
private int ReconcileHashListsWithCurrentAssets()
    { 
        _localHashList = JsonSerializer.Deserialize<List<HashEntry>>(
        File.ReadAllText(_localHashPath)) ?? [];
        _globalHashList = JsonSerializer.Deserialize<List<HashEntry>>(
        File.ReadAllText(_globalHashPath)) ?? [];
        var reconciliationList = new Dictionary<HashEntry, ReconciliationState>(); //usar esto para descartar archivos reconciliados
        
        foreach (var nextLocalEntry in _localHashList) // Local Asset Checker TODO
        {
            var nextByteSize = nextLocalEntry.ByteSize;
            var nextCurrentPath = nextLocalEntry.LastFilePath;
            var nextHash = nextLocalEntry.Hash;
            var checkHash = CalculateFileHash(nextCurrentPath);
            var nextCurrentDirectory = Path.GetDirectoryName(nextCurrentPath) ?? "";

            if (File.Exists(nextCurrentPath) // First check filter
                && checkHash == nextHash)
            {
                reconciliationList[nextLocalEntry] = ReconciliationState.Correct;
                continue;
            }

            if (checkHash != nextHash) reconciliationList[nextLocalEntry] = ReconciliationState.Unknown;

            var filenamesList = Directory.GetFiles(nextCurrentDirectory);
            foreach (var nextFileInCurrentDirectory in filenamesList) // Second check filter
            {
                if (!LocalImportService.IsSupportedImageFile(nextFileInCurrentDirectory)
                    || reconciliationList.Any(r => 
                        r.Key.LastFilePath == nextFileInCurrentDirectory)) continue;
                
                var existingFileSize = new FileInfo(nextFileInCurrentDirectory).Length;
                if (nextByteSize != existingFileSize) continue;
                
                nextLocalEntry.LastFilePath = nextFileInCurrentDirectory;
                reconciliationList[nextLocalEntry] = ReconciliationState.Correct;
                break;
            }

            var externalFileList = Directory.GetFiles(
                Path.Combine(osuMascotArchivePath, "osu-mascot-workspace", "local-entries"),
                "*.*", SearchOption.AllDirectories);
            foreach (var nextFileInParentDirectory in externalFileList) //Third check filter
            {
                if (!LocalImportService.IsSupportedImageFile(nextFileInParentDirectory)
                    || reconciliationList.Any(r => 
                        r.Key.LastFilePath == nextFileInParentDirectory)) continue;
                                
                var existingFileSize = new FileInfo(nextFileInParentDirectory).Length;
                if (nextByteSize != existingFileSize) continue;
                
                nextLocalEntry.LastFilePath = nextFileInParentDirectory;
                reconciliationList[nextLocalEntry] = ReconciliationState.Correct;
                break;
            }
            if(!reconciliationList.ContainsKey(nextLocalEntry)
               || reconciliationList[nextLocalEntry] != ReconciliationState.Correct) 
                reconciliationList[nextLocalEntry] = ReconciliationState.Unresolved;
            

        }

        var unresolvedLocalList = reconciliationList
            .Where(r => r.Value == ReconciliationState.Unresolved)
            .Select(r => r.Key);
        _localHashList.RemoveAll(e => unresolvedLocalList.Any(u => u.Hash == e.Hash));

        foreach (var entry in unresolvedLocalList)
        {
            reconciliationList[entry] = ReconciliationState.Removed;
        }
        
        foreach (var nextGlobalEntry in _globalHashList) // Global Asset Checker
        {
            var nextByteSize = nextGlobalEntry.ByteSize;
            var nextCurrentPath = nextGlobalEntry.LastFilePath;
            var nextHash = nextGlobalEntry.Hash;
            var checkHash = CalculateFileHash(nextCurrentPath);
            var nextCurrentDirectory = Path.GetDirectoryName(nextCurrentPath) ?? "";

            if (File.Exists(nextCurrentPath) // First check filter
                && checkHash == nextHash)
            {
                reconciliationList[nextGlobalEntry] = ReconciliationState.Correct;
                continue;
            }

            if (checkHash != nextHash) reconciliationList[nextGlobalEntry] = ReconciliationState.Unknown;

            var filenamesList = Directory.GetFiles(nextCurrentDirectory);
            foreach (var nextFileInCurrentDirectory in filenamesList) // Second check filter
            {
                if (!LocalImportService.IsSupportedImageFile(nextFileInCurrentDirectory)
                    || reconciliationList.Any(r => 
                        r.Key.LastFilePath == nextFileInCurrentDirectory)) continue;
                
                var existingFileSize = new FileInfo(nextFileInCurrentDirectory).Length;
                if (nextByteSize != existingFileSize) continue;
                
                nextGlobalEntry.LastFilePath = nextFileInCurrentDirectory;
                reconciliationList[nextGlobalEntry] = ReconciliationState.Correct;
                break;
            }

            var externalFileList = Directory.GetFiles(
                Path.Combine(osuMascotArchivePath, "osu-mascot-workspace"),
                "*.*", SearchOption.AllDirectories);
            foreach (var nextFileInParentDirectory in externalFileList) //Third check filter
            {
                if (!LocalImportService.IsSupportedImageFile(nextFileInParentDirectory)
                    || reconciliationList.Any(r => 
                        r.Key.LastFilePath == nextFileInParentDirectory)) continue;
                                
                var existingFileSize = new FileInfo(nextFileInParentDirectory).Length;
                if (nextByteSize != existingFileSize) continue;
                
                nextGlobalEntry.LastFilePath = nextFileInParentDirectory;
                reconciliationList[nextGlobalEntry] = ReconciliationState.Correct;
                break;
            }
            if(!reconciliationList.ContainsKey(nextGlobalEntry)
               || reconciliationList[nextGlobalEntry] != ReconciliationState.Correct) 
                reconciliationList[nextGlobalEntry] = ReconciliationState.Unresolved;
        }
        CollectUnlistedLocalAssets();
        //pensar que quizás estos assets puedan estar relacionados con alguna entrada de .md y que no estén registrados en las listas.
        return 0; //devolver un valor que indique cuantos archivos se han reconciliado.
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
        var currentHashList = File.ReadAllText(_localHashPath);
        var hashEntries = JsonSerializer.Deserialize<List<HashEntry>>(currentHashList) ?? [];
        hashEntries.Add(entry);

        var localJson = JsonSerializer.Serialize(hashEntries, new JsonSerializerOptions()
        { WriteIndented = true });
        File.WriteAllText(_localHashPath, localJson);
    }

    private void CollectUnlistedLocalAssets()
    {
        var allImages = Directory
            .GetFiles(Path.Combine(osuMascotArchivePath, "osu-mascot-workspace"), "*.*", SearchOption.AllDirectories)
            .Where(f => !f.Contains("local-entries") && LocalImportService.IsSupportedImageFile(f));

        foreach (var nextUnresolvedFile in allImages)
        {
            var hash = CalculateFileHash(nextUnresolvedFile);
            bool isListed = _localHashList.Any(e => e.Hash == hash)
                            || _globalHashList.Any(e => e.Hash == hash);
            if (isListed) continue;

            var unverifiedAssetPath = Path.Combine(
                osuMascotArchivePath, "osu-mascot-workspace", "99_Staging", "05_unverified", "unverified-assets",
                Path.GetFileName(nextUnresolvedFile));
            try
            {
                File.Copy(nextUnresolvedFile,
                    unverifiedAssetPath); //refactorizar esto para que sea más dinámico (soportará cambios en la estructura de directorios en el futuro)
            }
            catch(Exception error)
            {
                WriteReconciliationLog(Convert.ToString(error) ?? "Unknown unregistered exception", hash, nextUnresolvedFile);
                continue;
            }
            
            if (File.Exists(unverifiedAssetPath)) File.Delete(nextUnresolvedFile);
        } 
    }
    //hash lists directory checking helpers
    
    private void WriteReconciliationLog(string message, string relatedHash, string relatedPath)
    {
        var logEntry = new ReconciliationLogEntry()
        {
            LastKnownPath = relatedPath,
            Hash = relatedHash,
            EventType = message,
            OccurredAt = DateTime.Now.ToString("u")
        };
        var currentLogList = File.ReadAllText(_reconciliationLogPath);
        var logEntries = JsonSerializer.Deserialize<List<ReconciliationLogEntry>>(currentLogList) ?? [];
        
        logEntries.Add(logEntry);
        
        var logJson = JsonSerializer.Serialize(logEntries, new JsonSerializerOptions()
        { WriteIndented = true });
        File.WriteAllText(_reconciliationLogPath, logJson);
    }
    private enum ReconciliationState { Correct, Unresolved, Unknown, Removed }
    private enum EventType { Deleted, Moved, Changed, }
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
    
}