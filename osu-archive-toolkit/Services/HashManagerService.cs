using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices.JavaScript;
using System.Security.Cryptography;
using System.Text.Json;
using osuArchiveToolkit.Models;
using System.Linq;
using Avalonia.Controls.Converters;

namespace osuArchiveToolkit.Services;

public class HashManagerService(string osuMascotArchivePath)
{
    private string _globalHashPath = "";
    private string _localHashPath = "";
    private string _localHashFileString = "";
    public string LastDuplicatedFilePath { get; private set; } = "";
    
    public bool CreateHashFiles()
    {
        if (!IsHashPathAvailable())
        {
            Directory.CreateDirectory(GetGlobalHashPath());
            Directory.CreateDirectory(GetLocalHashPath());
        }
        _globalHashPath = EnsureGlobalHashList();
        _localHashPath = EnsureLocalHashList();

        if (!File.Exists(_globalHashPath)) 
            File.WriteAllText(_globalHashPath, "[]");
        
        if (!File.Exists(_localHashPath)) 
            File.WriteAllText(_localHashPath, "[]");
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
        var globalHashList = JsonSerializer.Deserialize<List<HashEntry>>(
            File.ReadAllText(_globalHashPath)) ?? [];
        var localHashList = JsonSerializer.Deserialize<List<HashEntry>>(
            File.ReadAllText(_localHashPath)) ?? [];
        var reconciliationList = new Dictionary<HashEntry, ReconciliationState>(); //usar esto para descartar archivos reconciliados
        
        foreach (var compareMetadata in localHashList) // Local Asset Checker TODO
        {
            var nextByteSize = compareMetadata.ByteSize;
            var nextCurrentPath = compareMetadata.LastFilePath;
            var nextHash = compareMetadata.Hash;
            var checkHash = CalculateFileHash(nextCurrentPath);
            var nextCurrentDirectory = Path.GetDirectoryName(nextCurrentPath) ?? "";

            if (File.Exists(nextCurrentPath) // First check filter
                && checkHash == nextHash)
            {
                reconciliationList[compareMetadata] = ReconciliationState.Correct;
                continue;
            }

            if (checkHash != nextHash) reconciliationList[compareMetadata] = ReconciliationState.Unknown;

            var filenamesList = Directory.GetFiles(nextCurrentDirectory);
            foreach (var nextFileInCurrentDirectory in filenamesList) // Second check filter
            {
                if (!LocalImportService.IsSupportedImageFile(nextFileInCurrentDirectory)
                    || reconciliationList.Any(r => 
                        r.Key.LastFilePath == nextFileInCurrentDirectory)) continue;
                
                var existingFileSize = new FileInfo(nextFileInCurrentDirectory).Length;
                if (nextByteSize != existingFileSize) continue;
                
                compareMetadata.LastFilePath = nextFileInCurrentDirectory;
                reconciliationList[compareMetadata] = ReconciliationState.Correct;
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
                
                compareMetadata.LastFilePath = nextFileInParentDirectory;
                reconciliationList[compareMetadata] = ReconciliationState.Correct;
                break;
            }
            if(!reconciliationList.ContainsKey(compareMetadata)
               || reconciliationList[compareMetadata] != ReconciliationState.Correct) 
                reconciliationList[compareMetadata] = ReconciliationState.Unresolved;
        }
        

        //TODO identificar assets que no estén registrados y no estén relacionados a ninguna entrada, y moverlos a una carpeta especial para ser revisada manualmente después.
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
        var hashEntries = JsonSerializer.Deserialize<List<HashEntry>>(currentHashList)
                          ?? [];
        hashEntries.Add(entry);

        var localJson = JsonSerializer.Serialize(hashEntries, new JsonSerializerOptions()
        {
            WriteIndented = true
        });
        File.WriteAllText(_localHashPath, localJson);
    }
    //hash lists directory checking helpers
    private enum ReconciliationState { Correct, Unresolved, Unknown }
    private bool IsHashPathAvailable()
    {
        return !string.IsNullOrWhiteSpace(osuMascotArchivePath)
            && Directory.Exists(GetLocalHashPath())
            && Directory.Exists(GetGlobalHashPath());
    }
    private string EnsureGlobalHashList()
    {
        var globalHashFolder = GetGlobalHashPath();

        return Path.Combine(
            globalHashFolder,
            $"GlobalAssetsHashList.json"
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
    private string GetGlobalHashPath()
    {
        return Path.Combine(
            osuMascotArchivePath,
            ".Models"
        );
    }
    
}