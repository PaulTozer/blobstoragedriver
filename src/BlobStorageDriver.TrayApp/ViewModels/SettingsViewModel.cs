using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BlobStorageDriver.Common.Configuration;
using BlobStorageDriver.SyncEngine.VirtualDrive;
using BlobStorageDriver.CloudProvider;
using Microsoft.Win32;

namespace BlobStorageDriver.TrayApp.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppConfiguration _config;

    // Azure Blob Settings
    [ObservableProperty]
    private AuthenticationType _selectedAuthType;

    [ObservableProperty]
    private string _connectionString = string.Empty;

    [ObservableProperty]
    private string _accountName = string.Empty;

    [ObservableProperty]
    private string _accountKey = string.Empty;

    [ObservableProperty]
    private string _containerName = string.Empty;

    [ObservableProperty]
    private string _sasToken = string.Empty;

    [ObservableProperty]
    private string _tenantId = string.Empty;

    [ObservableProperty]
    private string _clientId = string.Empty;

    [ObservableProperty]
    private bool _useManagedIdentity;

    // Computed visibility properties for auth fields
    public bool ShowConnectionStringField => SelectedAuthType == AuthenticationType.ConnectionString;
    public bool ShowAccountNameField => SelectedAuthType != AuthenticationType.ConnectionString;
    public bool ShowAccountKeyField => SelectedAuthType == AuthenticationType.AccountKey;
    public bool ShowSasTokenField => SelectedAuthType == AuthenticationType.SasToken;
    public bool ShowEntraIdFields => SelectedAuthType == AuthenticationType.EntraIdInteractive || 
                                      SelectedAuthType == AuthenticationType.EntraIdDefault;

    // Cache Settings
    [ObservableProperty]
    private string _localSyncFolder = string.Empty;

    [ObservableProperty]
    private long _maxCacheSizeMB;

    [ObservableProperty]
    private int _keepAccessedWithinDays;

    // Sync Settings
    [ObservableProperty]
    private int _syncIntervalMinutes;

    [ObservableProperty]
    private bool _enableRealTimeSync;

    [ObservableProperty]
    private int _maxConcurrentUploads;

    [ObservableProperty]
    private int _maxConcurrentDownloads;

    [ObservableProperty]
    private ConflictStrategy _defaultConflictStrategy;

    [ObservableProperty]
    private bool _pauseOnMeteredConnection;

    // UI Settings
    [ObservableProperty]
    private bool _showNotifications;

    [ObservableProperty]
    private bool _startMinimized;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _showSyncProgressInTray;

    [ObservableProperty]
    private bool _showConflictNotifications;

    // Integration Settings
    [ObservableProperty]
    private IntegrationMode _selectedIntegrationMode;

    [ObservableProperty]
    private string _selectedDriveLetter = "Z";

    [ObservableProperty]
    private string _volumeLabel = "Azure Blob Storage";

    [ObservableProperty]
    private bool _showInNavigationPane;

    // Computed visibility properties for integration fields
    public bool ShowDriveLetterField => SelectedIntegrationMode == IntegrationMode.VirtualDrive;
    public bool ShowNavigationPaneOption => SelectedIntegrationMode == IntegrationMode.ShellNamespace;
    public bool IsDokanInstalled => SyncEngine.VirtualDrive.VirtualDriveManager.IsDokanInstalled();

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    public SettingsViewModel(AppConfiguration config)
    {
        _config = config;
        LoadSettings();
        
        // Debug: Log available auth types
        System.Diagnostics.Debug.WriteLine($"AuthenticationTypeOptions count: {AuthenticationTypeOptions.Count()}");
        foreach (var opt in AuthenticationTypeOptions)
        {
            System.Diagnostics.Debug.WriteLine($"  - {opt}");
        }
    }

    private void LoadSettings()
    {
        // Azure Blob
        SelectedAuthType = _config.AzureBlob.AuthType;
        ConnectionString = _config.AzureBlob.ConnectionString ?? string.Empty;
        AccountName = _config.AzureBlob.AccountName ?? string.Empty;
        AccountKey = _config.AzureBlob.AccountKey ?? string.Empty;
        ContainerName = _config.AzureBlob.ContainerName ?? string.Empty;
        SasToken = _config.AzureBlob.SasToken ?? string.Empty;
        TenantId = _config.AzureBlob.TenantId ?? string.Empty;
        ClientId = _config.AzureBlob.ClientId ?? string.Empty;

        // Cache
        LocalSyncFolder = _config.Cache.LocalSyncFolder;
        MaxCacheSizeMB = _config.Cache.MaxCacheSizeBytes / (1024 * 1024);
        KeepAccessedWithinDays = _config.Cache.KeepAccessedWithinDays;

        // Sync
        SyncIntervalMinutes = _config.Sync.SyncIntervalSeconds / 60;
        EnableRealTimeSync = _config.Sync.EnableRealTimeSync;
        MaxConcurrentUploads = _config.Sync.MaxConcurrentUploads;
        MaxConcurrentDownloads = _config.Sync.MaxConcurrentDownloads;
        DefaultConflictStrategy = _config.Sync.DefaultConflictStrategy;
        PauseOnMeteredConnection = _config.Sync.PauseOnMeteredConnection;

        // UI
        ShowNotifications = _config.Ui.ShowNotifications;
        StartMinimized = _config.Ui.StartMinimized;
        StartWithWindows = _config.Ui.StartWithWindows;
        ShowSyncProgressInTray = _config.Ui.ShowSyncProgressInTray;
        ShowConflictNotifications = _config.Ui.ShowConflictNotifications;

        // Integration
        SelectedIntegrationMode = _config.Integration.Mode;
        SelectedDriveLetter = _config.Integration.DriveLetter ?? "Z";
        VolumeLabel = _config.Integration.VolumeLabel ?? "Azure Blob Storage";
        ShowInNavigationPane = _config.Integration.ShowInNavigationPane;

        HasUnsavedChanges = false;
    }

    partial void OnSelectedAuthTypeChanged(AuthenticationType value)
    {
        HasUnsavedChanges = true;
        OnPropertyChanged(nameof(ShowConnectionStringField));
        OnPropertyChanged(nameof(ShowAccountNameField));
        OnPropertyChanged(nameof(ShowAccountKeyField));
        OnPropertyChanged(nameof(ShowSasTokenField));
        OnPropertyChanged(nameof(ShowEntraIdFields));
    }
    partial void OnConnectionStringChanged(string value) => HasUnsavedChanges = true;
    partial void OnAccountNameChanged(string value) => HasUnsavedChanges = true;
    partial void OnAccountKeyChanged(string value) => HasUnsavedChanges = true;
    partial void OnContainerNameChanged(string value) => HasUnsavedChanges = true;
    partial void OnSasTokenChanged(string value) => HasUnsavedChanges = true;
    partial void OnTenantIdChanged(string value) => HasUnsavedChanges = true;
    partial void OnClientIdChanged(string value) => HasUnsavedChanges = true;
    partial void OnLocalSyncFolderChanged(string value) => HasUnsavedChanges = true;
    partial void OnMaxCacheSizeMBChanged(long value) => HasUnsavedChanges = true;
    partial void OnSyncIntervalMinutesChanged(int value) => HasUnsavedChanges = true;
    partial void OnEnableRealTimeSyncChanged(bool value) => HasUnsavedChanges = true;
    partial void OnSelectedIntegrationModeChanged(IntegrationMode value)
    {
        HasUnsavedChanges = true;
        OnPropertyChanged(nameof(ShowDriveLetterField));
        OnPropertyChanged(nameof(ShowNavigationPaneOption));
    }
    partial void OnSelectedDriveLetterChanged(string value) => HasUnsavedChanges = true;
    partial void OnVolumeLabelChanged(string value) => HasUnsavedChanges = true;
    partial void OnShowInNavigationPaneChanged(bool value) => HasUnsavedChanges = true;

    [RelayCommand]
    private void BrowseSyncFolder()
    {
        // Use Microsoft.Win32 FolderBrowserDialog via OpenFolderDialog (WPF)
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select local sync folder",
            InitialDirectory = LocalSyncFolder
        };

        if (dialog.ShowDialog() == true)
        {
            LocalSyncFolder = dialog.FolderName;
        }
    }

    [RelayCommand]
    private void SaveSettings()
    {
        SaveSettingsInternal();
        
        System.Windows.MessageBox.Show(
            "Settings saved successfully!\n\nClick 'Save & Start' to apply changes and start syncing.",
            "Settings Saved",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }
    
    [RelayCommand]
    private async Task SaveAndStartAsync()
    {
        SaveSettingsInternal();
        
        // Start the integration
        var app = (App)System.Windows.Application.Current;
        var success = await app.StartIntegrationAsync();
        
        if (success)
        {
            System.Windows.MessageBox.Show(
                "✓ Settings saved and sync started!\n\n" +
                "Your Azure Blob Storage container should now appear in File Explorer.",
                "Sync Started",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }
        else
        {
            System.Windows.MessageBox.Show(
                "Settings saved but failed to start sync.\n\n" +
                "Please check:\n" +
                "• Your Azure credentials are correct\n" +
                "• You've signed in (for Entra ID)\n" +
                "• The container exists and is accessible",
                "Start Failed",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }
    
    private void SaveSettingsInternal()
    {
        // Azure Blob
        _config.AzureBlob.AuthType = SelectedAuthType;
        _config.AzureBlob.ConnectionString = string.IsNullOrWhiteSpace(ConnectionString) ? null : ConnectionString;
        _config.AzureBlob.AccountName = string.IsNullOrWhiteSpace(AccountName) ? null : AccountName;
        _config.AzureBlob.AccountKey = string.IsNullOrWhiteSpace(AccountKey) ? null : AccountKey;
        _config.AzureBlob.ContainerName = string.IsNullOrWhiteSpace(ContainerName) ? null : ContainerName;
        _config.AzureBlob.SasToken = string.IsNullOrWhiteSpace(SasToken) ? null : SasToken;
        _config.AzureBlob.TenantId = string.IsNullOrWhiteSpace(TenantId) ? null : TenantId;
        _config.AzureBlob.ClientId = string.IsNullOrWhiteSpace(ClientId) ? null : ClientId;

        // Cache
        _config.Cache.LocalSyncFolder = LocalSyncFolder;
        _config.Cache.MaxCacheSizeBytes = MaxCacheSizeMB * 1024 * 1024;
        _config.Cache.KeepAccessedWithinDays = KeepAccessedWithinDays;

        // Sync
        _config.Sync.SyncIntervalSeconds = SyncIntervalMinutes * 60;
        _config.Sync.EnableRealTimeSync = EnableRealTimeSync;
        _config.Sync.MaxConcurrentUploads = MaxConcurrentUploads;
        _config.Sync.MaxConcurrentDownloads = MaxConcurrentDownloads;
        _config.Sync.DefaultConflictStrategy = DefaultConflictStrategy;
        _config.Sync.PauseOnMeteredConnection = PauseOnMeteredConnection;

        // UI
        _config.Ui.ShowNotifications = ShowNotifications;
        _config.Ui.StartMinimized = StartMinimized;
        _config.Ui.StartWithWindows = StartWithWindows;
        _config.Ui.ShowSyncProgressInTray = ShowSyncProgressInTray;
        _config.Ui.ShowConflictNotifications = ShowConflictNotifications;

        // Integration
        _config.Integration.Mode = SelectedIntegrationMode;
        _config.Integration.DriveLetter = SelectedDriveLetter;
        _config.Integration.VolumeLabel = VolumeLabel;
        _config.Integration.ShowInNavigationPane = ShowInNavigationPane;

        _config.Save();
        
        // Update startup registry
        UpdateStartupRegistry();

        HasUnsavedChanges = false;
    }

    [RelayCommand]
    private void CancelChanges()
    {
        LoadSettings();
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        var defaults = new AppConfiguration();
        
        LocalSyncFolder = defaults.Cache.LocalSyncFolder;
        MaxCacheSizeMB = defaults.Cache.MaxCacheSizeBytes / (1024 * 1024);
        KeepAccessedWithinDays = defaults.Cache.KeepAccessedWithinDays;
        SyncIntervalMinutes = defaults.Sync.SyncIntervalSeconds / 60;
        EnableRealTimeSync = defaults.Sync.EnableRealTimeSync;
        MaxConcurrentUploads = defaults.Sync.MaxConcurrentUploads;
        MaxConcurrentDownloads = defaults.Sync.MaxConcurrentDownloads;
        DefaultConflictStrategy = defaults.Sync.DefaultConflictStrategy;
        PauseOnMeteredConnection = defaults.Sync.PauseOnMeteredConnection;
        ShowNotifications = defaults.Ui.ShowNotifications;
        StartMinimized = defaults.Ui.StartMinimized;
        StartWithWindows = defaults.Ui.StartWithWindows;
        ShowSyncProgressInTray = defaults.Ui.ShowSyncProgressInTray;
        ShowConflictNotifications = defaults.Ui.ShowConflictNotifications;

        HasUnsavedChanges = true;
    }

    private void UpdateStartupRegistry()
    {
        const string appName = "BlobStorageDriver";
        var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        
        using var key = Registry.CurrentUser.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
        
        if (key != null)
        {
            if (StartWithWindows && exePath != null)
            {
                key.SetValue(appName, $"\"{exePath}\" --minimized");
            }
            else
            {
                key.DeleteValue(appName, false);
            }
        }
    }

    public IEnumerable<ConflictStrategy> ConflictStrategyOptions => 
        Enum.GetValues<ConflictStrategy>();

    public IEnumerable<AuthenticationType> AuthenticationTypeOptions => 
        Enum.GetValues<AuthenticationType>();

    public IEnumerable<IntegrationMode> IntegrationModeOptions => 
        Enum.GetValues<IntegrationMode>();

    public IEnumerable<string> AvailableDriveLetters => 
        SyncEngine.VirtualDrive.VirtualDriveManager.GetAvailableDriveLetters();

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        try
        {
            // Build configuration from current settings
            var blobSettings = new AzureBlobSettings
            {
                AuthType = SelectedAuthType,
                ConnectionString = ConnectionString,
                AccountName = AccountName,
                AccountKey = AccountKey,
                ContainerName = ContainerName,
                SasToken = SasToken,
                TenantId = TenantId,
                ClientId = ClientId
            };

            // Validate required fields based on auth type
            var validationError = ValidateConfiguration(blobSettings);
            if (validationError != null)
            {
                System.Windows.MessageBox.Show(
                    validationError,
                    "Validation Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            // Create provider and test connection (use null logger for testing)
            var provider = new AzureBlobStorageProvider(blobSettings, 
                Microsoft.Extensions.Logging.Abstractions.NullLogger<AzureBlobStorageProvider>.Instance);
            var success = await provider.TestConnectionAsync();

            if (success)
            {
                System.Windows.MessageBox.Show(
                    "✓ Successfully connected to Azure Blob Storage!\n\n" +
                    $"Container: {ContainerName}",
                    "Connection Successful",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            else
            {
                System.Windows.MessageBox.Show(
                    "✗ Could not connect to Azure Blob Storage.\n\n" +
                    "Please check your settings and try again.",
                    "Connection Failed",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"✗ Connection failed: {ex.Message}",
                "Connection Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private string? ValidateConfiguration(AzureBlobSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ContainerName))
            return "Container name is required.";

        switch (settings.AuthType)
        {
            case AuthenticationType.ConnectionString:
                if (string.IsNullOrWhiteSpace(settings.ConnectionString))
                    return "Connection string is required.";
                break;
            case AuthenticationType.AccountKey:
                if (string.IsNullOrWhiteSpace(settings.AccountName))
                    return "Account name is required.";
                if (string.IsNullOrWhiteSpace(settings.AccountKey))
                    return "Account key is required.";
                break;
            case AuthenticationType.SasToken:
                if (string.IsNullOrWhiteSpace(settings.AccountName))
                    return "Account name is required.";
                if (string.IsNullOrWhiteSpace(settings.SasToken))
                    return "SAS token is required.";
                break;
            case AuthenticationType.EntraIdInteractive:
            case AuthenticationType.EntraIdDefault:
                if (string.IsNullOrWhiteSpace(settings.AccountName))
                    return "Account name is required for Entra ID authentication.";
                break;
        }

        return null;
    }
}
