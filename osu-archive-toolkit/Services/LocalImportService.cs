using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using static osuArchiveToolkit.Services.HashManagerService;
namespace osuArchiveToolkit.Services;

public class LocalImportService(string osuMascotArchivePath, string gitUser)
{
    public string LastEntryTempName { get; private set; } = "";
    public string LastImportedAssetPath = "";

    //HELPERS
    public static bool IsSupportedImageFile(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        return extension == ".png"
               || extension == ".jpg"
               || extension == ".jpeg"
               || extension == ".webp"
               || extension == ".gif";
    }

    public void ValidateFiles(string selectedFilePath)
    {
        if (!IsSupportedImageFile(selectedFilePath))
        {
            throw new InvalidOperationException($"Unsupported image type: {Path.GetExtension(selectedFilePath)}");
        }

        EnsureLocalDirectories();

    }
    public string ImportImage(string selectedFilePath)
    {
        var tempLocalLabel = NextTemporaryLocalLabel();
        var newImportingFileName = RenameImportingFile(selectedFilePath, tempLocalLabel);
        var destinationPath = GetLocalAssetPath(newImportingFileName);
        var markdownPath = GetLocalEntryMarkdownPath(tempLocalLabel);

        if (File.Exists(destinationPath)) //Modificar esta operación para que verifique duplicados por contenido, no por directorio. 
        { 
            throw new InvalidOperationException($"Asset already exists: {destinationPath}");
        }
        if (File.Exists(markdownPath))
        {
            throw new InvalidOperationException($"Entry already exists: {Path.GetFileName(markdownPath)}");
        }
        
        File.Copy(selectedFilePath, destinationPath, false);
        LastImportedAssetPath = destinationPath;
        var markdownContent = BuildLocalEntryMarkdown(newImportingFileName);
        File.WriteAllText(markdownPath, markdownContent);
        LastEntryTempName = markdownPath;
        return newImportingFileName;
    }
    private string RenameImportingFile(string importedFileName, string tempLocalLabel)
    {
        var extension = Path.GetExtension(importedFileName).ToLowerInvariant();
        var temporaryLocalFileName = tempLocalLabel + extension;
        return temporaryLocalFileName;
    }
    
    private void EnsureLocalDirectories()
    {
        Directory.CreateDirectory(GetLocalAssetsPath());
        Directory.CreateDirectory(GetLocalEntriesPath());
    }
    private string GetLocalAssetPath(string newImportingFileName)
    {
        return Path.Combine(
            GetLocalAssetsPath(),
            newImportingFileName
        );
    }

    
    private int NextIdNumberAvailable()
    {
        List<int> idNumbers = new List<int>();
        var entriesList = Directory.GetFiles(
                GetLocalEntriesPath(), "TEMP-*.md");
        if (entriesList.Length == 0)
        {
            return 1;
        }
        foreach (var filePath in entriesList)
        {
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            string textNumber = fileName.Substring(fileName.Length - 5);
            int number = int.Parse(textNumber);
            idNumbers.Add(number);
        }
        int nextIdValue = idNumbers.Max() + 1;
        return nextIdValue;
    }
    private string GetLocalEntryMarkdownPath(string tempLocalLabel)
    {
        var entryFolder = GetLocalEntriesPath();

        return Path.Combine(
            entryFolder,
            $"{tempLocalLabel}.md"
        );
    }

    private string NextTemporaryLocalLabel()
    {
        int number = NextIdNumberAvailable();
        string fiveDigitNumber = number.ToString("D5");
        
        return "TEMP-" + fiveDigitNumber;
    }
    private string BuildLocalEntryMarkdown(string tempFileId)
    {
        var createdAt = DateTime.UtcNow.ToString("u");
        return $@"---
title: 
created_at: {createdAt}
submitted_by: {gitUser}
tags:
date:
characters:
artists:
type:
canon:
sources:
source_url:
related:
status:
official_id:
dynamic_collab_id:
curated_by:
---

# 

## Preview

![[{tempFileId}]]

## Notes
";
    }

    private string GetLocalEntriesPath()
    {
        return Path.Combine(
            osuMascotArchivePath,
            "osu-mascot-workspace",
            "local_entries"
        );
    }
    private string GetLocalAssetsPath()
    {
        return Path.Combine(
            osuMascotArchivePath,
            "osu-mascot-workspace",
            "local_entries",
            "local_assets"
        );
    }
}