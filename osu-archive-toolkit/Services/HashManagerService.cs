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
        var localHashFile = File.ReadAllText(_localHashPath);
        var globalHashFile = File.ReadAllText(_globalHashPath);
        var globalHashList = JsonSerializer.Deserialize<List<HashEntry>>(globalHashFile) ?? [];
        var localHashList = JsonSerializer.Deserialize<List<HashEntry>>(localHashFile) ?? [];
        var localFileHash = CalculateLocalFileHash(filePath);

        var duplicatedEntry = localHashList.FirstOrDefault(entry => entry.Hash == localFileHash);
        if (duplicatedEntry != null)
        {
            LastDuplicatedFilePath = duplicatedEntry.LastFilePath;
            return true;
        };
        duplicatedEntry = globalHashList.FirstOrDefault(entry => entry.Hash == localFileHash);
        if (duplicatedEntry != null)
        {
            LastDuplicatedFilePath = duplicatedEntry.LastFilePath;
            return true;
        };
        return false;
    }

    private string CalculateLocalFileHash(string filePath)
    {
        using var openFile = File.OpenRead(filePath);
        var hashFileBytes = SHA256.HashData(openFile);
        var hashBytesText = Convert.ToHexString(hashFileBytes).ToLowerInvariant();
        _localHashFileString = hashBytesText;

        return hashBytesText;
    }

    public void WriteLocalHashEntry(string gitUser, string lastImportedAssetPath)
    {

        var entry = new HashEntry()
        {
            Hash = _localHashFileString,
            HashCreatedAt = DateTime.UtcNow.ToString("u"),
            SubmittedBy = gitUser,
            LastFilePath = lastImportedAssetPath
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