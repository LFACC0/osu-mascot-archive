using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.JavaScript;
using System.Text.RegularExpressions;
using osuArchiveToolkit.Models;

namespace osuArchiveToolkit.Services;

public class LocalImportService(string osuMascotArchivePath, string gitUser)
{
    private const string FrontmatterDelimiter = "---";

    public string LastEntryTempName { get; private set; } = "";
    public string LastImportedAssetPath = "";
    private HashManagerService _managerService = new(osuMascotArchivePath);
    
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

        // Reconciliation is orchestrated by MainWindow via IsHashFileDuplicated before ImportImage is called.
        if (File.Exists(destinationPath))
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
                standalone:
                official_id:
                dynamic_collab_id:
                curated_by:
                ---

                # 

                ## Preview

                ![[{tempFileId}]]

                ## Notes

                """;
    } //FOR LATER: migrate the "frontmatter" handling to a typed model (EntryMetadata in Models/),
    //reliably serializable, preserving the body of the .md. This was deferred so it would not block the flag.
    //Revisit if the metadata grows a lot or the temporary patch falls short.
    private static bool ListUnregisteredMarkdownEntries(string hashPath, string markdownSearchPath, string assetsSearchPath, HashManagerService.EntryScope scope)
    {
        var hashEntries = HashManagerService.LoadHashList(hashPath);
        var markdownFilesPaths = HashManagerService.GetAllMarkdownFilesRecursively(
            markdownSearchPath, true);
        var assetFilesPaths = HashManagerService.GetAllSupportedFilesRecursively(
            assetsSearchPath, true);

        foreach (var nextFile in markdownFilesPaths)
        {
            if (!string.IsNullOrEmpty(
                    hashEntries.FirstOrDefault(entry => entry.LastEntryPath == nextFile)?.LastEntryPath)) continue;
            
            var unlistedMarkdownFile = File.ReadAllText(nextFile);
            var linkedAssetNames = GetLinkedAssetNames(unlistedMarkdownFile);
            var missingAssetNames = linkedAssetNames
                .Where(assetName => ResolveLinkedAssetPath(assetName, assetFilesPaths) is null)
                .ToList();

            if (linkedAssetNames.Count == 0)
            {
                var isFileCorrupted = new FileInfo(nextFile).Length <= 10;
                
                
                //identify whether the .md file is empty. If it is empty and weighs less than 10 bits, it gets written to the reconciliation log and deleted.
                //otherwise, the .md string can be parsed to identify whether it has content of value. If there is information,
                //it will be moved to global or local unverified
                //in any other case, the .md will be moved to a temporary trash bin, global and/or local? (not created yet).
            }
            //pending: decide what these two pieces of data are for, according to initialdesigns.txt.
            //No linked names -> standalone candidate. Names that do not resolve -> warn the user.

        }

        return false;
    }
    internal static List<string> GetLinkedAssetNames(string markdownText)
    {
        var linkedAssetNames = new List<string>();

        AddLinkedAssetNames(MarkdownImageLink.WikiLink().Matches(markdownText), linkedAssetNames);
        AddLinkedAssetNames(MarkdownImageLink.InlineLink().Matches(markdownText), linkedAssetNames);

        return linkedAssetNames;
    }
    private static void AddLinkedAssetNames(MatchCollection linkMatches, List<string> linkedAssetNames)
    {
        foreach (Match nextLink in linkMatches)
        {
            var assetName = NormalizeLinkTarget(nextLink.Groups[MarkdownImageLink.TargetGroup].Value);

            if (!string.IsNullOrEmpty(assetName))
            {
                linkedAssetNames.Add(assetName);
            }
        }
    }
    private static string NormalizeLinkTarget(string rawTarget)
    {
        var trimmedTarget = rawTarget.Trim();

        if (string.IsNullOrEmpty(trimmedTarget))
        {
            return "";
        }

        // A hand-written link can arrive with %20 and with folder separators from any platform.
        var decodedTarget = Uri.UnescapeDataString(trimmedTarget).Replace('\\', '/');
        return Path.GetFileName(decodedTarget);
    }
    private static string? ResolveLinkedAssetPath(string assetName, IEnumerable<string> assetFilesPaths)
    {
        // Entries created by the system link without an extension (![[TEMP-00001]]), so both cases have to be covered.
        if (string.IsNullOrEmpty(Path.GetExtension(assetName)))
        {
            return assetFilesPaths.FirstOrDefault(assetPath => string.Equals(
                Path.GetFileNameWithoutExtension(assetPath), assetName, StringComparison.OrdinalIgnoreCase));
        }

        return assetFilesPaths.FirstOrDefault(assetPath => string.Equals(
            Path.GetFileName(assetPath), assetName, StringComparison.OrdinalIgnoreCase));
    }
    /// <summary>
    /// Reads the frontmatter of a .md into a key/value dictionary, in a single pass.
    /// Missing field = key that is not there; declared and empty field = key holding "".
    /// It only understands scalar fields (key: value): Obsidian lists (tags: with dashes below)
    /// leave the key holding "" and their items are ignored. No consumer needs them yet.
    /// </summary>
    internal static Dictionary<string, string> GetFrontmatterFields(string markdownText)
    {
        // OrdinalIgnoreCase because a human may type "Title:" and it is still the same field.
        var frontmatterFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var nextLine in GetFrontmatterLines(markdownText))
        {
            var separatorIndex = nextLine.IndexOf(':');

            // The first : and not a Split, because source_url: https://... carries more than one.
            // separatorIndex <= 0 discards lines without a key: list items, comments, garbage.
            if (separatorIndex <= 0) continue;

            var fieldName = nextLine[..separatorIndex].Trim();
            var fieldValue = NormalizeFieldValue(nextLine[(separatorIndex + 1)..]);

            // A frontmatter with a repeated key is invalid; we keep the first one and do not blow up.
            frontmatterFields.TryAdd(fieldName, fieldValue);
        }

        return frontmatterFields;
    }
    private static List<string> GetFrontmatterLines(string markdownText)
    {
        var frontmatterLines = new List<string>();
        var allLines = markdownText.Split('\n');

        // The block only counts if it opens on the first line: a --- in the body is a horizontal rule.
        if (allLines.Length == 0 || TrimLine(allLines[0]) != FrontmatterDelimiter)
        {
            return frontmatterLines;
        }

        foreach (var nextLine in allLines.Skip(1))
        {
            if (TrimLine(nextLine) == FrontmatterDelimiter)
            {
                return frontmatterLines;
            }

            frontmatterLines.Add(nextLine);
        }

        // Without a closing line the frontmatter is broken and we do not know where it ends:
        // we prefer to read nothing rather than swallow the whole body as if it were metadata.
        return [];
    }
    private static string TrimLine(string rawLine)
    {
        // The \r survives Split('\n') in files with Windows line endings,
        // and the BOM can come glued to the first ---.
        return rawLine.Trim().Trim('\uFEFF');
    }
    private static string NormalizeFieldValue(string rawValue)
    {
        var trimmedValue = rawValue.Trim();

        // Obsidian quotes the value when it contains : or #; the real content is what is inside.
        if (trimmedValue.Length >= 2
            && ((trimmedValue[0] == '"' && trimmedValue[^1] == '"')
                || (trimmedValue[0] == '\'' && trimmedValue[^1] == '\'')))
        {
            return trimmedValue[1..^1];
        }

        return trimmedValue;
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
    public enum ReadStandaloneState { False, Confirmed, Unconfirmed }
}

/// <summary>
/// Recognizes the two embedded image formats of a .md: the Obsidian wikilink (![[file.png|300]])
/// and the hand-written standard markdown (![alt](file.png)).
/// </summary>
internal static partial class MarkdownImageLink
{
    internal const string TargetGroup = "target";

    /// <summary>Obsidian wikilink: ![[file.png|300]]. The name stops at the | of the size.</summary>
    [GeneratedRegex(@"!\[\[(?<target>[^\]|]+)")]
    internal static partial Regex WikiLink();

    /// <summary>Hand-written standard markdown: ![alt](file.png "title"). The name stops at the space before the title.</summary>
    [GeneratedRegex(@"!\[[^\]]*\]\(\s*(?<target>[^)\s]+)")]
    internal static partial Regex InlineLink();
}