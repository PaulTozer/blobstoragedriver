using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlobStorageDriver.Common.Configuration;

/// <summary>
/// Application configuration settings
/// </summary>
public class AppConfiguration
{
    public AzureBlobSettings AzureBlob { get; set; } = new();
    public CacheSettings Cache { get; set; } = new();
    public SyncSettings Sync { get; set; } = new();
    public UiSettings Ui { get; set; } = new();
    public IntegrationSettings Integration { get; set; } = new();
    
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BlobStorageDriver",
        "config.json");
        
    public static AppConfiguration Load()
    {
        if (File.Exists(ConfigPath))
        {
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<AppConfiguration>(json) ?? new AppConfiguration();
        }
        return new AppConfiguration();
    }
    
    public void Save()
    {
        var directory = Path.GetDirectoryName(ConfigPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        
        var options = new JsonSerializerOptions 
        { 
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        var json = JsonSerializer.Serialize(this, options);
        File.WriteAllText(ConfigPath, json);
    }
}

/// <summary>
/// Authentication type for Azure Blob Storage
/// </summary>
public enum AuthenticationType
{
    /// <summary>Full connection string</summary>
    ConnectionString,
    /// <summary>Account name and key</summary>
    AccountKey,
    /// <summary>Shared Access Signature token</summary>
    SasToken,
    /// <summary>Microsoft Entra ID (Azure AD) - Interactive browser login</summary>
    EntraIdInteractive,
    /// <summary>Microsoft Entra ID (Azure AD) - Default credential chain</summary>
    EntraIdDefault,
    /// <summary>Managed Identity (for Azure-hosted apps)</summary>
    ManagedIdentity
}

/// <summary>
/// Azure Blob Storage connection settings
/// </summary>
public class AzureBlobSettings
{
    /// <summary>The authentication method to use</summary>
    public AuthenticationType AuthType { get; set; } = AuthenticationType.EntraIdInteractive;
    
    /// <summary>Full connection string (for ConnectionString auth)</summary>
    public string? ConnectionString { get; set; }
    
    /// <summary>Storage account name</summary>
    public string? AccountName { get; set; }
    
    /// <summary>Storage account key (for AccountKey auth)</summary>
    public string? AccountKey { get; set; }
    
    /// <summary>Container name to sync</summary>
    public string? ContainerName { get; set; }
    
    /// <summary>SAS token (for SasToken auth)</summary>
    public string? SasToken { get; set; }
    
    /// <summary>Azure AD Tenant ID (optional, for Entra ID auth)</summary>
    public string? TenantId { get; set; }
    
    /// <summary>Azure AD Client/App ID (optional, for service principal auth)</summary>
    public string? ClientId { get; set; }
    
    // Legacy property for backward compatibility
    [Obsolete("Use AuthType instead")]
    public bool UseManagedIdentity 
    { 
        get => AuthType == AuthenticationType.ManagedIdentity;
        set { if (value) AuthType = AuthenticationType.ManagedIdentity; }
    }
    
    public int MaxRetries { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 2;
    public int TimeoutSeconds { get; set; } = 60;
}

/// <summary>
/// Local cache settings
/// </summary>
public class CacheSettings
{
    /// <summary>Local folder to sync files to</summary>
    public string LocalSyncFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "BlobStorage");
        
    /// <summary>Maximum cache size in bytes (default 10GB)</summary>
    public long MaxCacheSizeBytes { get; set; } = 10L * 1024 * 1024 * 1024;
    
    /// <summary>Threshold to start evicting cached files (default 80%)</summary>
    public double EvictionThresholdPercent { get; set; } = 80;
    
    /// <summary>Keep files accessed within this many days</summary>
    public int KeepAccessedWithinDays { get; set; } = 30;
    
    /// <summary>Always keep pinned files locally</summary>
    public bool RespectPinnedFiles { get; set; } = true;
    
    /// <summary>Database path for file metadata</summary>
    public string DatabasePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BlobStorageDriver",
        "filedb.db");
}

/// <summary>
/// Synchronization settings
/// </summary>
public class SyncSettings
{
    /// <summary>Automatic sync interval in seconds</summary>
    public int SyncIntervalSeconds { get; set; } = 300;
    
    /// <summary>Enable real-time sync using file watcher</summary>
    public bool EnableRealTimeSync { get; set; } = true;
    
    /// <summary>Delay before syncing after file change (debounce)</summary>
    public int FileChangeDelayMs { get; set; } = 1000;
    
    /// <summary>Maximum concurrent uploads</summary>
    public int MaxConcurrentUploads { get; set; } = 4;
    
    /// <summary>Maximum concurrent downloads</summary>
    public int MaxConcurrentDownloads { get; set; } = 4;
    
    /// <summary>Chunk size for large file transfers</summary>
    public int ChunkSizeBytes { get; set; } = 4 * 1024 * 1024;
    
    /// <summary>Files larger than this use chunked transfer</summary>
    public long ChunkThresholdBytes { get; set; } = 100 * 1024 * 1024;
    
    /// <summary>Conflict resolution strategy</summary>
    public ConflictStrategy DefaultConflictStrategy { get; set; } = ConflictStrategy.AskUser;
    
    /// <summary>Pause sync when on metered connection</summary>
    public bool PauseOnMeteredConnection { get; set; } = true;
    
    /// <summary>File patterns to exclude from sync</summary>
    public List<string> ExcludePatterns { get; set; } = new()
    {
        "*.tmp",
        "~$*",
        ".DS_Store",
        "Thumbs.db",
        "desktop.ini",
        "*.partial"
    };
}

/// <summary>
/// UI settings
/// </summary>
public class UiSettings
{
    public bool ShowNotifications { get; set; } = true;
    public bool StartMinimized { get; set; } = false;
    public bool StartWithWindows { get; set; } = true;
    public bool ShowSyncProgressInTray { get; set; } = true;
    public bool PlaySoundOnComplete { get; set; } = false;
    public bool ShowConflictNotifications { get; set; } = true;
}

/// <summary>
/// Conflict resolution strategy
/// </summary>
public enum ConflictStrategy
{
    AskUser,
    KeepLocal,
    KeepCloud,
    KeepBoth,
    KeepNewest
}

/// <summary>
/// How the storage appears in Windows
/// </summary>
public enum IntegrationMode
{
    /// <summary>Standard folder sync (like Google Drive)</summary>
    LocalFolder,
    /// <summary>Appears in File Explorer navigation pane</summary>
    ShellNamespace,
    /// <summary>Mounted as a drive letter (like mapped network drive)</summary>
    VirtualDrive
}

/// <summary>
/// Windows integration settings
/// </summary>
public class IntegrationSettings
{
    /// <summary>How the storage appears in Windows</summary>
    public IntegrationMode Mode { get; set; } = IntegrationMode.ShellNamespace;
    
    /// <summary>Drive letter for VirtualDrive mode (e.g., "Z")</summary>
    public string DriveLetter { get; set; } = "Z";
    
    /// <summary>Volume label shown in File Explorer</summary>
    public string VolumeLabel { get; set; } = "Azure Blob Storage";
    
    /// <summary>Show in navigation pane for ShellNamespace mode</summary>
    public bool ShowInNavigationPane { get; set; } = true;
    
    /// <summary>Show cloud status icons on files</summary>
    public bool ShowStatusOverlays { get; set; } = true;
}
