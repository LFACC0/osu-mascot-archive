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
using System.Xml;
using Avalonia.Controls;
using Avalonia.Controls.Converters;

namespace osuArchiveToolkit.Services;

public class HashManagerService(string osuMascotArchivePath)
{
    public string GlobalHashPath = "";
    public string LocalHashPath = "";
    private string _reconciliationLogPath = "";
    private readonly string[] _globalExcludedPaths = ["local_entries", "00_Home", "01_Templates", "duplicated-assets"];
    private List<HashEntry> _localHashList = [];
    private List<HashEntry> _globalHashList = [];
    private List<ReconciliationLogEntry> _reconciliationLogEntries = [];
    private IProgress<int>? _progress;
    public string LastDuplicatedFilePath { get; private set; } = "";

    public bool CreateListFiles()
    {
        if (!IsListsPathsAvailable())
        {
            Directory.CreateDirectory(GetGlobalListsPath());
            Directory.CreateDirectory(GetLocalHashPath());
        }
        GlobalHashPath = EnsureGlobalHashList();
        LocalHashPath = EnsureLocalHashList();
        _reconciliationLogPath = EnsureGlobalLogList();

        if (!File.Exists(GlobalHashPath)) 
            File.WriteAllText(GlobalHashPath, "[]");
        
        if (!File.Exists(LocalHashPath)) 
            File.WriteAllText(LocalHashPath, "[]");
        
        if (!File.Exists(_reconciliationLogPath)) 
            File.WriteAllText(_reconciliationLogPath, "[]");
        return true;
    }

    public bool IsHashFileDuplicated(string filePath)
    {
        var localHashList = LoadHashList(LocalHashPath);
        var globalHashList = LoadHashList(GlobalHashPath);
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

    private static bool IsFileStructuresEqualToLists(string selectedPath, List<HashEntry> hashList, params string[] excludedPaths)
    {
        
        var selectedFileAssetsBytes = GetAllSupportedFilesRecursively(selectedPath, true, excludedPaths).Sum(f => new FileInfo(f).Length);
        var markdownFileCount = GetAllMarkdownFilesRecursively(selectedPath, true, excludedPaths).Count();
        var selectedListAssetsBytes = hashList.Sum(e => e.ByteSize);
        var markdownListCount = hashList.Select(e => e.LastEntryPath).Distinct().Count();

        return (selectedFileAssetsBytes == selectedListAssetsBytes && markdownListCount == markdownFileCount);
    }
    public int ReconcileLocalHashListWithCurrentAssets(IProgress<int>? progress = null)
    {
        var localEntriesPath = Path.Combine(osuMascotArchivePath, "osu-mascot-workspace", "local_entries"); //HARDCODING WARNING
        _progress = progress;
        try
        {
            _localHashList = LoadHashList(LocalHashPath);
            if (IsFileStructuresEqualToLists(localEntriesPath, _localHashList)) return 0;
            var reconciliationList = new Dictionary<HashEntry, ReconciliationState>();

            // 1. Local reconciliation: fixes the paths of the local assets against the disk.
            var correctedCount = ReconcileHashEntries(_localHashList, localEntriesPath, reconciliationList);

            // Local entries that could not be resolved are removed from the local list.
            var unresolvedLocal = reconciliationList
                .Where(r => r.Value == ReconciliationState.Unresolved)
                .Select(r => r.Key)
                .ToList();
            _localHashList.RemoveAll(e => unresolvedLocal.Any(u => u.Hash == e.Hash));
            foreach (var entry in unresolvedLocal)
            {
                reconciliationList[entry] = ReconciliationState.Removed;
                WriteReconciliationLog("",nameof(EventType.Deleted), entry.Hash, entry.LastFilePath);
            }
            correctedCount += unresolvedLocal.Count;

            // The local list is persisted here: local work stays valid even if the pull fails afterwards.
            PersistHashList(_localHashList, LocalHashPath);
            return correctedCount;
        }
        finally
        {
            PersistReconciliationLog(_reconciliationLogEntries); //what happens if this function cannot write? Every file change stays in memory and is lost. Revisit this later.
            _reconciliationLogEntries = [];
        }
    }
    public int ReconcileGlobalHashListWithCurrentAssets(IProgress<int>? progress = null)
    {
        _progress = progress;
        try
        {
            _globalHashList = LoadHashList(GlobalHashPath);
            var globalSearchDirectory = Path.Combine(osuMascotArchivePath, "osu-mascot-workspace"); //HARDCODED PATH WARNING HERE
            
            // 2. git pull. If syncing with the remote fails, we abort before touching anything global.
            var correctedCount = 0;
            if (!CheckGitState()) return correctedCount;
            if (IsFileStructuresEqualToLists(globalSearchDirectory, _globalHashList, _globalExcludedPaths)) return correctedCount;

            // 3. Global reconciliation: the repo is already up to date from the remote (double check).
            var reconciliationList = new Dictionary<HashEntry, ReconciliationState>();
            correctedCount += ReconcileHashEntries(_globalHashList, globalSearchDirectory, reconciliationList, _globalExcludedPaths);

            CollectUnlistedGlobalAssets();
            PersistHashList(_globalHashList, GlobalHashPath);
            return correctedCount;
        }
        finally
        {
            PersistReconciliationLog(_reconciliationLogEntries); //what happens if this function cannot write? Every file change stays in memory and is lost. Revisit this later.
            _reconciliationLogEntries = [];
        }
    }
    public void WriteHashEntry(string gitUser, string lastImportedAssetPath, string lastEntryPath, EntryScope scope)
    {
        var entry = new HashEntry()
        {
            Hash = CalculateFileHash(lastImportedAssetPath),
            HashCreatedAt = DateTime.UtcNow.ToString("u"),
            SubmittedBy = gitUser,
            LastFilePath = lastImportedAssetPath,
            ByteSize = new FileInfo(lastImportedAssetPath).Length,
            LastEntryPath = lastEntryPath
        };

        var hashEntries = LoadHashList(scope == EntryScope.Local ? LocalHashPath : GlobalHashPath);
        hashEntries.Add(entry);
        PersistHashList(hashEntries, LocalHashPath);
    }

    private string CalculateFileHash(string filePath)
    {
        if (!File.Exists(filePath)) return "";
        using var openFile = File.OpenRead(filePath);
        var hashFileBytes = SHA256.HashData(openFile);
        var hashBytesText = Convert.ToHexString(hashFileBytes).ToLowerInvariant();

        return hashBytesText;
    }

    // Reconciles a hash list against the disk. Returns how many entries were relocated.
    // Shares the state dictionary with the caller so the same file is never claimed twice.
    private int ReconcileHashEntries(
        List<HashEntry> hashList,
        string externalSearchDirectory,
        Dictionary<HashEntry, ReconciliationState> reconciliationList,
        params string[] excludedPaths)
    {
        var relocatedCount = 0;

        foreach (var entry in hashList)
        {
            var currentPath = entry.LastFilePath;
            var currentDirectory = Path.GetDirectoryName(currentPath) ?? "";
            var checkHash = CalculateFileHash(currentPath);

            // Filter 1: the file is still where we expect it and its content did not change.
            if (File.Exists(currentPath) && checkHash == entry.Hash)
            {
                reconciliationList[entry] = ReconciliationState.Correct;
                continue;
            }

            // The file exists at its path but its content changed: the event gets logged.
            if (checkHash != entry.Hash)
            {
                reconciliationList[entry] = ReconciliationState.Stale;
                if (File.Exists(currentPath))
                    WriteReconciliationLog("",nameof(EventType.Changed), entry.Hash, currentPath);
            }

            // Filter 2: same directory. Filter 3: the external search directory.
            // Both match by size in bytes.
            if (TryRelocateBySize(entry, currentDirectory, reconciliationList, recursive: false, excludedPaths)
                || TryRelocateBySize(entry, externalSearchDirectory, reconciliationList, recursive: true, excludedPaths))
            {
                relocatedCount++;
                WriteReconciliationLog("",nameof(EventType.Moved), entry.Hash, entry.LastFilePath);
                continue;
            }

            if (!reconciliationList.ContainsKey(entry)
                || reconciliationList[entry] != ReconciliationState.Correct)
                reconciliationList[entry] = ReconciliationState.Unresolved;
        }

        return relocatedCount;
    }

    // Looks in searchDirectory for a supported, not yet claimed file with the same size as the entry.
    // If it finds one, it updates its LastFilePath and marks it as Correct.
    private static bool TryRelocateBySize(
        HashEntry entry,
        string searchDirectory,
        Dictionary<HashEntry, ReconciliationState> reconciliationList,
        bool recursive,
        params string[] excludedPaths)
    {
        var selectedFiles = GetAllSupportedFilesRecursively(searchDirectory, recursive, excludedPaths);

        foreach (var candidate in selectedFiles)
        {
            if (reconciliationList.Any(r => r.Key.LastFilePath == candidate)) continue;
            if (new FileInfo(candidate).Length != entry.ByteSize) continue;

            entry.LastFilePath = candidate;
            reconciliationList[entry] = ReconciliationState.Correct;
            return true;
        }

        return false;
    }

    public static List<HashEntry> LoadHashList(string path) =>
        JsonSerializer.Deserialize<List<HashEntry>>(File.ReadAllText(path)) ?? [];

    private static void PersistHashList(List<HashEntry> hashList, string path)
    {
        var json = JsonSerializer.Serialize(hashList, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    private void CollectUnlistedGlobalAssets()
    {
        //refactor this to be more dynamic (it will have to support directory structure changes in the future)
        var unverifiedDirectory = Path.Combine(
            osuMascotArchivePath, "osu-mascot-workspace", "99_Staging", "05_unverified", "unverified-assets");
        Directory.CreateDirectory(unverifiedDirectory);

        var osuWorkspacePath = Path.Combine(osuMascotArchivePath, "osu-mascot-workspace");

        // The destination folder itself is excluded so already collected assets are not reprocessed (nor recopied over themselves).
        var allImages = GetAllSupportedFilesRecursively(osuWorkspacePath, true, [.._globalExcludedPaths, unverifiedDirectory]);

        foreach (var nextUnresolvedFile in allImages)
        {
            var hash = CalculateFileHash(nextUnresolvedFile);
            var matchedLocalEntry = _localHashList.FirstOrDefault(e => e.Hash == hash);
            var matchedGlobalEntry = _globalHashList.FirstOrDefault(e => e.Hash == hash);
            var matchedPath = matchedGlobalEntry?.LastFilePath ?? matchedLocalEntry?.LastFilePath;
            var isListed = !string.IsNullOrEmpty(matchedPath);
            var nextFilePath = Path.GetFullPath(nextUnresolvedFile);
            var isCanonical = isListed && string.Equals(nextFilePath, Path.GetFullPath(matchedPath!),
                StringComparison.Ordinal) && File.Exists(matchedPath);
            
            if (File.Exists(matchedPath) && isListed && isCanonical ) continue;

            if (!File.Exists(matchedPath) && isListed && matchedGlobalEntry is not null)
            { 
                matchedGlobalEntry.LastFilePath = nextFilePath; 
                WriteReconciliationLog("Redirected Asset Path of existent Global Entry with valid new asset", nameof(EventType.Adopted), 
                    hash, "Related Entry: " + matchedGlobalEntry.LastEntryPath);
                continue;

            }

            if (matchedGlobalEntry is not null && matchedLocalEntry is not null)
            {
                WriteReconciliationLog("How.",
                    nameof(EventType.Unexpected), hash, 
                    $"{matchedGlobalEntry.LastEntryPath} has the same asset as {matchedLocalEntry.LastEntryPath}");
                continue;
            }
            if (matchedLocalEntry is not null)
            {
                WriteReconciliationLog("Global Asset is matching a local Entry", nameof(EventType.Unexpected), hash,
                    $"Asset path {nextFilePath} matches entry located at {matchedLocalEntry.LastFilePath}");
                continue;
            }
            
            var unverifiedAssetPath = Path.Combine(unverifiedDirectory, hash + "_" + Path.GetFileName(nextUnresolvedFile));
            var duplicatedAssetPath = Path.Combine(osuWorkspacePath, "99_Staging",
                "04_possible-duplicates", "duplicated-assets", hash + "_" + Path.GetFileName(nextUnresolvedFile));
            try
            {
                if (File.Exists(matchedPath))
                {
                    File.Copy(nextUnresolvedFile, duplicatedAssetPath, true);
                    if (!File.Exists(duplicatedAssetPath))
                    {
                        WriteReconciliationLog("An error occured trying to move the duplicated asset", 
                            nameof(EventType.Failed), hash, $"from {nextUnresolvedFile} to {duplicatedAssetPath}");
                        continue;
                    }
                    File.Delete(nextUnresolvedFile);
                    WriteReconciliationLog("",nameof(EventType.Duplicated), hash, duplicatedAssetPath);
                    continue;
                }
                File.Copy(nextUnresolvedFile, unverifiedAssetPath, true);
            }
            catch (Exception error)
            {
                WriteReconciliationLog(
                    Convert.ToString(error) ?? "Unknown unregistered exception",
                    nameof(EventType.Failed), hash, nextUnresolvedFile);
                continue;
            }

            if (!File.Exists(unverifiedAssetPath))
            {
                WriteReconciliationLog("An error occured trying to move the unverified asset", 
                    nameof(EventType.Failed), hash, $"from {nextFilePath} to {unverifiedAssetPath}");
                continue;
            }
            File.Delete(nextUnresolvedFile); 
            WriteReconciliationLog("",nameof(EventType.Collected), hash, unverifiedAssetPath);
        }
    }

    public bool IsDirectoryAssetsTotalSizeEqualToLists()
    {
        var localEntriesPath = Path.Combine(osuMascotArchivePath, "osu-mascot-workspace", "local_entries"); //HARDCODING WARNING
        var globalSearchDirectory = Path.Combine(osuMascotArchivePath, "osu-mascot-workspace"); //HARDCODED PATH WARNING HERE
        var isGlobalStructureMatchingLists = IsFileStructuresEqualToLists(globalSearchDirectory, LoadHashList(GlobalHashPath), _globalExcludedPaths);
        var isLocalStructureMatchingLists = IsFileStructuresEqualToLists(localEntriesPath, LoadHashList(LocalHashPath));
        

        return (isLocalStructureMatchingLists && isGlobalStructureMatchingLists);
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
    
    
    private void WriteReconciliationLog(string message, string eventType, string relatedHash, string relatedPath)
    {
        var logEntry = new ReconciliationLogEntry()
        {
            LastKnownPath = relatedPath,
            Hash = relatedHash,
            EventType = eventType,
            Message = message,
            OccurredAt = DateTime.Now.ToString("u")
        };
        _reconciliationLogEntries.Add(logEntry);
        _progress?.Report(_reconciliationLogEntries.Count);
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

    public (RepoStatus IsComplete, string MessageOutput) RepoUpdateState()
    {
        var (globalOutput, globalExitCode) = RunGitCommand("ls-remote origin -h refs/heads/main");
        var (localOutput, localExitCode) = RunGitCommand("rev-parse main");

        var globalSha = globalOutput.Split('\t')[0];
        var localSha = localOutput.Split('\t')[0];
        var shasMatch = localSha == globalSha;
        var writeLog = $"git: {globalOutput} \n" + $"git: {localOutput}";
        
        if (globalExitCode != 0 || localExitCode != 0)
        {
            return (RepoStatus.CheckFailed, 
                writeLog);
        }
        return shasMatch ? (RepoStatus.UpToDate, "") 
            : (RepoStatus.OutDated,writeLog);
    }

    private (string output, int exitCode) RunGitCommand(string arguments)
    {
        var process = new Process();

        process.StartInfo.FileName = "git";
        process.StartInfo.Arguments = arguments;
        process.StartInfo.WorkingDirectory = osuMascotArchivePath;
        process.StartInfo.RedirectStandardError = true;

        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.UseShellExecute = false;
        process.Start();
        
        var output = process.StandardOutput.ReadToEnd().Trim(); 
        if(string.IsNullOrEmpty(output)) output = process.StandardError.ReadToEnd().Trim();
        process.WaitForExit();
        return (output, process.ExitCode);
    } //UNSOLVED WARNING: possible deadlock caused by buffer cap (~4KB of available space) in long stream calls.

    public static IEnumerable<string> GetAllSupportedFilesRecursively(
        string specificPath, bool recursive = true, params string[] excludedPaths)
    {
        if (!Directory.Exists(specificPath)) return [];
        
        var rawFilesList= Directory
            .GetFiles(specificPath, "*.*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly) //REMOVE THE HARDCODED PATH HERE. CAREFUL.
            .Where(f => LocalImportService.IsSupportedImageFile(f) && !excludedPaths.Any(f.Contains));
        return rawFilesList;
    }

    public static IEnumerable<string> GetAllMarkdownFilesRecursively(
        string selectedPath, bool recursive = true, params string[] excludedPaths)
    {
        return Directory.GetFiles(selectedPath, "*.md",
                recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .Where(f => !excludedPaths.Any(f.Contains));
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

    private enum ReconciliationState { Correct, Unresolved, Stale, Removed}
    private enum EventType { Deleted, Moved, Changed, Collected, Failed, Duplicated, Adopted, Unexpected } 
    public enum RepoStatus { UpToDate, OutDated, CheckFailed }
    public enum EntryScope {Global, Local} 

}