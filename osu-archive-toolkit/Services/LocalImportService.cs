using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.JavaScript;
using Avalonia.Logging;
namespace osuArchiveToolkit.Services;

public class LocalImportService(string osuMascotArchivePath, string gitUser)
{
    public string LastEntryTempName { get; private set; } = "";
    public string LastImportedAssetPath = "";
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

        // La reconciliación la orquesta MainWindow vía IsHashFileDuplicated antes de llamar a ImportImage.
        if (File.Exists(destinationPath)) //Modificar esta operación para que verifique duplicados por contenido, no por directorio. //colocar aquí el checkgitstate y reconciliationmanager
        { 
            throw new InvalidOperationException($"Asset already exists: {destinationPath}");
        }
        if (File.Exists(markdownPath))
        {
            throw new InvalidOperationException($"Entry already exists: {Path.GetFileName(markdownPath)}"); //esto no pasará. si
        }
        
        File.Copy(selectedFilePath, destinationPath, false);
        LastImportedAssetPath = destinationPath;
        var markdownContent = BuildLocalEntryMarkdown(newImportingFileName);
        File.WriteAllText(markdownPath, markdownContent);
        LastEntryTempName = markdownPath;
        return newImportingFileName;
    }
    public static bool IsSupportedImageFile(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        return extension == ".png"
               || extension == ".jpg"
               || extension == ".jpeg"
               || extension == ".webp"
               || extension == ".gif";
    }
    public static void WriteLog(string errorText, string archivePath, [CallerMemberName] string caller = "")
    {
        var logPath = Path.Combine(archivePath, ".ErrorLog");
        var logEntry = $"> [{DateTime.UtcNow:u}] in {caller}: {errorText}";
        
        if (!File.Exists(logPath) || string.IsNullOrEmpty(File.ReadAllText(logPath)))
        {
            File.WriteAllText(
                Path.Combine(archivePath, ".ErrorLog"),
                $"{logEntry}\n");
            return;
        }

        var currentLogContent = File.ReadAllText(logPath);
        File.WriteAllText(logPath,
            $"{currentLogContent}\n {logEntry}");
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

    private string NextTemporaryLocalLabel()
    {
        var number = NextIdNumberAvailable();
        var fiveDigitNumber = number.ToString("D5");
        
        return "TEMP-" + fiveDigitNumber;
    }
    private int NextIdNumberAvailable()
    {
        var idNumbers = new List<int>();
        var entriesList = Directory.GetFiles(
            GetLocalEntriesPath(), "TEMP-*.md");
        if (entriesList.Length == 0)
        {
            return 1;
        }
        foreach (var filePath in entriesList)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var textNumber = fileName.Substring(fileName.Length - 5);
            var number = int.Parse(textNumber);
            idNumbers.Add(number);
        }
        var nextIdValue = idNumbers.Max() + 1;
        return nextIdValue;
    }
    private string BuildLocalEntryMarkdown(string tempFileId)
    {
        var createdAt = DateTime.UtcNow.ToString("u");
        return $"""
                ---
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

                """;
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
    private string GetLocalAssetPath(string newImportingFileName)
    {
        return Path.Combine(
            GetLocalAssetsPath(),
            newImportingFileName
        );
    }
    private string GetLocalEntryMarkdownPath(string tempLocalLabel)
    {
        var entryFolder = GetLocalEntriesPath();

        return Path.Combine(
            entryFolder,
            $"{tempLocalLabel}.md"
        );
    }


}