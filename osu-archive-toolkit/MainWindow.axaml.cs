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
    public MainWindow()
    {
        InitializeComponent();
        StatusLog.Text =
            "1. No File selected\n" +
            "2. Waiting for git username\n" +
            "____________________________";

        LoadGitUser();
    }

    private async void OnSelectVaultClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Select Vault Workspace Folder",
                AllowMultiple = false
            });
        if (folders.Count > 0)
        {
            workspacePath = folders[0].Path.LocalPath;

            AddLog($"Vault selected: {workspacePath}");

            if (ValidateVaultStructure())
            {
                AddLog("Valid osu-mascot-archive repository detected.");
                if (gitUser != "Unknown")
                {
                    EnterIntakeMode();
                }
            }
            else
            {
                AddLog("Invalid vault structure. make sure to select the main subfolder /osu-mascot-archive/osu-mascot-workspace");
            }
        }
    }
    private void AddLog(string message)
    {
        StatusLog.Text += $"\n> {message}";
        StatusLog.CaretIndex = StatusLog.Text.Length;
    }
    private async void OnSelectImageClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {   
            AddLog("No repository selected");
            return;
        }
     
        SelectImageButton.Content = "Loading...";
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select an image",
            AllowMultiple = false
        });
        
        SelectImageButton.Content = "Select Image";
        
        if (files.Count > 0)
        {
            var selectedFile = files[0].Path.LocalPath;
            var fileName = System.IO.Path.GetFileName(selectedFile);
            var assetsFolder = System.IO.Path.Combine(
                workspacePath,
                "98_Incoming",
                "Incoming-assets"
                );
            System.IO.Directory.CreateDirectory(assetsFolder);
            var destinationPath = System.IO.Path.Combine(
                assetsFolder,
                fileName
            );
            
            System.IO.File.Copy(selectedFile, destinationPath, true);
            var entryFolder = System.IO.Path.Combine(
                workspacePath,
                "98_Incoming",
                "Incoming-entries"
                );
            System.IO.Directory.CreateDirectory(entryFolder);

            var markdownPath = System.IO.Path.Combine(
                entryFolder,
                $"{System.IO.Path.GetFileNameWithoutExtension(fileName)}.md"
            );

            var markdownContent =
                $@"---
title: {System.IO.Path.GetFileNameWithoutExtension(fileName)}
asset: {fileName}
---

# Notes
";
            System.IO.File.WriteAllText(markdownPath, markdownContent
            );
            
            AddLog ($"Imported: {fileName}");
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

    private bool ValidateVaultStructure()
    {
        var incomingPath = System.IO.Path.Combine(workspacePath, "98_Incoming");
        var stagingPath = System.IO.Path.Combine(workspacePath, "99_Staging");

        bool incomingExists = System.IO.Directory.Exists(incomingPath);
        bool stagingExists = System.IO.Directory.Exists(stagingPath);

        return incomingExists && stagingExists;
    }

    private void EnterIntakeMode()
    {
        SetupPanel.IsVisible = false;
        IntakePanel.IsVisible = true;

        AddLog("Intake mode enabled.");
    }
    private void OnLoadGitUserClick(object? sender, RoutedEventArgs e)
    {
       LoadGitUser();
    }
}