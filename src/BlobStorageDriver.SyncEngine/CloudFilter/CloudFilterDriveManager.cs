using BlobStorageDriver.CloudProvider;
using BlobStorageDriver.Common.Configuration;
using BlobStorageDriver.SyncEngine.Cache;
using BlobStorageDriver.SyncEngine.Integration;
using Microsoft.Extensions.Logging;

namespace BlobStorageDriver.SyncEngine.CloudFilter;

/// <summary>
/// Manages the Cloud Filter sync root lifecycle as an alternative to Dokan-based VirtualDriveManager.
/// 
/// This implementation uses the native Windows Cloud Files API (cfapi.dll) which provides:
/// - No third-party driver installation (unlike Dokan)
/// - Native Windows Shell integration
/// - Placeholder files with on-demand hydration
/// - Automatic File Explorer integration
/// - Better performance and stability
/// 
/// The Cloud Filter approach creates a sync root folder that can optionally be mapped
/// to a drive letter for legacy application compatibility. Drive letter mapping uses
/// the Windows DefineDosDevice API (similar to 'subst' command) - no Dokan required.
/// 
/// This is the same technology used by OneDrive, Dropbox, and other cloud sync providers.
/// </summary>
public class CloudFilterDriveManager : IDisposable
{
    private readonly ICloudStorageProvider _cloudProvider;
    private readonly LocalCacheManager _cacheManager;
    private readonly AppConfiguration _config;
    private readonly ILogger<CloudFilterDriveManager> _logger;
    
    private readonly SyncRootRegistrar _syncRootRegistrar;
    private readonly PlaceholderManager _placeholderManager;
    private readonly CloudFilterSyncProvider _syncProvider;
    private readonly DriveLetterMapper _driveLetterMapper;
    
    private bool _isActive;
    private string? _syncRootPath;
    private string? _syncRootId;
    private char? _driveLetter;
    private readonly object _stateLock = new();
    
    /// <summary>
    /// Event raised when file activity occurs (download, upload, etc.)
    /// </summary>
    public event EventHandler<FileActivityEventArgs>? FileActivity;
    
    public bool IsActive => _isActive;
    public string? SyncRootPath => _syncRootPath;
    
    /// <summary>
    /// Gets the mapped drive letter, if any
    /// </summary>
    public char? DriveLetter => _driveLetter;
    
    /// <summary>
    /// Gets the full drive path (e.g., "Z:\") if a drive letter is mapped
    /// </summary>
    public string? DrivePath => _driveLetter.HasValue ? $"{_driveLetter.Value}:\\" : null;
    
    public CloudFilterDriveManager(
        ICloudStorageProvider cloudProvider,
        LocalCacheManager cacheManager,
        AppConfiguration config,
        ILogger<CloudFilterDriveManager> logger,
        ILogger<SyncRootRegistrar> registrarLogger,
        ILogger<PlaceholderManager> placeholderLogger,
        ILogger<CloudFilterSyncProvider> providerLogger,
        ILogger<DriveLetterMapper> driveMapperLogger)
    {
        _cloudProvider = cloudProvider;
        _cacheManager = cacheManager;
        _config = config;
        _logger = logger;
        
        _syncRootRegistrar = new SyncRootRegistrar(registrarLogger);
        _placeholderManager = new PlaceholderManager(cloudProvider, placeholderLogger);
        _syncProvider = new CloudFilterSyncProvider(cloudProvider, cacheManager, config, providerLogger);
        _driveLetterMapper = new DriveLetterMapper(driveMapperLogger);
        
        // Forward file activity events
        _syncProvider.FileActivity += (s, e) => FileActivity?.Invoke(this, e);
    }

    /// <summary>
    /// Checks if Cloud Filter API is available on this system
    /// </summary>
    public static bool IsCloudFilterAvailable()
    {
        return CloudFilterSyncProvider.IsAvailable();
    }

    /// <summary>
    /// Checks if Dokan is installed (for comparison/fallback)
    /// </summary>
    public static bool IsDokanInstalled()
    {
        try
        {
            // Try to load Dokan library
            var handle = System.Runtime.InteropServices.NativeLibrary.Load("dokan2.dll");
            System.Runtime.InteropServices.NativeLibrary.Free(handle);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Activates the Cloud Filter sync root without a drive letter.
    /// The sync root folder will appear in File Explorer's navigation pane.
    /// </summary>
    public Task<bool> ActivateAsync(CancellationToken cancellationToken = default)
    {
        return ActivateAsync(driveLetter: null, cancellationToken);
    }

    /// <summary>
    /// Activates the Cloud Filter sync root with an optional drive letter mapping.
    /// </summary>
    /// <param name="driveLetter">Optional drive letter (e.g., 'Z'). If specified, the sync root
    /// will be accessible via both the folder path and the drive letter.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if activation succeeded</returns>
    public async Task<bool> ActivateAsync(char? driveLetter, CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (_isActive)
            {
                _logger.LogWarning("Cloud Filter sync root is already active at: {Path}", _syncRootPath);
                return true;
            }
        }

        try
        {
            // Verify Cloud Filter is available
            if (!IsCloudFilterAvailable())
            {
                _logger.LogError("Cloud Filter API is not available on this system. Windows 10 version 1709 or later is required.");
                return false;
            }

            // Determine sync root path
            _syncRootPath = _config.Cache.LocalSyncFolder;
            if (string.IsNullOrEmpty(_syncRootPath))
            {
                _syncRootPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "AzureBlobStorage");
            }

            _logger.LogInformation("Activating Cloud Filter sync root at: {Path}", _syncRootPath);

            // Ensure sync root directory exists
            if (!Directory.Exists(_syncRootPath))
            {
                Directory.CreateDirectory(_syncRootPath);
            }

            // Register with Windows Shell
            var accountId = GetAccountId();
            var displayName = _config.Integration.VolumeLabel ?? "Azure Blob Storage";
            
            _syncRootId = await _syncRootRegistrar.RegisterAsync(_syncRootPath, accountId, displayName);
            _logger.LogInformation("Registered sync root with ID: {SyncRootId}", _syncRootId);

            // Connect the sync provider
            if (!await _syncProvider.ConnectAsync(_syncRootPath, cancellationToken))
            {
                _logger.LogError("Failed to connect sync provider");
                await _syncRootRegistrar.UnregisterAsync(accountId);
                return false;
            }

            // Create initial placeholders from cloud storage
            await _placeholderManager.CreatePlaceholdersAsync(_syncRootPath, "", true, cancellationToken);

            // Map drive letter if requested
            if (driveLetter.HasValue)
            {
                _driveLetter = _driveLetterMapper.MapDriveLetter(_syncRootPath, driveLetter.Value);
                if (_driveLetter.HasValue)
                {
                    _logger.LogInformation("Mapped sync root to drive letter {Letter}:", _driveLetter.Value);
                }
                else
                {
                    _logger.LogWarning("Failed to map drive letter {Letter}:, continuing without drive mapping", 
                        driveLetter.Value);
                }
            }

            lock (_stateLock)
            {
                _isActive = true;
            }

            var message = _driveLetter.HasValue 
                ? $"Successfully activated Cloud Filter sync root at: {_syncRootPath} (mapped to {_driveLetter.Value}:)"
                : $"Successfully activated Cloud Filter sync root at: {_syncRootPath}";
            _logger.LogInformation(message);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to activate Cloud Filter sync root");
            return false;
        }
    }

    /// <summary>
    /// Deactivates the Cloud Filter sync root and unmaps any drive letter
    /// </summary>
    public async Task DeactivateAsync()
    {
        lock (_stateLock)
        {
            if (!_isActive)
            {
                _logger.LogWarning("Cloud Filter sync root is not currently active");
                return;
            }
        }

        try
        {
            _logger.LogInformation("Deactivating Cloud Filter sync root at: {Path}", _syncRootPath);

            // Unmap drive letter if mapped
            if (_driveLetter.HasValue)
            {
                _logger.LogInformation("Unmapping drive letter {Letter}:", _driveLetter.Value);
                _driveLetterMapper.UnmapDriveLetter();
                _driveLetter = null;
            }

            // Disconnect sync provider
            await _syncProvider.DisconnectAsync();

            // Optionally unregister (usually we keep registration for seamless experience)
            // await _syncRootRegistrar.UnregisterAsync(GetAccountId());

            lock (_stateLock)
            {
                _isActive = false;
            }

            _logger.LogInformation("Successfully deactivated Cloud Filter sync root");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deactivating Cloud Filter sync root");
        }
    }

    /// <summary>
    /// Refreshes placeholders from cloud storage
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!_isActive || string.IsNullOrEmpty(_syncRootPath))
        {
            _logger.LogWarning("Cannot refresh: sync root is not active");
            return;
        }

        await _placeholderManager.CreatePlaceholdersAsync(_syncRootPath, "", true, cancellationToken);
    }

    /// <summary>
    /// Forces hydration (download) of a specific file or directory
    /// </summary>
    public async Task HydrateAsync(string relativePath)
    {
        if (!_isActive || string.IsNullOrEmpty(_syncRootPath))
        {
            _logger.LogWarning("Cannot hydrate: sync root is not active");
            return;
        }

        var localPath = Path.Combine(_syncRootPath, relativePath);
        await _placeholderManager.HydrateAsync(localPath);
    }

    /// <summary>
    /// Dehydrates a file (frees local space while keeping placeholder)
    /// </summary>
    public async Task DehydrateAsync(string relativePath)
    {
        if (!_isActive || string.IsNullOrEmpty(_syncRootPath))
        {
            _logger.LogWarning("Cannot dehydrate: sync root is not active");
            return;
        }

        var localPath = Path.Combine(_syncRootPath, relativePath);
        await _placeholderManager.DehydrateAsync(localPath);
    }

    /// <summary>
    /// Pins a file to always be available offline
    /// </summary>
    public async Task PinAsync(string relativePath, bool recursive = false)
    {
        if (!_isActive || string.IsNullOrEmpty(_syncRootPath))
        {
            _logger.LogWarning("Cannot pin: sync root is not active");
            return;
        }

        var localPath = Path.Combine(_syncRootPath, relativePath);
        await _placeholderManager.SetPinStateAsync(
            localPath, 
            CloudFilterNative.CF_PIN_STATE.CF_PIN_STATE_PINNED, 
            recursive);
    }

    /// <summary>
    /// Unpins a file (allows dehydration to free space)
    /// </summary>
    public async Task UnpinAsync(string relativePath, bool recursive = false)
    {
        if (!_isActive || string.IsNullOrEmpty(_syncRootPath))
        {
            _logger.LogWarning("Cannot unpin: sync root is not active");
            return;
        }

        var localPath = Path.Combine(_syncRootPath, relativePath);
        await _placeholderManager.SetPinStateAsync(
            localPath, 
            CloudFilterNative.CF_PIN_STATE.CF_PIN_STATE_UNPINNED, 
            recursive);
    }

    #region Drive Letter Management

    /// <summary>
    /// Maps the sync root to a drive letter (can be called after activation)
    /// </summary>
    /// <param name="driveLetter">Drive letter to use, or null to auto-select</param>
    /// <returns>The mapped drive letter, or null if mapping failed</returns>
    public char? MapDriveLetter(char? driveLetter = null)
    {
        if (!_isActive || string.IsNullOrEmpty(_syncRootPath))
        {
            _logger.LogWarning("Cannot map drive letter: sync root is not active");
            return null;
        }

        if (_driveLetter.HasValue)
        {
            _logger.LogWarning("Drive letter {Letter}: is already mapped", _driveLetter.Value);
            return _driveLetter;
        }

        _driveLetter = _driveLetterMapper.MapDriveLetter(_syncRootPath, driveLetter);
        return _driveLetter;
    }

    /// <summary>
    /// Unmaps the current drive letter
    /// </summary>
    public bool UnmapDriveLetter()
    {
        if (!_driveLetter.HasValue)
        {
            _logger.LogDebug("No drive letter is currently mapped");
            return true;
        }

        var result = _driveLetterMapper.UnmapDriveLetter();
        if (result)
        {
            _driveLetter = null;
        }
        return result;
    }

    /// <summary>
    /// Checks if a specific drive letter is available for mapping
    /// </summary>
    public static bool IsDriveLetterAvailable(char driveLetter)
    {
        return DriveLetterMapper.IsDriveLetterAvailable(driveLetter);
    }

    /// <summary>
    /// Gets the first available drive letter (searching Z to D)
    /// </summary>
    public static char? GetFirstAvailableDriveLetter()
    {
        return DriveLetterMapper.GetFirstAvailableDriveLetter();
    }

    #endregion

    /// <summary>
    /// Gets a unique account identifier for sync root registration
    /// </summary>
    private string GetAccountId()
    {
        // Use storage account name or connection string hash
        var connectionString = _config.AzureBlob?.ConnectionString;
        if (!string.IsNullOrEmpty(connectionString))
        {
            // Extract account name from connection string
            var parts = connectionString.Split(';');
            var accountPart = parts.FirstOrDefault(p => p.StartsWith("AccountName=", StringComparison.OrdinalIgnoreCase));
            if (accountPart != null)
            {
                return accountPart.Split('=')[1];
            }
            
            // Fallback to hash
            return connectionString.GetHashCode().ToString("X8");
        }

        // Try account name directly
        if (!string.IsNullOrEmpty(_config.AzureBlob?.AccountName))
        {
            return _config.AzureBlob.AccountName;
        }

        return "default";
    }

    #region IDisposable

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;

        if (_isActive)
        {
            DeactivateAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        }

        _driveLetterMapper.Dispose();
        _syncProvider.Dispose();
        _disposed = true;
    }

    #endregion
}
