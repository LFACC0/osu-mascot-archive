using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System.Diagnostics;
using osuArchiveToolkit.Services;

namespace osuArchiveToolkit;

public partial class MainWindow : Window
{
    private string gitUser = "Unknown";
    private string workspacePath = "";
    private bool intakeModeEnabled = false;
    public MainWindow()
    {
        InitializeComponent();
        StatusLog.Text = "Welcome to the toolkit!";
        LoadGitUser();
        NextButton.IsVisible = false;
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

    private bool IsSetupComplete()
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return false;
        }

        if (!ValidateWorkspaceStructure())
        {
            return false;
        }

        if (gitUser == "Unknown")
        {
            return false;
        }

        return true;
    }

    private bool ValidateSetupWithLog()
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            AddLog("Workspace path is required before continuing");
            return false;
        }

        if (!ValidateWorkspaceStructure())
        {
            AddLog("Invalid workspace folder, Select osu-mascot-workspace or the repo folder that contains it");
            return false;
        }

        if (gitUser == "Unknown")
        {
            AddLog("Git username is required before continuing.");
            return false;
        }

        return true;
    }
        
    private void UpdateSetupState()
    {
        WorkspaceStatusText.Text = string.IsNullOrWhiteSpace(workspacePath)
            ? "Workspace: not selected"
            : $"Workspace: {workspacePath}";

        GitUserStatusText.Text = gitUser == "Unknown"
            ? "Git user: not loaded"
            : $"Git user: {gitUser}";

        NextButton.IsEnabled = IsSetupComplete();
    }

    public string GitUserAbbreviation()
    {
        string firstThreeChar = gitUser.Substring(0, 3);
        return firstThreeChar;
    }
    private void TryEnterIntakeMode()
    {
        UpdateSetupState();

        if (!ValidateSetupWithLog())
        {
            return;
        }
        
        AddLog("Setup complete. Ready to continue.");
    }

    private void OnCollaboratorModeClick(object? sender, RoutedEventArgs e)
    {
        WelcomePanel.IsVisible = false;
        SetupPanel.IsVisible = true;
        NextButton.IsVisible = true;

        StatusLog.Text = "";
        AddLog("Collaborator mode selected");
        
        UpdateSetupState();
    }
    private void OnNextClick(object? sender, RoutedEventArgs e)
    {
        if (!IsSetupComplete())
        {
            AddLog("Setup is not complete yet.");
            return;
        }
        
        EnterIntakeMode();
    }

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        ExitIntakeMode();
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
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                FilePickerFileTypes.ImageAll 
            }
        });
        
        SelectImageButton.Content = "Import Image";
        
        if (files.Count == 0)
        {
            AddLog("No image selected.");
            return;
        }
        var selectedFile = files[0].Path.LocalPath;

        try
        {
            var importService = new LocalImportService(workspacePath, gitUser);
            var importedFileName = importService.ImportImage(selectedFile);
            AddLog($"Imported: {importedFileName}");

            //var temporaryLocalAssetName = importService.TempImageName(importedFileName);
            //AddLog($"Renamed file to: {temporaryLocalAssetName}");
            ShowSuccessPanel();
            
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

    private void ShowSuccessPanel()
    {
        IntakePanel.IsVisible = false;
        SuccessPanel.IsVisible = true;

        StatusLog.Text = "";
        AddLog("Entry added successfully.");
    }

    private void OnAddOtherEntryClick(object? sender, RoutedEventArgs e)
    {
        SuccessPanel.IsVisible = false;
        IntakePanel.IsVisible = true;

        StatusLog.Text = "";
        AddLog("Ready to add another entry.");
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
    private bool ValidateWorkspaceStructure()
    {
        var incomingPath = System.IO.Path.Combine(workspacePath, "98_Incoming");
        var stagingPath = System.IO.Path.Combine(workspacePath, "99_Staging");

        bool incomingExists = System.IO.Directory.Exists(incomingPath);
        bool stagingExists = System.IO.Directory.Exists(stagingPath);

        return incomingExists && stagingExists;
    }

    private void ExitIntakeMode()
    {
        intakeModeEnabled = false;

        SetupPanel.IsVisible = true;
        IntakePanel.IsVisible = false;
        NextButton.IsVisible = true;

        StatusLog.Text = "";
        AddLog("Returned to setup mode.");
        
        UpdateSetupState();
    }
    private LocalImportService? _importService;
    private void EnterIntakeMode()
    {
        intakeModeEnabled = true;
        SetupPanel.IsVisible = false;
        IntakePanel.IsVisible = true;
        NextButton.IsVisible = false;
        
        StatusLog.Text = "";
        _importService = new LocalImportService(workspacePath, gitUser);
        AddLog("Intake mode enabled.");
    }
    private void OnLoadGitUserClick(object? sender, RoutedEventArgs e)
    {
        LoadGitUser();
    }

}