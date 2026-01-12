using CommunityToolkit.Mvvm.ComponentModel;
using BlobStorageDriver.Common.Configuration;

namespace BlobStorageDriver.WinUI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppConfiguration _config;

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
    private string _localSyncFolder = string.Empty;

    [ObservableProperty]
    private int _syncIntervalMinutes;

    [ObservableProperty]
    private bool _enableRealTimeSync;

    [ObservableProperty]
    private IntegrationMode _selectedIntegrationMode;

    [ObservableProperty]
    private string _selectedDriveLetter = "Z";

    [ObservableProperty]
    private string _volumeLabel = "Azure Blob Storage";

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _startMinimized;

    [ObservableProperty]
    private bool _showNotifications;

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    public SettingsViewModel(AppConfiguration config)
    {
        _config = config;
        LoadSettings();
    }

    private void LoadSettings()
    {
        SelectedAuthType = _config.AzureBlob.AuthType;
        ConnectionString = _config.AzureBlob.ConnectionString ?? string.Empty;
        AccountName = _config.AzureBlob.AccountName ?? string.Empty;
        AccountKey = _config.AzureBlob.AccountKey ?? string.Empty;
        ContainerName = _config.AzureBlob.ContainerName ?? string.Empty;
        SasToken = _config.AzureBlob.SasToken ?? string.Empty;
        TenantId = _config.AzureBlob.TenantId ?? string.Empty;
        ClientId = _config.AzureBlob.ClientId ?? string.Empty;

        LocalSyncFolder = _config.Cache.LocalSyncFolder;
        SyncIntervalMinutes = _config.Sync.SyncIntervalSeconds / 60;
        EnableRealTimeSync = _config.Sync.EnableRealTimeSync;

        SelectedIntegrationMode = _config.Integration.Mode;
        SelectedDriveLetter = _config.Integration.DriveLetter ?? "Z";
        VolumeLabel = _config.Integration.VolumeLabel ?? "Azure Blob Storage";

        StartWithWindows = _config.Ui.StartWithWindows;
        StartMinimized = _config.Ui.StartMinimized;
        ShowNotifications = _config.Ui.ShowNotifications;

        HasUnsavedChanges = false;
    }

    public void SaveSettings()
    {
        _config.AzureBlob.AuthType = SelectedAuthType;
        _config.AzureBlob.ConnectionString = ConnectionString;
        _config.AzureBlob.AccountName = AccountName;
        _config.AzureBlob.AccountKey = AccountKey;
        _config.AzureBlob.ContainerName = ContainerName;
        _config.AzureBlob.SasToken = SasToken;
        _config.AzureBlob.TenantId = TenantId;
        _config.AzureBlob.ClientId = ClientId;

        _config.Cache.LocalSyncFolder = LocalSyncFolder;
        _config.Sync.SyncIntervalSeconds = SyncIntervalMinutes * 60;
        _config.Sync.EnableRealTimeSync = EnableRealTimeSync;

        _config.Integration.Mode = SelectedIntegrationMode;
        _config.Integration.DriveLetter = SelectedDriveLetter;
        _config.Integration.VolumeLabel = VolumeLabel;

        _config.Ui.StartWithWindows = StartWithWindows;
        _config.Ui.StartMinimized = StartMinimized;
        _config.Ui.ShowNotifications = ShowNotifications;

        _config.Save();
        HasUnsavedChanges = false;
    }
}
