using BlobStorageDriver.CloudProvider;
using BlobStorageDriver.Common.Configuration;
using BlobStorageDriver.SyncEngine.Cache;
using BlobStorageDriver.SyncEngine.CloudFilter;
using BlobStorageDriver.SyncEngine.VirtualDrive;
using Microsoft.Extensions.Logging;

namespace BlobStorageDriver.SyncEngine.Integration;

/// <summary>
/// Manages the Windows integration mode (LocalFolder, ShellNamespace, or VirtualDrive)
/// </summary>
public class IntegrationManager : IDisposable
{
    private readonly ICloudStorageProvider _cloudProvider;
    private readonly LocalCacheManager _cacheManager;
    private readonly AppConfiguration _config;
    private readonly ILogger<IntegrationManager> _logger;
    private readonly ILoggerFactory _loggerFactory;
    
    private VirtualDriveManager? _virtualDriveManager;
    private CloudFilterProvider? _cloudFilterProvider;
    private FileSystemWatcher? _fileWatcher;
    
    private IntegrationMode _currentMode = IntegrationMode.LocalFolder;
    private bool _isActive;
    
    public event EventHandler<IntegrationModeChangedEventArgs>? ModeChanged;
    public event EventHandler<IntegrationStatusEventArgs>? StatusChanged;
    public event EventHandler<FileActivityEventArgs>? FileActivity;

    public IntegrationMode CurrentMode => _currentMode;
    public bool IsActive => _isActive;

    public IntegrationManager(
        ICloudStorageProvider cloudProvider,
        LocalCacheManager cacheManager,
        AppConfiguration config,
        ILogger<IntegrationManager> logger,
        ILoggerFactory loggerFactory)
    {
        _cloudProvider = cloudProvider;
        _cacheManager = cacheManager;
        _config = config;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Initialize and start the configured integration mode
    /// </summary>
    public async Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        var targetMode = _config.Integration.Mode;
        _logger.LogInformation("Starting integration in {Mode} mode", targetMode);

        try
        {
            switch (targetMode)
            {
                case IntegrationMode.LocalFolder:
                    return await StartLocalFolderModeAsync(cancellationToken);

                case IntegrationMode.ShellNamespace:
                    return await StartShellNamespaceModeAsync(cancellationToken);

                case IntegrationMode.VirtualDrive:
                    return await StartVirtualDriveModeAsync(cancellationToken);

                default:
                    _logger.LogWarning("Unknown integration mode: {Mode}", targetMode);
                    return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start integration in {Mode} mode", targetMode);
            return false;
        }
    }

    /// <summary>
    /// Stop the current integration
    /// </summary>
    public async Task StopAsync()
    {
        _logger.LogInformation("Stopping integration");

        try
        {
            // Stop file watcher
            if (_fileWatcher != null)
            {
                _fileWatcher.EnableRaisingEvents = false;
                _fileWatcher.Dispose();
                _fileWatcher = null;
            }
            
            if (_virtualDriveManager != null)
            {
                await _virtualDriveManager.UnmountAsync();
                _virtualDriveManager.Dispose();
                _virtualDriveManager = null;
            }

            if (_cloudFilterProvider != null)
            {
                _cloudFilterProvider.UnregisterSyncRoot();
                _cloudFilterProvider.Dispose();
                _cloudFilterProvider = null;
            }

            _isActive = false;
            StatusChanged?.Invoke(this, new IntegrationStatusEventArgs(false, "Integration stopped"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping integration");
        }
    }

    /// <summary>
    /// Switch to a different integration mode
    /// </summary>
    public async Task<bool> SwitchModeAsync(IntegrationMode newMode, CancellationToken cancellationToken = default)
    {
        if (newMode == _currentMode && _isActive)
        {
            _logger.LogInformation("Already running in {Mode} mode", newMode);
            return true;
        }

        var oldMode = _currentMode;
        
        await StopAsync();
        
        _config.Integration.Mode = newMode;
        
        var success = await StartAsync(cancellationToken);
        
        if (success)
        {
            _currentMode = newMode;
            ModeChanged?.Invoke(this, new IntegrationModeChangedEventArgs(oldMode, newMode));
        }

        return success;
    }

    /// <summary>
    /// Get information about available modes and their requirements
    /// </summary>
    public IntegrationModeInfo[] GetAvailableModes()
    {
        return new[]
        {
            new IntegrationModeInfo
            {
                Mode = IntegrationMode.LocalFolder,
                DisplayName = "Local Folder Sync",
                Description = "Syncs files to a local folder using Windows Cloud Files API. Files appear as placeholders and download on demand.",
                RequiresElevation = false,
                RequiresDriver = false,
                IsAvailable = true
            },
            new IntegrationModeInfo
            {
                Mode = IntegrationMode.ShellNamespace,
                DisplayName = "Navigation Pane",
                Description = "Appears in File Explorer's navigation pane. Uses Cloud Files API with shell namespace registration.",
                RequiresElevation = true,
                RequiresDriver = false,
                IsAvailable = true
            },
            new IntegrationModeInfo
            {
                Mode = IntegrationMode.VirtualDrive,
                DisplayName = "Drive Letter",
                Description = "Mounts blob storage as a drive letter (e.g., Z:). Best for legacy SMB compatibility.",
                RequiresElevation = true,
                RequiresDriver = true,
                IsAvailable = VirtualDriveManager.IsDokanInstalled(),
                UnavailableReason = VirtualDriveManager.IsDokanInstalled() ? null : "Dokan driver is not installed. Please install from https://github.com/dokan-dev/dokany/releases"
            }
        };
    }

    private async Task<bool> StartLocalFolderModeAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Local Folder mode at {Path}", _config.Cache.LocalSyncFolder);

        // Ensure sync folder exists
        if (!Directory.Exists(_config.Cache.LocalSyncFolder))
        {
            Directory.CreateDirectory(_config.Cache.LocalSyncFolder);
        }

        // Initialize Cloud Filter provider
        _cloudFilterProvider = new CloudFilterProvider(
            _config.Cache,
            _loggerFactory.CreateLogger<CloudFilterProvider>());

        try
        {
            await _cloudFilterProvider.RegisterSyncRootAsync();
            
            // Start file system watcher for activity tracking and sync
            StartFileWatcher();
            
            _currentMode = IntegrationMode.LocalFolder;
            _isActive = true;
            StatusChanged?.Invoke(this, new IntegrationStatusEventArgs(true, $"Syncing to {_config.Cache.LocalSyncFolder}"));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register Cloud Filter sync root");
            return false;
        }
    }

    private async Task<bool> StartShellNamespaceModeAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Shell Namespace mode");

        // Shell namespace uses the same Cloud Files API but with additional registry settings
        // to appear in the navigation pane
        
        if (!Directory.Exists(_config.Cache.LocalSyncFolder))
        {
            Directory.CreateDirectory(_config.Cache.LocalSyncFolder);
        }

        _cloudFilterProvider = new CloudFilterProvider(
            _config.Cache,
            _loggerFactory.CreateLogger<CloudFilterProvider>());

        try
        {
            await _cloudFilterProvider.RegisterSyncRootAsync();
            
            if (_config.Integration.ShowInNavigationPane)
            {
                // Register for navigation pane display
                RegisterNavigationPane();
            }
            
            // Start file system watcher for activity tracking and sync
            StartFileWatcher();

            _currentMode = IntegrationMode.ShellNamespace;
            _isActive = true;
            StatusChanged?.Invoke(this, new IntegrationStatusEventArgs(true, "Registered in File Explorer"));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start shell namespace mode");
            return false;
        }
    }
    
    private void StartFileWatcher()
    {
        var syncFolder = _config.Cache.LocalSyncFolder;
        if (!Directory.Exists(syncFolder))
        {
            return;
        }
        
        _fileWatcher = new FileSystemWatcher(syncFolder)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | 
                           NotifyFilters.LastWrite | NotifyFilters.Size,
            IncludeSubdirectories = true,
            EnableRaisingEvents = true
        };
        
        _fileWatcher.Created += OnFileCreated;
        _fileWatcher.Changed += OnFileChanged;
        _fileWatcher.Deleted += OnFileDeleted;
        _fileWatcher.Renamed += OnFileRenamed;
        _fileWatcher.Error += OnFileWatcherError;
        
        _logger.LogInformation("File watcher started for: {Path}", syncFolder);
    }
    
    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        // Skip system files
        if (ShouldSkipFile(e.FullPath)) return;
        
        var isDirectory = Directory.Exists(e.FullPath);
        var relativePath = GetRelativePath(e.FullPath);
        
        _logger.LogInformation("{Type} created: {Path}", isDirectory ? "Folder" : "File", relativePath);
        
        FileActivity?.Invoke(this, new FileActivityEventArgs(
            FileActivityType.Created, 
            relativePath, 
            isDirectory,
            "Created locally"));
        
        // Queue for upload
        _ = Task.Run(() => UploadFileAsync(e.FullPath, relativePath, isDirectory));
    }
    
    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (ShouldSkipFile(e.FullPath)) return;
        if (Directory.Exists(e.FullPath)) return; // Skip directory change events
        
        var relativePath = GetRelativePath(e.FullPath);
        
        _logger.LogDebug("File modified: {Path}", relativePath);
        
        FileActivity?.Invoke(this, new FileActivityEventArgs(
            FileActivityType.Modified, 
            relativePath, 
            false,
            "Modified locally"));
        
        // Queue for upload
        _ = Task.Run(() => UploadFileAsync(e.FullPath, relativePath, false));
    }
    
    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        if (ShouldSkipFile(e.FullPath)) return;
        
        var relativePath = GetRelativePath(e.FullPath);
        
        _logger.LogInformation("Deleted: {Path}", relativePath);
        
        FileActivity?.Invoke(this, new FileActivityEventArgs(
            FileActivityType.Deleted, 
            relativePath, 
            false,
            "Deleted locally"));
        
        // Queue for deletion in cloud
        _ = Task.Run(() => DeleteFileAsync(relativePath));
    }
    
    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        if (ShouldSkipFile(e.FullPath)) return;
        
        var oldRelativePath = GetRelativePath(e.OldFullPath);
        var newRelativePath = GetRelativePath(e.FullPath);
        var isDirectory = Directory.Exists(e.FullPath);
        
        _logger.LogInformation("Renamed: {OldPath} -> {NewPath}", oldRelativePath, newRelativePath);
        
        FileActivity?.Invoke(this, new FileActivityEventArgs(
            FileActivityType.Renamed, 
            newRelativePath, 
            isDirectory,
            $"Renamed from {oldRelativePath}"));
        
        // Delete old and upload new
        _ = Task.Run(async () =>
        {
            await DeleteFileAsync(oldRelativePath);
            await UploadFileAsync(e.FullPath, newRelativePath, isDirectory);
        });
    }
    
    private void OnFileWatcherError(object sender, ErrorEventArgs e)
    {
        _logger.LogError(e.GetException(), "File watcher error");
    }
    
    private bool ShouldSkipFile(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.StartsWith(".") || 
               fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) ||
               fileName.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase);
    }
    
    private string GetRelativePath(string fullPath)
    {
        return Path.GetRelativePath(_config.Cache.LocalSyncFolder, fullPath).Replace('\\', '/');
    }
    
    private async Task UploadFileAsync(string localPath, string relativePath, bool isDirectory)
    {
        try
        {
            if (isDirectory)
            {
                // For directories, just log - Azure Blob uses virtual directories
                _logger.LogDebug("Directory created (virtual in blob): {Path}", relativePath);
                return;
            }
            
            // Wait a moment for file to be ready (e.g., still being written)
            await Task.Delay(500);
            
            if (!File.Exists(localPath)) return;
            
            // Read file content
            var content = await File.ReadAllBytesAsync(localPath);
            
            FileActivity?.Invoke(this, new FileActivityEventArgs(
                FileActivityType.Uploading, 
                relativePath, 
                false,
                $"Uploading ({FormatSize(content.Length)})..."));
            
            // Upload to Azure using stream
            using var stream = new MemoryStream(content);
            await _cloudProvider.UploadFromStreamAsync(stream, relativePath);
            
            _logger.LogInformation("Uploaded to Azure: {Path} ({Size})", relativePath, FormatSize(content.Length));
            
            FileActivity?.Invoke(this, new FileActivityEventArgs(
                FileActivityType.Uploaded, 
                relativePath, 
                false,
                $"Uploaded to Azure ({FormatSize(content.Length)})"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload: {Path}", relativePath);
            
            // Provide helpful error message for permission issues
            var errorMessage = ex.Message;
            if (ex.Message.Contains("AuthorizationPermissionMismatch") || 
                ex.Message.Contains("403") ||
                ex.InnerException?.Message.Contains("AuthorizationPermissionMismatch") == true)
            {
                errorMessage = "Permission denied. For Entra ID auth, ensure your account has 'Storage Blob Data Contributor' role on the storage account.";
            }
            
            FileActivity?.Invoke(this, new FileActivityEventArgs(
                FileActivityType.Error, 
                relativePath, 
                false,
                $"Upload failed: {errorMessage}"));
        }
    }
    
    private async Task DeleteFileAsync(string relativePath)
    {
        try
        {
            await _cloudProvider.DeleteItemAsync(relativePath);
            _logger.LogInformation("Deleted from Azure: {Path}", relativePath);
            
            FileActivity?.Invoke(this, new FileActivityEventArgs(
                FileActivityType.Deleted, 
                relativePath, 
                false,
                "Deleted from Azure"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete from Azure: {Path}", relativePath);
            
            // Provide helpful error message for permission issues
            var errorMessage = ex.Message;
            if (ex.Message.Contains("AuthorizationPermissionMismatch") || 
                ex.Message.Contains("403") ||
                ex.InnerException?.Message.Contains("AuthorizationPermissionMismatch") == true)
            {
                errorMessage = "Permission denied. For Entra ID auth, ensure your account has 'Storage Blob Data Contributor' role on the storage account.";
            }
            
            FileActivity?.Invoke(this, new FileActivityEventArgs(
                FileActivityType.Error, 
                relativePath, 
                false,
                $"Delete failed: {errorMessage}"));
        }
    }
    
    private static string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }

    private async Task<bool> StartVirtualDriveModeAsync(CancellationToken cancellationToken)
    {
        if (!VirtualDriveManager.IsDokanInstalled())
        {
            _logger.LogError("Dokan driver is not installed. Virtual Drive mode is not available.");
            StatusChanged?.Invoke(this, new IntegrationStatusEventArgs(false, "Dokan driver not installed"));
            return false;
        }

        _virtualDriveManager = new VirtualDriveManager(
            _cloudProvider,
            _cacheManager,
            _config,
            _loggerFactory.CreateLogger<VirtualDriveManager>(),
            _loggerFactory.CreateLogger<BlobFileSystem>());
        
        // Forward file activity events from virtual drive
        _virtualDriveManager.FileActivity += (s, e) => FileActivity?.Invoke(this, e);

        var success = await _virtualDriveManager.MountAsync(cancellationToken);

        if (success)
        {
            _currentMode = IntegrationMode.VirtualDrive;
            _isActive = true;
            StatusChanged?.Invoke(this, new IntegrationStatusEventArgs(true, $"Mounted at {_virtualDriveManager.MountPoint}"));
        }

        return success;
    }

    private void RegisterNavigationPane()
    {
        try
        {
            // Register CLSID for the namespace extension
            // This makes the folder appear in the navigation pane
            var clsid = "{B1D3A6E8-7F4C-4B2A-9E1D-5C8F2A3B4D6E}"; // Unique CLSID for our app
            
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                $@"Software\Classes\CLSID\{clsid}");
            key?.SetValue("", "Azure Blob Storage");
            key?.SetValue("System.IsPinnedToNameSpaceTree", 1, Microsoft.Win32.RegistryValueKind.DWord);

            using var defaultIcon = key?.CreateSubKey("DefaultIcon");
            defaultIcon?.SetValue("", "%SystemRoot%\\System32\\shell32.dll,43"); // Cloud icon

            using var inProcServer = key?.CreateSubKey("InProcServer32");
            inProcServer?.SetValue("", "%SystemRoot%\\System32\\shell32.dll");

            using var shellFolder = key?.CreateSubKey("ShellFolder");
            // Use unchecked to allow large unsigned values to be stored as signed int
            shellFolder?.SetValue("Attributes", unchecked((int)0xF080004D), Microsoft.Win32.RegistryValueKind.DWord);
            shellFolder?.SetValue("FolderValueFlags", 0x28, Microsoft.Win32.RegistryValueKind.DWord);

            using var instance = key?.CreateSubKey("Instance");
            instance?.SetValue("CLSID", "{0E5AAE11-A475-4c5b-AB00-C66DE400274E}"); // Shell folder

            using var initPropertyBag = instance?.CreateSubKey("InitPropertyBag");
            initPropertyBag?.SetValue("TargetFolderPath", _config.Cache.LocalSyncFolder);
            initPropertyBag?.SetValue("Attributes", 0x11, Microsoft.Win32.RegistryValueKind.DWord);

            // Add to namespace root
            using var namespaceKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                $@"Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\{clsid}");
            namespaceKey?.SetValue("", "Azure Blob Storage");

            // Pin to Quick Access / Navigation pane
            using var hideDesktopKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                $@"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel");
            hideDesktopKey?.SetValue(clsid, 1, Microsoft.Win32.RegistryValueKind.DWord);

            _logger.LogInformation("Registered Azure Blob Storage in File Explorer navigation pane");
            
            // Tell Explorer to refresh
            RefreshExplorerNamespace();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register shell namespace");
        }
    }
    
    /// <summary>
    /// Notifies Explorer shell to refresh and pick up namespace changes
    /// </summary>
    private void RefreshExplorerNamespace()
    {
        try
        {
            // Notify shell of changes
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            _logger.LogInformation("Notified Explorer to refresh namespace");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to notify Explorer of namespace changes");
        }
    }
    
    // P/Invoke for shell notification
    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const int SHCNF_IDLIST = 0x0000;
    
    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);

    private void UnregisterNavigationPane()
    {
        try
        {
            var clsid = "{B1D3A6E8-7F4C-4B2A-9E1D-5C8F2A3B4D6E}";
            
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(
                $@"Software\Classes\CLSID\{clsid}", false);
            
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(
                $@"Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace\{clsid}", false);

            _logger.LogInformation("Unregistered Azure Blob Storage from File Explorer navigation pane");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unregister shell namespace");
        }
    }

    public void Dispose()
    {
        StopAsync().Wait();
    }
}

public class IntegrationModeChangedEventArgs : EventArgs
{
    public IntegrationMode OldMode { get; }
    public IntegrationMode NewMode { get; }

    public IntegrationModeChangedEventArgs(IntegrationMode oldMode, IntegrationMode newMode)
    {
        OldMode = oldMode;
        NewMode = newMode;
    }
}

public class IntegrationStatusEventArgs : EventArgs
{
    public bool IsActive { get; }
    public string Message { get; }

    public IntegrationStatusEventArgs(bool isActive, string message)
    {
        IsActive = isActive;
        Message = message;
    }
}

public class IntegrationModeInfo
{
    public IntegrationMode Mode { get; set; }
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public bool RequiresElevation { get; set; }
    public bool RequiresDriver { get; set; }
    public bool IsAvailable { get; set; }
    public string? UnavailableReason { get; set; }
}

public enum FileActivityType
{
    Created,
    Modified,
    Deleted,
    Renamed,
    Uploading,
    Uploaded,
    Downloading,
    Downloaded,
    Error
}

public class FileActivityEventArgs : EventArgs
{
    public FileActivityType ActivityType { get; }
    public string RelativePath { get; }
    public bool IsDirectory { get; }
    public string Message { get; }
    public DateTime Timestamp { get; }

    public FileActivityEventArgs(FileActivityType activityType, string relativePath, bool isDirectory, string message)
    {
        ActivityType = activityType;
        RelativePath = relativePath;
        IsDirectory = isDirectory;
        Message = message;
        Timestamp = DateTime.Now;
    }
}
