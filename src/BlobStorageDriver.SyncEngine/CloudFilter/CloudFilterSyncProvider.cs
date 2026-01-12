using System.Collections.Concurrent;
using BlobStorageDriver.CloudProvider;
using BlobStorageDriver.Common.Configuration;
using BlobStorageDriver.Common.Models;
using BlobStorageDriver.SyncEngine.Cache;
using BlobStorageDriver.SyncEngine.Integration;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;
using static BlobStorageDriver.SyncEngine.CloudFilter.CloudFilterNative;

namespace BlobStorageDriver.SyncEngine.CloudFilter;

/// <summary>
/// Cloud Filter Sync Provider implementation using the native Windows Cloud Files API.
/// This replaces the Dokan-based implementation with the same API used by OneDrive.
/// 
/// Key benefits over Dokan:
/// - No third-party driver installation required
/// - Native Windows integration (placeholder files, File Explorer integration)
/// - Better performance and lower memory usage
/// - Automatic hydration/dehydration support
/// - Built-in progress reporting and Shell integration
/// </summary>
public class CloudFilterSyncProvider : IDisposable
{
    private readonly ICloudStorageProvider _cloudProvider;
    private readonly LocalCacheManager _cacheManager;
    private readonly AppConfiguration _config;
    private readonly ILogger<CloudFilterSyncProvider> _logger;
    
    private long _connectionKey;
    private bool _isConnected;
    private readonly object _connectionLock = new();
    
    // Keep callback delegates alive to prevent GC
    private readonly CF_CALLBACK _fetchDataCallback;
    private readonly CF_CALLBACK _cancelFetchDataCallback;
    private readonly CF_CALLBACK _fetchPlaceholdersCallback;
    private readonly CF_CALLBACK _notifyDeleteCallback;
    private readonly CF_CALLBACK _notifyRenameCallback;
    private readonly CF_CALLBACK _notifyDehydrateCallback;
    
    // Track active transfers
    private readonly ConcurrentDictionary<long, TransferContext> _activeTransfers = new();
    
    public string? SyncRootPath { get; private set; }
    public bool IsConnected => _isConnected;
    
    /// <summary>
    /// Event raised when file activity occurs
    /// </summary>
    public event EventHandler<FileActivityEventArgs>? FileActivity;

    public CloudFilterSyncProvider(
        ICloudStorageProvider cloudProvider,
        LocalCacheManager cacheManager,
        AppConfiguration config,
        ILogger<CloudFilterSyncProvider> logger)
    {
        _cloudProvider = cloudProvider;
        _cacheManager = cacheManager;
        _config = config;
        _logger = logger;
        
        // Initialize callback delegates
        _fetchDataCallback = OnFetchData;
        _cancelFetchDataCallback = OnCancelFetchData;
        _fetchPlaceholdersCallback = OnFetchPlaceholders;
        _notifyDeleteCallback = OnNotifyDelete;
        _notifyRenameCallback = OnNotifyRename;
        _notifyDehydrateCallback = OnNotifyDehydrate;
    }

    /// <summary>
    /// Checks if the Cloud Filter API is available on this system
    /// </summary>
    public static bool IsAvailable()
    {
        return CloudFilterNative.IsCloudFilterAvailable();
    }

    /// <summary>
    /// Connects to the sync root and starts handling cloud file operations
    /// </summary>
    public Task<bool> ConnectAsync(string syncRootPath, CancellationToken cancellationToken = default)
    {
        lock (_connectionLock)
        {
            if (_isConnected)
            {
                _logger.LogWarning("Already connected to sync root: {SyncRootPath}", SyncRootPath);
                return Task.FromResult(true);
            }
        }

        try
        {
            _logger.LogInformation("Connecting to sync root: {SyncRootPath}", syncRootPath);

            // Ensure the sync root directory exists
            if (!Directory.Exists(syncRootPath))
            {
                Directory.CreateDirectory(syncRootPath);
            }

            // Set up callback registration table
            var callbackTable = new CF_CALLBACK_REGISTRATION[]
            {
                new() { Type = CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_FETCH_DATA, Callback = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(_fetchDataCallback) },
                new() { Type = CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_CANCEL_FETCH_DATA, Callback = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(_cancelFetchDataCallback) },
                new() { Type = CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_FETCH_PLACEHOLDERS, Callback = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(_fetchPlaceholdersCallback) },
                new() { Type = CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_NOTIFY_DELETE, Callback = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(_notifyDeleteCallback) },
                new() { Type = CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_NOTIFY_RENAME, Callback = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(_notifyRenameCallback) },
                new() { Type = CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_NOTIFY_DEHYDRATE, Callback = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(_notifyDehydrateCallback) },
                CF_CALLBACK_REGISTRATION.CF_CALLBACK_REGISTRATION_END
            };

            // Connect to the sync root
            var connectFlags = CF_CONNECT_FLAGS.CF_CONNECT_FLAG_REQUIRE_PROCESS_INFO | 
                              CF_CONNECT_FLAGS.CF_CONNECT_FLAG_REQUIRE_FULL_FILE_PATH;

            var hr = CfConnectSyncRoot(
                syncRootPath,
                callbackTable,
                IntPtr.Zero,
                connectFlags,
                out _connectionKey);

            ThrowIfFailed(hr, "CfConnectSyncRoot");

            // Update provider status to idle
            hr = CfUpdateSyncProviderStatus(_connectionKey, CF_SYNC_PROVIDER_STATUS.CF_PROVIDER_STATUS_IDLE);
            if (hr < 0)
            {
                _logger.LogWarning("Failed to update provider status: HRESULT 0x{HR:X8}", hr);
            }

            SyncRootPath = syncRootPath;
            
            lock (_connectionLock)
            {
                _isConnected = true;
            }

            _logger.LogInformation("Successfully connected to sync root: {SyncRootPath}", syncRootPath);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to sync root: {SyncRootPath}", syncRootPath);
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Disconnects from the sync root
    /// </summary>
    public Task DisconnectAsync()
    {
        lock (_connectionLock)
        {
            if (!_isConnected)
            {
                _logger.LogWarning("Not currently connected to a sync root");
                return Task.CompletedTask;
            }
        }

        try
        {
            _logger.LogInformation("Disconnecting from sync root: {SyncRootPath}", SyncRootPath);

            // Cancel any active transfers
            foreach (var transfer in _activeTransfers.Values)
            {
                transfer.CancellationSource.Cancel();
            }
            _activeTransfers.Clear();

            // Disconnect from the sync root
            var hr = CfDisconnectSyncRoot(_connectionKey);
            if (hr < 0)
            {
                _logger.LogWarning("CfDisconnectSyncRoot returned: HRESULT 0x{HR:X8}", hr);
            }

            lock (_connectionLock)
            {
                _isConnected = false;
                _connectionKey = 0;
                SyncRootPath = null;
            }

            _logger.LogInformation("Successfully disconnected from sync root");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disconnecting from sync root");
        }
        
        return Task.CompletedTask;
    }

    #region Callback Handlers

    /// <summary>
    /// Called when Windows needs to hydrate (download) file content
    /// </summary>
    private void OnFetchData(in CF_CALLBACK_INFO callbackInfo, in CF_CALLBACK_PARAMETERS callbackParameters)
    {
        var filePath = callbackInfo.NormalizedPath;
        var transferKey = callbackInfo.TransferKey;
        var requiredOffset = callbackParameters.Union.FetchData.RequiredFileOffset;
        var requiredLength = callbackParameters.Union.FetchData.RequiredLength;

        _logger.LogDebug("FetchData requested for: {FilePath}, Offset: {Offset}, Length: {Length}", 
            filePath, requiredOffset, requiredLength);

        // Create a transfer context
        var transferContext = new TransferContext
        {
            FilePath = filePath,
            TransferKey = transferKey,
            ConnectionKey = callbackInfo.ConnectionKey,
            RequiredOffset = requiredOffset,
            RequiredLength = requiredLength,
            TotalSize = callbackInfo.FileSize,
            CancellationSource = new CancellationTokenSource()
        };

        _activeTransfers[transferKey] = transferContext;

        // Start the transfer asynchronously
        _ = Task.Run(async () =>
        {
            try
            {
                await TransferDataAsync(transferContext);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during data transfer for: {FilePath}", filePath);
            }
            finally
            {
                _activeTransfers.TryRemove(transferKey, out _);
            }
        });
    }

    /// <summary>
    /// Called when a fetch data operation is cancelled
    /// </summary>
    private void OnCancelFetchData(in CF_CALLBACK_INFO callbackInfo, in CF_CALLBACK_PARAMETERS callbackParameters)
    {
        var transferKey = callbackInfo.TransferKey;
        _logger.LogDebug("CancelFetchData for TransferKey: {TransferKey}", transferKey);

        if (_activeTransfers.TryGetValue(transferKey, out var context))
        {
            context.CancellationSource.Cancel();
        }
    }

    /// <summary>
    /// Called when Windows needs to populate directory placeholders
    /// </summary>
    private void OnFetchPlaceholders(in CF_CALLBACK_INFO callbackInfo, in CF_CALLBACK_PARAMETERS callbackParameters)
    {
        var directoryPath = callbackInfo.NormalizedPath;
        var pattern = callbackParameters.Union.FetchPlaceholders.Pattern;
        
        // Copy the callback info to use in the async context (cannot use 'in' parameters in lambdas)
        var callbackInfoCopy = callbackInfo;

        _logger.LogDebug("FetchPlaceholders requested for: {DirectoryPath}, Pattern: {Pattern}", 
            directoryPath, pattern);

        // Start the placeholder population asynchronously
        _ = Task.Run(async () =>
        {
            try
            {
                await PopulatePlaceholdersAsync(callbackInfoCopy, directoryPath, pattern);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error populating placeholders for: {DirectoryPath}", directoryPath);
            }
        });
    }

    /// <summary>
    /// Called when a file is being deleted
    /// </summary>
    private void OnNotifyDelete(in CF_CALLBACK_INFO callbackInfo, in CF_CALLBACK_PARAMETERS callbackParameters)
    {
        var filePath = callbackInfo.NormalizedPath;
        _logger.LogDebug("NotifyDelete for: {FilePath}", filePath);

        FileActivity?.Invoke(this, new FileActivityEventArgs(
            FileActivityType.Deleted, filePath, false, "Deleted"));

        // Acknowledge the delete
        AcknowledgeOperation(callbackInfo, CF_OPERATION_TYPE.CF_OPERATION_TYPE_ACK_DELETE);
    }

    /// <summary>
    /// Called when a file is being renamed
    /// </summary>
    private void OnNotifyRename(in CF_CALLBACK_INFO callbackInfo, in CF_CALLBACK_PARAMETERS callbackParameters)
    {
        var sourcePath = callbackInfo.NormalizedPath;
        var targetPath = callbackParameters.Union.Rename.TargetPath;
        _logger.LogDebug("NotifyRename from: {SourcePath} to: {TargetPath}", sourcePath, targetPath);

        // Acknowledge the rename
        AcknowledgeOperation(callbackInfo, CF_OPERATION_TYPE.CF_OPERATION_TYPE_ACK_RENAME);
    }

    /// <summary>
    /// Called when a file is being dehydrated
    /// </summary>
    private void OnNotifyDehydrate(in CF_CALLBACK_INFO callbackInfo, in CF_CALLBACK_PARAMETERS callbackParameters)
    {
        var filePath = callbackInfo.NormalizedPath;
        _logger.LogDebug("NotifyDehydrate for: {FilePath}", filePath);

        // Acknowledge the dehydrate
        AcknowledgeOperation(callbackInfo, CF_OPERATION_TYPE.CF_OPERATION_TYPE_ACK_DEHYDRATE);
    }

    #endregion

    #region Data Transfer

    /// <summary>
    /// Transfers data from cloud storage to the placeholder file
    /// </summary>
    private async Task TransferDataAsync(TransferContext context)
    {
        var token = context.CancellationSource.Token;
        var relativePath = GetRelativePath(context.FilePath);

        FileActivity?.Invoke(this, new FileActivityEventArgs(
            FileActivityType.Downloading, relativePath, false, "Downloading..."));

        try
        {
            // Download the file from cloud storage
            var tempPath = Path.GetTempFileName();
            try
            {
                await _cloudProvider.DownloadFileAsync(relativePath, tempPath, null, token);

                if (token.IsCancellationRequested)
                    return;

                // Read the file content
                using var fileStream = new FileStream(tempPath, FileMode.Open, FileAccess.Read);
                var buffer = new byte[4 * 1024 * 1024]; // 4MB buffer
                long totalBytesRead = 0;
                long offset = context.RequiredOffset;

                fileStream.Position = offset;

                while (totalBytesRead < context.RequiredLength && !token.IsCancellationRequested)
                {
                    var bytesToRead = (int)Math.Min(buffer.Length, context.RequiredLength - totalBytesRead);
                    var bytesRead = await fileStream.ReadAsync(buffer, 0, bytesToRead, token);

                    if (bytesRead == 0)
                        break;

                    // Transfer the data to the placeholder
                    TransferDataToPlaceholder(context.ConnectionKey, context.TransferKey, buffer, offset, bytesRead);

                    totalBytesRead += bytesRead;
                    offset += bytesRead;

                    // Report progress
                    var progress = (int)((totalBytesRead * 100) / context.RequiredLength);
                    ReportProgress(context.ConnectionKey, context.TransferKey, context.TotalSize, totalBytesRead);

                    FileActivity?.Invoke(this, new FileActivityEventArgs(
                        FileActivityType.Downloading, relativePath, false, $"Downloading... {progress}%"));
                }

                FileActivity?.Invoke(this, new FileActivityEventArgs(
                    FileActivityType.Downloaded, relativePath, false, "Downloaded"));
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Transfer cancelled for: {FilePath}", context.FilePath);
            FileActivity?.Invoke(this, new FileActivityEventArgs(
                FileActivityType.Error, relativePath, false, "Download cancelled"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error transferring data for: {FilePath}", context.FilePath);
            FileActivity?.Invoke(this, new FileActivityEventArgs(
                FileActivityType.Error, relativePath, false, $"Download failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Transfers a chunk of data to the placeholder file
    /// </summary>
    private void TransferDataToPlaceholder(long connectionKey, long transferKey, byte[] buffer, long offset, int length)
    {
        var opInfo = new CF_OPERATION_INFO
        {
            StructSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<CF_OPERATION_INFO>(),
            Type = CF_OPERATION_TYPE.CF_OPERATION_TYPE_TRANSFER_DATA,
            ConnectionKey = connectionKey,
            TransferKey = transferKey,
            CorrelationVector = IntPtr.Zero,
            SyncStatus = IntPtr.Zero,
            RequestKey = 0
        };

        // Pin the buffer and create the operation parameters
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(buffer, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            var opParams = new CF_OPERATION_PARAMETERS
            {
                ParamSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<CF_OPERATION_PARAMETERS>(),
                Union = new CF_OPERATION_PARAMETERS_UNION
                {
                    TransferData = new CF_OPERATION_TRANSFER_DATA
                    {
                        Flags = CF_OPERATION_TRANSFER_DATA_FLAGS.CF_OPERATION_TRANSFER_DATA_FLAG_NONE,
                        CompletionStatus = 0, // STATUS_SUCCESS
                        Buffer = handle.AddrOfPinnedObject(),
                        Offset = offset,
                        Length = length
                    }
                }
            };

            var hr = CfExecute(in opInfo, ref opParams);
            if (hr < 0)
            {
                _logger.LogWarning("CfExecute (TransferData) failed: HRESULT 0x{HR:X8}", hr);
            }
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>
    /// Reports download progress to Windows
    /// </summary>
    private void ReportProgress(long connectionKey, long transferKey, long total, long completed)
    {
        var hr = CfReportProviderProgress(connectionKey, transferKey, total, completed);
        if (hr < 0)
        {
            _logger.LogDebug("CfReportProviderProgress failed: HRESULT 0x{HR:X8}", hr);
        }
    }

    #endregion

    #region Placeholder Population

    /// <summary>
    /// Populates directory placeholders from cloud storage
    /// </summary>
    private async Task PopulatePlaceholdersAsync(CF_CALLBACK_INFO callbackInfo, string directoryPath, string? pattern)
    {
        try
        {
            var relativePath = GetRelativePath(directoryPath);
            
            // List items from cloud storage
            var items = await _cloudProvider.ListItemsAsync(relativePath);

            if (items == null || !items.Any())
            {
                // Acknowledge completion even if no items
                AcknowledgeOperation(callbackInfo, CF_OPERATION_TYPE.CF_OPERATION_TYPE_TRANSFER_PLACEHOLDERS);
                return;
            }

            // Filter by pattern if specified
            if (!string.IsNullOrEmpty(pattern) && pattern != "*")
            {
                // Simple wildcard matching
                items = items.Where(i => MatchesPattern(i.Name, pattern)).ToList();
            }

            // Create placeholder structures
            var localDirPath = Path.Combine(SyncRootPath!, relativePath);
            var placeholders = new List<CF_PLACEHOLDER_CREATE_INFO>();

            foreach (var item in items)
            {
                var placeholder = new CF_PLACEHOLDER_CREATE_INFO
                {
                    RelativeFileName = item.Name,
                    Flags = CF_PLACEHOLDER_CREATE_FLAGS.CF_PLACEHOLDER_CREATE_FLAG_MARK_IN_SYNC,
                    FsMetadata = new CF_FS_METADATA
                    {
                        FileAttributes = item.IsDirectory ? 
                            (uint)System.IO.FileAttributes.Directory : 
                            (uint)System.IO.FileAttributes.Normal,
                        CreationTime = item.CreatedAt.ToFileTimeUtc(),
                        LastAccessTime = item.ModifiedAt.ToFileTimeUtc(),
                        LastWriteTime = item.ModifiedAt.ToFileTimeUtc(),
                        ChangeTime = item.ModifiedAt.ToFileTimeUtc(),
                        FileSize = item.Size
                    },
                    FileIdentity = IntPtr.Zero,
                    FileIdentityLength = 0
                };

                placeholders.Add(placeholder);
            }

            // Create the placeholders
            if (placeholders.Count > 0)
            {
                var hr = CfCreatePlaceholders(
                    localDirPath,
                    placeholders.ToArray(),
                    (uint)placeholders.Count,
                    CF_PLACEHOLDER_CREATE_FLAGS.CF_PLACEHOLDER_CREATE_FLAG_NONE,
                    out var entriesProcessed);

                if (hr < 0)
                {
                    _logger.LogWarning("CfCreatePlaceholders failed: HRESULT 0x{HR:X8}, Processed: {Processed}/{Total}",
                        hr, entriesProcessed, placeholders.Count);
                }
                else
                {
                    _logger.LogDebug("Created {Count} placeholders in {Directory}", entriesProcessed, directoryPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error populating placeholders for: {DirectoryPath}", directoryPath);
        }
    }

    /// <summary>
    /// Simple wildcard pattern matching
    /// </summary>
    private static bool MatchesPattern(string name, string pattern)
    {
        if (pattern == "*" || string.IsNullOrEmpty(pattern))
            return true;

        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        
        return System.Text.RegularExpressions.Regex.IsMatch(
            name, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Acknowledges a Cloud Filter operation
    /// </summary>
    private void AcknowledgeOperation(CF_CALLBACK_INFO callbackInfo, CF_OPERATION_TYPE operationType)
    {
        var opInfo = new CF_OPERATION_INFO
        {
            StructSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<CF_OPERATION_INFO>(),
            Type = operationType,
            ConnectionKey = callbackInfo.ConnectionKey,
            TransferKey = callbackInfo.TransferKey,
            CorrelationVector = callbackInfo.CorrelationVector,
            SyncStatus = IntPtr.Zero,
            RequestKey = callbackInfo.RequestKey
        };

        var opParams = new CF_OPERATION_PARAMETERS
        {
            ParamSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<CF_OPERATION_PARAMETERS>()
        };

        // Set appropriate union member based on operation type
        switch (operationType)
        {
            case CF_OPERATION_TYPE.CF_OPERATION_TYPE_ACK_DELETE:
                opParams.Union.AckDelete = new CF_OPERATION_ACK_DELETE
                {
                    Flags = CF_OPERATION_ACK_DELETE_FLAGS.CF_OPERATION_ACK_DELETE_FLAG_NONE,
                    CompletionStatus = 0
                };
                break;
            case CF_OPERATION_TYPE.CF_OPERATION_TYPE_ACK_RENAME:
                opParams.Union.AckRename = new CF_OPERATION_ACK_RENAME
                {
                    Flags = CF_OPERATION_ACK_RENAME_FLAGS.CF_OPERATION_ACK_RENAME_FLAG_NONE,
                    CompletionStatus = 0
                };
                break;
            case CF_OPERATION_TYPE.CF_OPERATION_TYPE_ACK_DEHYDRATE:
                opParams.Union.AckDehydrate = new CF_OPERATION_ACK_DEHYDRATE
                {
                    Flags = CF_OPERATION_ACK_DEHYDRATE_FLAGS.CF_OPERATION_ACK_DEHYDRATE_FLAG_NONE,
                    CompletionStatus = 0,
                    FileIdentity = IntPtr.Zero,
                    FileIdentityLength = 0
                };
                break;
        }

        var hr = CfExecute(in opInfo, ref opParams);
        if (hr < 0)
        {
            _logger.LogWarning("CfExecute ({OperationType}) failed: HRESULT 0x{HR:X8}", operationType, hr);
        }
    }

    /// <summary>
    /// Gets the relative path from a full path
    /// </summary>
    private string GetRelativePath(string fullPath)
    {
        if (string.IsNullOrEmpty(SyncRootPath))
            return fullPath;

        // Remove sync root path prefix
        if (fullPath.StartsWith(SyncRootPath, StringComparison.OrdinalIgnoreCase))
        {
            var relative = fullPath.Substring(SyncRootPath.Length);
            return relative.TrimStart('\\', '/');
        }

        return fullPath;
    }

    #endregion

    #region IDisposable

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        
        if (_isConnected)
        {
            DisconnectAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        }

        _disposed = true;
    }

    #endregion

    /// <summary>
    /// Context for tracking active data transfers
    /// </summary>
    private class TransferContext
    {
        public required string FilePath { get; init; }
        public long TransferKey { get; init; }
        public long ConnectionKey { get; init; }
        public long RequiredOffset { get; init; }
        public long RequiredLength { get; init; }
        public long TotalSize { get; init; }
        public required CancellationTokenSource CancellationSource { get; init; }
    }
}
