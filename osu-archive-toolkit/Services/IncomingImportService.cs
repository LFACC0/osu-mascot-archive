using System;

namespace osuArchiveToolkit.Services;

public class IncomingImportService
{
    private readonly string workspacePath;
    private readonly string gitUser;

    public IncomingImportService(string workspacePath, string gitUser)
    {
        this.workspacePath = workspacePath;
        this.gitUser = gitUser;
    }
    
    //HELPERS
    private bool IsSupportedImageFile(string fileName)
    {
        var extension = System.IO.Path.GetExtension(fileName).ToLowerInvariant();

        return extension == ".png"
               || extension == ".jpg"
               || extension == ".jpeg"
               || extension == ".webp"
               || extension == ".gif";
    }

    public string ImportImage(string selectedFile)
    {
        var fileName = System.IO.Path.GetFileName(selectedFile);
        if (!IsSupportedImageFile(fileName))
        {
            throw new InvalidOperationException($"Unsupported image type: {System.IO.Path.GetExtension(fileName)}");
        }
        EnsureIncomingDirectories();

        var destinationPath = GetIncomingAssetPath(fileName);
        var markdownPath = GetIncomingEntryMarkdownPath(fileName);

        if (System.IO.File.Exists(destinationPath))
        {
            throw new InvalidOperationException($"Asset already exists: {fileName}");
        }
        if (System.IO.File.Exists(markdownPath))
        {
            throw new InvalidOperationException($"Entry already exists: {System.IO.Path.GetFileName(markdownPath)}");
        }

        System.IO.File.Copy(selectedFile, destinationPath, false);
        
        var markdownContent = BuildIncomingEntryMarkdown(fileName);
        System.IO.File.WriteAllText(markdownPath, markdownContent);

        return fileName;
    }
    private void EnsureIncomingDirectories()
    {
        System.IO.Directory.CreateDirectory(GetIncomingAssetsPath());
        System.IO.Directory.CreateDirectory(GetIncomingEntriesPath());
    }
    private string GetIncomingAssetPath(string fileName)
    {
        return System.IO.Path.Combine(
            GetIncomingAssetsPath(),
            fileName
        );
    }
    private string GetIncomingAssetsPath()
    {
        return System.IO.Path.Combine(
            workspacePath,
            "98_Incoming",
            "Incoming-assets"
        );
    }

    private string GetIncomingEntryMarkdownPath(string fileName)
    {
        var entryFolder = GetIncomingEntriesPath();

        return System.IO.Path.Combine(
            entryFolder,
            $"{System.IO.Path.GetFileNameWithoutExtension(fileName)}.md"
        );
    }

    private string BuildIncomingEntryMarkdown(string fileName)
    {
        var title = System.IO.Path.GetFileNameWithoutExtension(fileName);
        var createdAt = DateTime.UtcNow.ToString("u");
        
        return $@"---
title: {title}
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
curated_by:
---

# {title}

## Preview

![[{fileName}]]

## Notes
";
    }
    private string GetIncomingEntriesPath()
    {
        return System.IO.Path.Combine(
            workspacePath,
            "98_Incoming",
            "Incoming-entries"
        );
    }
}