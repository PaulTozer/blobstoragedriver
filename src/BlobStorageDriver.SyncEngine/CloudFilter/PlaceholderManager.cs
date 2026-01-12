using BlobStorageDriver.CloudProvider;
using BlobStorageDriver.Common.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using static BlobStorageDriver.SyncEngine.CloudFilter.CloudFilterNative;

namespace BlobStorageDriver.SyncEngine.CloudFilter;

/// <summary>
/// Manages placeholder files for the Cloud Filter sync root.
/// Placeholder files are lightweight representations of cloud files that
/// consume minimal disk space until they are accessed (hydrated).
/// </summary>
public class PlaceholderManager
{
    private readonly ICloudStorageProvider _cloudProvider;
    private readonly ILogger<PlaceholderManager> _logger;

    public PlaceholderManager(
        ICloudStorageProvider cloudProvider,
        ILogger<PlaceholderManager> logger)
    {
        _cloudProvider = cloudProvider;
        _logger = logger;
    }

    /// <summary>
    /// Creates placeholder files/directories in the sync root from cloud items
    /// </summary>
    /// <param name="syncRootPath">The local sync root path</param>
    /// <param name="cloudPath">The cloud path to sync from</param>
    /// <param name="recursive">Whether to recursively create placeholders</param>
    public async Task CreatePlaceholdersAsync(
        string syncRootPath, 
        string cloudPath = "", 
        bool recursive = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Creating placeholders from cloud path: {CloudPath}", cloudPath);

            // List items from cloud
            var items = await _cloudProvider.ListItemsAsync(cloudPath, cancellationToken);
            if (items == null || !items.Any())
            {
                _logger.LogDebug("No items found at cloud path: {CloudPath}", cloudPath);
                return;
            }

            // Determine local directory path
            var localDirPath = string.IsNullOrEmpty(cloudPath) 
                ? syncRootPath 
                : Path.Combine(syncRootPath, cloudPath);

            // Ensure directory exists
            if (!Directory.Exists(localDirPath))
            {
                Directory.CreateDirectory(localDirPath);
            }

            // Process directories first
            var directories = items.Where(i => i.IsDirectory).ToList();
            var files = items.Where(i => !i.IsDirectory).ToList();

            // Create directory placeholders
            foreach (var dir in directories)
            {
                if (cancellationToken.IsCancellationRequested) break;
                
                await CreateDirectoryPlaceholderAsync(localDirPath, dir);

                // Recurse into directories
                if (recursive)
                {
                    var subPath = string.IsNullOrEmpty(cloudPath) 
                        ? dir.Name 
                        : Path.Combine(cloudPath, dir.Name);
                    await CreatePlaceholdersAsync(syncRootPath, subPath, true, cancellationToken);
                }
            }

            // Create file placeholders in batches
            await CreateFilePlaceholdersBatchAsync(localDirPath, files);

            _logger.LogInformation("Created {ItemCount} placeholders at: {LocalPath}", items.Count(), localDirPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating placeholders at: {CloudPath}", cloudPath);
            throw;
        }
    }

    /// <summary>
    /// Creates a single directory placeholder
    /// </summary>
    private Task CreateDirectoryPlaceholderAsync(string parentPath, CloudFileItem item)
    {
        var dirPath = Path.Combine(parentPath, item.Name);

        try
        {
            // Check if already exists
            if (Directory.Exists(dirPath))
            {
                // Could update placeholder metadata here if needed
                return Task.CompletedTask;
            }

            // Create the directory
            Directory.CreateDirectory(dirPath);

            // Convert to placeholder
            using var handle = OpenDirectoryHandle(dirPath);
            if (handle != null && !handle.IsInvalid)
            {
                var hr = CfConvertToPlaceholder(
                    handle,
                    IntPtr.Zero,
                    0,
                    CF_CONVERT_FLAGS.CF_CONVERT_FLAG_MARK_IN_SYNC,
                    out _,
                    IntPtr.Zero);

                if (hr < 0)
                {
                    _logger.LogWarning("Failed to convert directory to placeholder: {Path}, HRESULT: 0x{HR:X8}", 
                        dirPath, hr);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error creating directory placeholder: {Path}", dirPath);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Creates file placeholders in a batch
    /// </summary>
    private Task CreateFilePlaceholdersBatchAsync(string parentPath, IEnumerable<CloudFileItem> items)
    {
        var itemList = items.ToList();
        if (!itemList.Any()) return Task.CompletedTask;

        var placeholders = itemList.Select(item => new CF_PLACEHOLDER_CREATE_INFO
        {
            RelativeFileName = item.Name,
            Flags = CF_PLACEHOLDER_CREATE_FLAGS.CF_PLACEHOLDER_CREATE_FLAG_MARK_IN_SYNC,
            FsMetadata = new CF_FS_METADATA
            {
                FileAttributes = (uint)System.IO.FileAttributes.Normal,
                CreationTime = item.CreatedAt.ToFileTimeUtc(),
                LastAccessTime = item.ModifiedAt.ToFileTimeUtc(),
                LastWriteTime = item.ModifiedAt.ToFileTimeUtc(),
                ChangeTime = item.ModifiedAt.ToFileTimeUtc(),
                FileSize = item.Size
            },
            FileIdentity = IntPtr.Zero,
            FileIdentityLength = 0
        }).ToArray();

        try
        {
            var hr = CfCreatePlaceholders(
                parentPath,
                placeholders,
                (uint)placeholders.Length,
                CF_PLACEHOLDER_CREATE_FLAGS.CF_PLACEHOLDER_CREATE_FLAG_NONE,
                out var entriesProcessed);

            if (hr < 0)
            {
                _logger.LogWarning("CfCreatePlaceholders failed: HRESULT 0x{HR:X8}, Processed: {Processed}/{Total}",
                    hr, entriesProcessed, placeholders.Length);

                // Log individual failures
                for (int i = 0; i < placeholders.Length; i++)
                {
                    if (placeholders[i].Result < 0)
                    {
                        _logger.LogDebug("Placeholder creation failed for {Name}: HRESULT 0x{HR:X8}",
                            placeholders[i].RelativeFileName, placeholders[i].Result);
                    }
                }
            }
            else
            {
                _logger.LogDebug("Created {Count} file placeholders in {Path}", entriesProcessed, parentPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating file placeholders in: {Path}", parentPath);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Creates a single placeholder file
    /// </summary>
    public async Task CreatePlaceholderAsync(string syncRootPath, CloudFileItem item)
    {
        var localPath = Path.Combine(syncRootPath, item.RelativePath);
        var parentPath = Path.GetDirectoryName(localPath)!;

        // Ensure parent directory exists
        if (!Directory.Exists(parentPath))
        {
            Directory.CreateDirectory(parentPath);
        }

        if (item.IsDirectory)
        {
            await CreateDirectoryPlaceholderAsync(parentPath, item);
        }
        else
        {
            var placeholder = new CF_PLACEHOLDER_CREATE_INFO
            {
                RelativeFileName = item.Name,
                Flags = CF_PLACEHOLDER_CREATE_FLAGS.CF_PLACEHOLDER_CREATE_FLAG_MARK_IN_SYNC,
                FsMetadata = new CF_FS_METADATA
                {
                    FileAttributes = (uint)System.IO.FileAttributes.Normal,
                    CreationTime = item.CreatedAt.ToFileTimeUtc(),
                    LastAccessTime = item.ModifiedAt.ToFileTimeUtc(),
                    LastWriteTime = item.ModifiedAt.ToFileTimeUtc(),
                    ChangeTime = item.ModifiedAt.ToFileTimeUtc(),
                    FileSize = item.Size
                },
                FileIdentity = IntPtr.Zero,
                FileIdentityLength = 0
            };

            var hr = CfCreatePlaceholders(
                parentPath,
                new[] { placeholder },
                1,
                CF_PLACEHOLDER_CREATE_FLAGS.CF_PLACEHOLDER_CREATE_FLAG_NONE,
                out _);

            if (hr < 0)
            {
                _logger.LogWarning("Failed to create placeholder for {Path}: HRESULT 0x{HR:X8}", 
                    localPath, hr);
            }
        }
    }

    /// <summary>
    /// Updates an existing placeholder's metadata
    /// </summary>
    public Task UpdatePlaceholderAsync(string localPath, CloudFileItem item)
    {
        try
        {
            using var handle = OpenFileHandle(localPath);
            if (handle == null || handle.IsInvalid)
            {
                _logger.LogWarning("Failed to open file for update: {Path}", localPath);
                return Task.CompletedTask;
            }

            var metadata = new CF_FS_METADATA
            {
                FileAttributes = item.IsDirectory ? 
                    (uint)System.IO.FileAttributes.Directory : 
                    (uint)System.IO.FileAttributes.Normal,
                CreationTime = item.CreatedAt.ToFileTimeUtc(),
                LastAccessTime = item.ModifiedAt.ToFileTimeUtc(),
                LastWriteTime = item.ModifiedAt.ToFileTimeUtc(),
                ChangeTime = item.ModifiedAt.ToFileTimeUtc(),
                FileSize = item.Size
            };

            var hr = CfUpdatePlaceholder(
                handle,
                in metadata,
                IntPtr.Zero,
                0,
                IntPtr.Zero,
                0,
                0, // Update flags
                out _,
                IntPtr.Zero);

            if (hr < 0)
            {
                _logger.LogWarning("Failed to update placeholder {Path}: HRESULT 0x{HR:X8}", 
                    localPath, hr);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating placeholder: {Path}", localPath);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Converts an existing file to a placeholder (dehydrates it)
    /// </summary>
    public Task ConvertToPlaceholderAsync(string localPath, bool dehydrate = true)
    {
        try
        {
            using var handle = OpenFileHandle(localPath);
            if (handle == null || handle.IsInvalid)
            {
                _logger.LogWarning("Failed to open file for conversion: {Path}", localPath);
                return Task.CompletedTask;
            }

            var flags = CF_CONVERT_FLAGS.CF_CONVERT_FLAG_MARK_IN_SYNC;
            if (dehydrate)
            {
                flags |= CF_CONVERT_FLAGS.CF_CONVERT_FLAG_DEHYDRATE;
            }

            var hr = CfConvertToPlaceholder(
                handle,
                IntPtr.Zero,
                0,
                flags,
                out _,
                IntPtr.Zero);

            if (hr < 0)
            {
                _logger.LogWarning("Failed to convert file to placeholder {Path}: HRESULT 0x{HR:X8}", 
                    localPath, hr);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error converting to placeholder: {Path}", localPath);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Reverts a placeholder back to a regular file
    /// </summary>
    public Task RevertPlaceholderAsync(string localPath)
    {
        try
        {
            using var handle = OpenFileHandle(localPath);
            if (handle == null || handle.IsInvalid)
            {
                _logger.LogWarning("Failed to open file for revert: {Path}", localPath);
                return Task.CompletedTask;
            }

            var hr = CfRevertPlaceholder(handle, 0, IntPtr.Zero);
            if (hr < 0)
            {
                _logger.LogWarning("Failed to revert placeholder {Path}: HRESULT 0x{HR:X8}", 
                    localPath, hr);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reverting placeholder: {Path}", localPath);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Sets the in-sync state of a placeholder
    /// </summary>
    public Task SetInSyncStateAsync(string localPath, bool inSync)
    {
        try
        {
            using var handle = OpenFileHandle(localPath);
            if (handle == null || handle.IsInvalid)
            {
                _logger.LogWarning("Failed to open file for in-sync state: {Path}", localPath);
                return Task.CompletedTask;
            }

            var state = inSync 
                ? CF_IN_SYNC_STATE.CF_IN_SYNC_STATE_IN_SYNC 
                : CF_IN_SYNC_STATE.CF_IN_SYNC_STATE_NOT_IN_SYNC;

            var hr = CfSetInSyncState(
                handle,
                state,
                CF_SET_IN_SYNC_FLAGS.CF_SET_IN_SYNC_FLAG_NONE,
                IntPtr.Zero);

            if (hr < 0)
            {
                _logger.LogWarning("Failed to set in-sync state for {Path}: HRESULT 0x{HR:X8}", 
                    localPath, hr);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting in-sync state: {Path}", localPath);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Sets the pin state of a placeholder (pinned = always available offline)
    /// </summary>
    public Task SetPinStateAsync(string localPath, CF_PIN_STATE pinState, bool recursive = false)
    {
        try
        {
            using var handle = OpenFileHandle(localPath);
            if (handle == null || handle.IsInvalid)
            {
                _logger.LogWarning("Failed to open file for pin state: {Path}", localPath);
                return Task.CompletedTask;
            }

            var flags = recursive 
                ? CF_SET_PIN_FLAGS.CF_SET_PIN_FLAG_RECURSE 
                : CF_SET_PIN_FLAGS.CF_SET_PIN_FLAG_NONE;

            var hr = CfSetPinState(handle, pinState, flags, IntPtr.Zero);
            if (hr < 0)
            {
                _logger.LogWarning("Failed to set pin state for {Path}: HRESULT 0x{HR:X8}", 
                    localPath, hr);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting pin state: {Path}", localPath);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Hydrates a placeholder file (downloads the content)
    /// </summary>
    public Task HydrateAsync(string localPath)
    {
        try
        {
            using var handle = OpenFileHandle(localPath);
            if (handle == null || handle.IsInvalid)
            {
                _logger.LogWarning("Failed to open file for hydration: {Path}", localPath);
                return Task.CompletedTask;
            }

            var hr = CfHydratePlaceholder(
                handle,
                0, // Start at beginning
                -1, // Entire file
                CF_HYDRATE_FLAGS.CF_HYDRATE_FLAG_NONE,
                IntPtr.Zero);

            if (hr < 0)
            {
                _logger.LogWarning("Failed to hydrate placeholder {Path}: HRESULT 0x{HR:X8}", 
                    localPath, hr);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error hydrating placeholder: {Path}", localPath);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Dehydrates a file (converts it back to a placeholder)
    /// </summary>
    public Task DehydrateAsync(string localPath)
    {
        try
        {
            using var handle = OpenFileHandle(localPath);
            if (handle == null || handle.IsInvalid)
            {
                _logger.LogWarning("Failed to open file for dehydration: {Path}", localPath);
                return Task.CompletedTask;
            }

            var hr = CfDehydratePlaceholder(
                handle,
                0, // Start at beginning
                -1, // Entire file
                CF_DEHYDRATE_FLAGS.CF_DEHYDRATE_FLAG_NONE,
                IntPtr.Zero);

            if (hr < 0)
            {
                _logger.LogWarning("Failed to dehydrate placeholder {Path}: HRESULT 0x{HR:X8}", 
                    localPath, hr);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error dehydrating placeholder: {Path}", localPath);
        }

        return Task.CompletedTask;
    }

    #region File Handle Helpers

    private static SafeFileHandle? OpenFileHandle(string path)
    {
        try
        {
            return File.OpenHandle(
                path,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.ReadWrite | FileShare.Delete,
                FileOptions.None);
        }
        catch
        {
            return null;
        }
    }

    private static SafeFileHandle? OpenDirectoryHandle(string path)
    {
        try
        {
            // Use P/Invoke to open directory handle with proper flags
            var handle = CreateFile(
                path,
                0x80000000 | 0x40000000, // GENERIC_READ | GENERIC_WRITE
                0x00000001 | 0x00000002 | 0x00000004, // FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE
                IntPtr.Zero,
                3, // OPEN_EXISTING
                0x02000000, // FILE_FLAG_BACKUP_SEMANTICS (required for directories)
                IntPtr.Zero);

            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                return null;

            return new SafeFileHandle(handle, true);
        }
        catch
        {
            return null;
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    #endregion
}
