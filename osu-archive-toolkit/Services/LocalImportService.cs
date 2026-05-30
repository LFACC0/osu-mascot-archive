using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
namespace osuArchiveToolkit.Services;

public class LocalImportService
{
    private readonly string _workspacePath;
    private readonly string _gitUser;
    public string LastEntryTempName { get; private set; } = "";
    
    public LocalImportService(string workspacePath, string gitUser)
    {
        this._workspacePath = workspacePath;
        this._gitUser = gitUser;
    }
    
    //HELPERS
    private bool IsSupportedImageFile(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        return extension == ".png"
               || extension == ".jpg"
               || extension == ".jpeg"
               || extension == ".webp"
               || extension == ".gif";
    }

    public void ValidateFiles(string selectedFile)
    {
        if (!IsSupportedImageFile(selectedFile))
        {
            throw new InvalidOperationException($"Unsupported image type: {Path.GetExtension(selectedFile)}");
        }

        EnsureLocalDirectories();

    }
    public string ImportImage(string fileName)
    {
        var tempFileId = RenameImportingFile(fileName);
        var destinationPath = GetLocalAssetPath(tempFileId);
        var markdownPath = GetLocalEntryMarkdownPath();

        if (File.Exists(destinationPath)) //SOLUCIONAR ESTA OPERACIÓN. ACTUALMENTE, NO SE BLOQUEA Y GENERA
        //ENTRADAS INCREMENTALES SIN TENER EN CUENTA EL CONTENIDO. LO IDEAL ES QUE LEA EL LOCAL HASH
        //LIST GENERADO POR GIT USER. 
        {
            throw new InvalidOperationException($"Asset already exists: {destinationPath}");
        }
        if (File.Exists(markdownPath))
        {
            throw new InvalidOperationException($"Entry already exists: {Path.GetFileName(markdownPath)}");
        }
        
        File.Copy(fileName, destinationPath, false);
        var markdownContent = BuildLocalEntryMarkdown(tempFileId);
        File.WriteAllText(markdownPath, markdownContent);
        LastEntryTempName = markdownPath;
        return tempFileId;
    }
    private string RenameImportingFile(string importedFileName)
    {
        var extension = Path.GetExtension(importedFileName).ToLowerInvariant();
        var temporaryLocalFileName = NextTemporaryLocalLabel() + extension;
        return temporaryLocalFileName;
    }
    
    private void EnsureLocalDirectories()
    {
        Directory.CreateDirectory(GetLocalAssetsPath());
        Directory.CreateDirectory(GetLocalEntriesPath());
    }
    private string GetLocalAssetPath(string fileName)
    {
        return Path.Combine(
            GetLocalAssetsPath(),
            fileName
        );
    }
    private string GetLocalAssetsPath()
    {
        return Path.Combine(
            _workspacePath,
            "local_entries",
            "local_assets"
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
    private string GetLocalEntryMarkdownPath()
    {
        var entryFolder = GetLocalEntriesPath();

        return Path.Combine(
            entryFolder,
            $"{NextTemporaryLocalLabel()}.md"
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
submitted_by: {_gitUser}
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
    public string GetLocalEntriesPath()
    {
        return Path.Combine(
            _workspacePath,
            "local_entries"
        );
    }
}