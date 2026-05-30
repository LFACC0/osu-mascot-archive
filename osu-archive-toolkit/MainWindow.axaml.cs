using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls.Shapes;
using osuArchiveToolkit.Services;
namespace osuArchiveToolkit;
public partial class MainWindow : Window
{
    private string _gitUser = "Unknown";
    private string _workspacePath = "";
    private bool _intakeModeEnabled = false;
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
            _workspacePath = ResolveWorkspacePath(folders[0].Path.LocalPath);

            AddLog($"Workspace selected: {_workspacePath}");

            TryEnterIntakeMode();
        }
    }

    private bool IsSetupComplete()
    {
        if (string.IsNullOrWhiteSpace(_workspacePath))
        {
            return false;
        }

        if (!ValidateWorkspaceStructure())
        {
            return false;
        }

        if (_gitUser == "Unknown")
        {
            return false;
        }

        return true;
    }

    private bool ValidateSetupWithLog()
    {
        if (string.IsNullOrWhiteSpace(_workspacePath))
        {
            AddLog("Workspace path is required before continuing");
            return false;
        }

        if (!ValidateWorkspaceStructure())
        {
            AddLog("Invalid workspace folder, Select osu-mascot-workspace or the repo folder that contains it");
            return false;
        }

        if (_gitUser == "Unknown")
        {
            AddLog("Git username is required before continuing.");
            return false;
        }

        return true;
    }
        
    private void UpdateSetupState()
    {
        WorkspaceStatusText.Text = string.IsNullOrWhiteSpace(_workspacePath)
            ? "Workspace: not selected"
            : $"Workspace: {_workspacePath}";

        GitUserStatusText.Text = _gitUser == "Unknown"
            ? "Git user: not loaded"
            : $"Git user: {_gitUser}";

        NextButton.IsEnabled = IsSetupComplete();
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
    private void OnLoadGitUserClick(object? sender, RoutedEventArgs e)
    {
        LoadGitUser();
    }
    
    private void OnAddOtherEntryClick(object? sender, RoutedEventArgs e)
    {
        SuccessPanel.IsVisible = false;
        IntakePanel.IsVisible = true;

        StatusLog.Text = "";
        AddLog("Ready to add another entry.");
    }

    private async void OnOpenEntryInObsidian(object? sender, RoutedEventArgs e)
    {
        var lastEntryPath = _importService.LastEntryTempName;

        try
        {
            if (OperatingSystem.IsLinux())
            {
                OpenInObsidianButton.IsVisible = false;
                AddLog("Ready for editing in obsidian");
            }
            else
            {
                Process.Start(new ProcessStartInfo()
                {
                    FileName = lastEntryPath,
                    UseShellExecute = true
                });
                AddLog($"Opening: {lastEntryPath}"); 
            }
            
        }
        catch (Exception ex)
        {
            AddLog($"Open action failed: {ex.Message}" +
                   "try open the file manually (?");
        }

    }

    //IMAGE SELECTOR LOGIC
    private async void OnSelectImageClick(object? sender, RoutedEventArgs e)
    {
        if (!_intakeModeEnabled)
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
            _importService = new LocalImportService(_workspacePath, _gitUser);
            var fileName = System.IO.Path.GetFileName(selectedFile);
            _importService.ValidateFiles(fileName);
            
            var renamedFileName = _importService.ImportImage(selectedFile);
            
            AddLog($"Imported: {selectedFile}\n" +
                   $"Renamed to: {renamedFileName}"
                );
            
            await ShowSuccessPanel();
            
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
                _gitUser = output;

                AddLog($"Git User: {_gitUser}");

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

    private async Task ShowSuccessPanel()
    {
        IntakePanel.IsVisible = false;
        SuccessPanel.IsVisible = true;
        
        AddLog("Entry added successfully.");
        await Task.Delay(1000);
        AddLog("Check /osu-mascot-workspace/local_entries for any uncommited entries and assets");
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
        var incomingPath = System.IO.Path.Combine(_workspacePath, "98_Incoming");
        var stagingPath = System.IO.Path.Combine(_workspacePath, "99_Staging");

        bool incomingExists = System.IO.Directory.Exists(incomingPath);
        bool stagingExists = System.IO.Directory.Exists(stagingPath);

        return incomingExists && stagingExists;
    }

    private void ExitIntakeMode()
    {
        _intakeModeEnabled = false;

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
        _intakeModeEnabled = true;
        SetupPanel.IsVisible = false;
        IntakePanel.IsVisible = true;
        NextButton.IsVisible = false;
        
        StatusLog.Text = "";
        _importService = new LocalImportService(_workspacePath, _gitUser);
        AddLog("Intake mode enabled.");
    }
}