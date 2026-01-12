using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.DependencyInjection;
using BlobStorageDriver.WinUI.ViewModels;
using BlobStorageDriver.Common.Configuration;
using BlobStorageDriver.SyncEngine.Integration;
using Windows.Storage.Pickers;
using System;
using WinRT.Interop;

namespace BlobStorageDriver.WinUI.Pages;

public sealed partial class SettingsPage : Page
{
    private SettingsViewModel? _viewModel;
    private AppConfiguration? _config;
    
    public SettingsPage()
    {
        this.InitializeComponent();
        
        _viewModel = App.Services.GetService<SettingsViewModel>();
        _config = App.Services.GetService<AppConfiguration>();
        
        LoadSettings();
    }
    
    private void LoadSettings()
    {
        if (_config == null) return;
        
        // Azure settings
        var authIndex = _config.AzureBlob.AuthType switch
        {
            AuthenticationType.ConnectionString => 0,
            AuthenticationType.EntraIdInteractive => 1,
            AuthenticationType.EntraIdDefault => 2,
            _ => 0
        };
        AuthTypeCombo.SelectedIndex = authIndex;
        
        AccountNameBox.Text = _config.AzureBlob.AccountName ?? "";
        ContainerNameBox.Text = _config.AzureBlob.ContainerName ?? "";
        ConnectionStringBox.Password = _config.AzureBlob.ConnectionString ?? "";
        
        // Sync settings
        LocalPathBox.Text = _config.Cache.LocalSyncFolder ?? "";
        SyncIntervalBox.Value = _config.Sync.SyncIntervalSeconds;
        SyncOnStartupToggle.IsOn = _config.Sync.EnableRealTimeSync;
        AutoResolveToggle.IsOn = _config.Sync.DefaultConflictStrategy != ConflictStrategy.AskUser;
        
        // Integration settings
        var modeIndex = _config.Integration.Mode switch
        {
            IntegrationMode.LocalFolder => 0,
            IntegrationMode.ShellNamespace => 1,
            IntegrationMode.VirtualDrive => 2,
            _ => 1
        };
        IntegrationModeCombo.SelectedIndex = modeIndex;
        
        // Set drive letter
        var driveLetter = _config.Integration.DriveLetter ?? "Z";
        for (int i = 0; i < DriveLetterCombo.Items.Count; i++)
        {
            if (DriveLetterCombo.Items[i] is ComboBoxItem item && item.Content?.ToString()?.StartsWith(driveLetter) == true)
            {
                DriveLetterCombo.SelectedIndex = i;
                break;
            }
        }
        
        VolumeLabelBox.Text = _config.Integration.VolumeLabel ?? "Azure Blob Storage";
        ShowInNavPaneToggle.IsOn = _config.Integration.ShowInNavigationPane;
        ShowStatusOverlaysToggle.IsOn = _config.Integration.ShowStatusOverlays;
        
        StartWithWindowsToggle.IsOn = _config.Ui.StartWithWindows;
        MinimizeToTrayToggle.IsOn = _config.Ui.StartMinimized;
        
        UpdateAuthFieldVisibility();
        UpdateIntegrationFieldVisibility();
    }
    
    private void AuthType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateAuthFieldVisibility();
    }
    
    private void UpdateAuthFieldVisibility()
    {
        if (AuthTypeCombo.SelectedItem is ComboBoxItem item)
        {
            var tag = item.Tag?.ToString();
            ConnectionStringBox.Visibility = tag == "ConnectionString" ? Visibility.Visible : Visibility.Collapsed;
        }
    }
    
    private void IntegrationMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateIntegrationFieldVisibility();
    }
    
    private void UpdateIntegrationFieldVisibility()
    {
        if (IntegrationModeCombo?.SelectedItem is ComboBoxItem item)
        {
            var tag = item.Tag?.ToString();
            // Show drive letter options only for VirtualDrive mode
            if (DriveLetterPanel != null)
            {
                DriveLetterPanel.Visibility = tag == "VirtualDrive" ? Visibility.Visible : Visibility.Collapsed;
            }
            // Show nav pane option for ShellNamespace mode
            if (ShowInNavPaneToggle != null)
            {
                ShowInNavPaneToggle.Visibility = tag == "ShellNamespace" ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }
    
    private async void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add("*");
        
        // Initialize with window handle
        var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);
        
        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            LocalPathBox.Text = folder.Path;
        }
    }
    
    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        TestConnectionBtn.IsEnabled = false;
        try
        {
            // TODO: Implement actual connection test
            await System.Threading.Tasks.Task.Delay(1000);
            
            var dialog = new ContentDialog
            {
                Title = "Connection Test",
                Content = "Connection test not implemented yet.",
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
        }
        finally
        {
            TestConnectionBtn.IsEnabled = true;
        }
    }
    
    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        LoadSettings();
    }
    
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_config == null) return;
        
        // Save Azure settings
        var authType = (AuthTypeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() switch
        {
            "ConnectionString" => AuthenticationType.ConnectionString,
            "AzureAD" => AuthenticationType.EntraIdInteractive,
            "ManagedIdentity" => AuthenticationType.EntraIdDefault,
            _ => AuthenticationType.ConnectionString
        };
        
        _config.AzureBlob.AuthType = authType;
        _config.AzureBlob.AccountName = AccountNameBox.Text;
        _config.AzureBlob.ContainerName = ContainerNameBox.Text;
        _config.AzureBlob.ConnectionString = ConnectionStringBox.Password;
        
        // Save sync settings
        _config.Cache.LocalSyncFolder = LocalPathBox.Text;
        _config.Sync.SyncIntervalSeconds = (int)SyncIntervalBox.Value;
        _config.Sync.EnableRealTimeSync = SyncOnStartupToggle.IsOn;
        _config.Sync.DefaultConflictStrategy = AutoResolveToggle.IsOn ? ConflictStrategy.KeepNewest : ConflictStrategy.AskUser;
        
        // Save integration settings
        var integrationMode = (IntegrationModeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() switch
        {
            "LocalFolder" => IntegrationMode.LocalFolder,
            "ShellNamespace" => IntegrationMode.ShellNamespace,
            "VirtualDrive" => IntegrationMode.VirtualDrive,
            _ => IntegrationMode.ShellNamespace
        };
        _config.Integration.Mode = integrationMode;
        
        // Save drive letter (extract just the letter)
        if (DriveLetterCombo.SelectedItem is ComboBoxItem driveItem)
        {
            var driveText = driveItem.Content?.ToString() ?? "Z:";
            _config.Integration.DriveLetter = driveText.TrimEnd(':');
        }
        
        _config.Integration.VolumeLabel = VolumeLabelBox.Text;
        _config.Integration.ShowInNavigationPane = ShowInNavPaneToggle.IsOn;
        _config.Integration.ShowStatusOverlays = ShowStatusOverlaysToggle.IsOn;
        
        _config.Ui.StartWithWindows = StartWithWindowsToggle.IsOn;
        _config.Ui.StartMinimized = MinimizeToTrayToggle.IsOn;
        
        // Save to file
        _config.Save();
        
        var dialog = new ContentDialog
        {
            Title = "Settings Saved",
            Content = "Your settings have been saved successfully.",
            CloseButtonText = "OK",
            XamlRoot = this.XamlRoot
        };
        await dialog.ShowAsync();
    }
}
