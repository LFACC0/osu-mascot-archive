using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System.Diagnostics;
namespace osuArchiveToolkit;

public partial class MainWindow : Window
{
    private string gitUser = "Unknown";
    private string workspacePath = "";
    private bool intakeModeEnabled = false;
    public MainWindow()
    {
        InitializeComponent();
        StatusLog.Text =
            "1. No File selected\n" +
            "2. Waiting for git username\n" +
            "____________________________";

        LoadGitUser();
    }

    private async void OnSelectWorkspaceClick(object? sender, RoutedEventArgs e)
    {
        var startLocation = await StorageProvider.TryGetWellKnownFolderAsync(
            WellKnownFolder.Documents
            );
            
        var folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Select current workspace folder",
                AllowMultiple = false,
                SuggestedStartLocation = startLocation
            });
        if (folders.Count > 0)
        {
            workspacePath = ResolveWorkspacePath(folders[0].Path.LocalPath);

            AddLog($"Workspace selected: {workspacePath}");

            TryEnterIntakeMode();
        }
    }

    private void TryEnterIntakeMode()
    {
        if (intakeModeEnabled)
        {
            return;
        }
        
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            AddLog("Workspace path is required before entering intake mode.");
            return;
        }

        if (!ValidateWorkspaceStructure())
        {
            AddLog("Invalid workspace folder. Select osu-mascot-workspace or the repo folder that contains it.");
            return;
        }
        
        AddLog("Valid workspace structure detected.");
        
        if (gitUser == "Unknown")
        {
            AddLog("Git username is required before entering intake mode.");
            return;
        }
        
        EnterIntakeMode();
    }
    private void AddLog(string message)
    {
        StatusLog.Text += $"\n> {message}";
        StatusLog.CaretIndex = StatusLog.Text.Length;
    }
    
    //IMAGE SELECTOR LOGIC
    
    private async void OnSelectImageClick(object? sender, RoutedEventArgs e)
    {
        if (!intakeModeEnabled)
        {   
            AddLog("Intake mode is required before importing images.");
            return;
        }
     
        SelectImageButton.Content = "Loading...";
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select an image",
            AllowMultiple = false
        });
        
        SelectImageButton.Content = "Select Image";
        
        if (files.Count == 0)
        {
            AddLog("No image selected.");
            return;
        }
        var selectedFile = files[0].Path.LocalPath;

        try
        {
            ImportImage(selectedFile);
        }
        catch (Exception ex)
        {
            AddLog($"Import failed: {ex.Message}");
        }
    }

    private void LoadGitUser()
    {
        try
        {
            var process = new Process();

            process.StartInfo.FileName = "git";
            process.StartInfo.Arguments = "config user.name";

            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.UseShellExecute = false;

            process.Start();

            string output = process.StandardOutput.ReadToEnd().Trim();

            process.WaitForExit();

            if (!string.IsNullOrEmpty(output))
            {
                gitUser = output;

                AddLog($"Git User: {gitUser}");

                GitUserButton.Content = "Reload Git User";
                
                TryEnterIntakeMode();
            }
            else
            {
                AddLog("Git username not found.");

                GitUserButton.Content = "Retry Git User load";
            }
        }
        catch
        {
            AddLog("Failed to load git user.");

            GitUserButton.Content = "Retry Git User load";
        }
      
    }

    private string ResolveWorkspacePath(string selectedPath)
    {
        var nestedWorkspacePath = System.IO.Path.Combine(
            selectedPath,
            "osu-mascot-workspace"
            );
        if (System.IO.Directory.Exists(nestedWorkspacePath))
        {
            return nestedWorkspacePath;
        }

        return selectedPath;
    }

//METHODS

    private void ImportImage(string selectedFile)
    {
        var fileName = System.IO.Path.GetFileName(selectedFile);

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

        AddLog($"Imported: {fileName}");
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
official_ID:
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
    private bool ValidateWorkspaceStructure()
    {
        var incomingPath = System.IO.Path.Combine(workspacePath, "98_Incoming");
        var stagingPath = System.IO.Path.Combine(workspacePath, "99_Staging");

        bool incomingExists = System.IO.Directory.Exists(incomingPath);
        bool stagingExists = System.IO.Directory.Exists(stagingPath);

        return incomingExists && stagingExists;
    }

    private void EnterIntakeMode()
    {
        intakeModeEnabled = true;
        SetupPanel.IsVisible = false;
        IntakePanel.IsVisible = true;

        StatusLog.Text = "";
        AddLog("Intake mode enabled.");
    }
    private void OnLoadGitUserClick(object? sender, RoutedEventArgs e)
    {
       LoadGitUser();
    }
}