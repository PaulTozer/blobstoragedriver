using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Windows.Security.Cryptography;
using Windows.Storage;
using Windows.Storage.Provider;

namespace BlobStorageDriver.SyncEngine.CloudFilter;

/// <summary>
/// Manages sync root registration with the Windows Shell using the Cloud Files API.
/// This provides File Explorer integration including:
/// - Navigation pane entry with custom icon and name
/// - Automatic hydration state icons on files
/// - Context menu integration
/// - Progress indication during hydration
/// </summary>
public class SyncRootRegistrar
{
    private readonly ILogger<SyncRootRegistrar> _logger;
    
    // Provider identification
    private const string ProviderId = "BlobStorageDriver";
    private const string ProviderVersion = "1.0.0";
    private const string DisplayName = "Azure Blob Storage";
    
    public SyncRootRegistrar(ILogger<SyncRootRegistrar> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Registers a sync root with the Windows Shell
    /// </summary>
    /// <param name="syncRootPath">The local folder path to use as the sync root</param>
    /// <param name="accountId">A unique identifier for the account (e.g., storage account name)</param>
    /// <param name="displayName">The display name shown in File Explorer</param>
    /// <returns>The sync root ID that was registered</returns>
    public async Task<string> RegisterAsync(
        string syncRootPath, 
        string accountId, 
        string? displayName = null)
    {
        try
        {
            _logger.LogInformation("Registering sync root at: {SyncRootPath}", syncRootPath);

            // Ensure directory exists
            if (!Directory.Exists(syncRootPath))
            {
                Directory.CreateDirectory(syncRootPath);
            }

            // Generate a unique sync root ID
            var syncRootId = GetSyncRootId(accountId);
            
            // Check if already registered
            try
            {
                var existingRoot = StorageProviderSyncRootManager.GetSyncRootInformationForFolder(
                    await StorageFolder.GetFolderFromPathAsync(syncRootPath));
                
                if (existingRoot != null && existingRoot.Id == syncRootId)
                {
                    _logger.LogInformation("Sync root already registered: {SyncRootId}", syncRootId);
                    return syncRootId;
                }
            }
            catch
            {
                // Not registered yet, continue with registration
            }

            // Create sync root info
            var info = new StorageProviderSyncRootInfo
            {
                Id = syncRootId,
                Path = await StorageFolder.GetFolderFromPathAsync(syncRootPath),
                DisplayNameResource = displayName ?? DisplayName,
                IconResource = GetIconResource(),
                Version = ProviderVersion,
                HydrationPolicy = StorageProviderHydrationPolicy.Full,
                HydrationPolicyModifier = StorageProviderHydrationPolicyModifier.None,
                PopulationPolicy = StorageProviderPopulationPolicy.Full,
                InSyncPolicy = StorageProviderInSyncPolicy.FileCreationTime | 
                              StorageProviderInSyncPolicy.DirectoryCreationTime,
                HardlinkPolicy = StorageProviderHardlinkPolicy.None,
                ShowSiblingsAsGroup = false,
                RecycleBinUri = null // Optional: URI for recycle bin
            };

            // Set context (optional data for the provider)
            var contextString = $"{syncRootPath}|{accountId}";
            info.Context = CryptographicBuffer.ConvertStringToBinary(
                contextString, BinaryStringEncoding.Utf8);

            // Add custom state definitions (for custom icons/status)
            AddCustomStates(info);

            // Register with the Shell
            StorageProviderSyncRootManager.Register(info);

            // Give the Shell time to process
            await Task.Delay(500);

            _logger.LogInformation("Successfully registered sync root: {SyncRootId}", syncRootId);
            return syncRootId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register sync root at: {SyncRootPath}", syncRootPath);
            throw;
        }
    }

    /// <summary>
    /// Unregisters a sync root from the Windows Shell
    /// </summary>
    public Task UnregisterAsync(string accountId)
    {
        try
        {
            var syncRootId = GetSyncRootId(accountId);
            _logger.LogInformation("Unregistering sync root: {SyncRootId}", syncRootId);

            StorageProviderSyncRootManager.Unregister(syncRootId);

            _logger.LogInformation("Successfully unregistered sync root: {SyncRootId}", syncRootId);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unregister sync root for account: {AccountId}", accountId);
            throw;
        }
    }

    /// <summary>
    /// Unregisters a sync root by path
    /// </summary>
    public async Task UnregisterByPathAsync(string syncRootPath)
    {
        try
        {
            _logger.LogInformation("Unregistering sync root at: {SyncRootPath}", syncRootPath);

            var folder = await StorageFolder.GetFolderFromPathAsync(syncRootPath);
            var info = StorageProviderSyncRootManager.GetSyncRootInformationForFolder(folder);
            
            if (info != null)
            {
                StorageProviderSyncRootManager.Unregister(info.Id);
                _logger.LogInformation("Successfully unregistered sync root: {SyncRootId}", info.Id);
            }
            else
            {
                _logger.LogWarning("No sync root found at: {SyncRootPath}", syncRootPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unregister sync root at: {SyncRootPath}", syncRootPath);
            throw;
        }
    }

    /// <summary>
    /// Checks if a path is registered as a sync root
    /// </summary>
    public async Task<bool> IsRegisteredAsync(string syncRootPath)
    {
        try
        {
            var folder = await StorageFolder.GetFolderFromPathAsync(syncRootPath);
            var info = StorageProviderSyncRootManager.GetSyncRootInformationForFolder(folder);
            return info != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets all registered sync roots for this provider
    /// </summary>
    public IReadOnlyList<StorageProviderSyncRootInfo> GetAllRegisteredRoots()
    {
        try
        {
            return StorageProviderSyncRootManager.GetCurrentSyncRoots();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get registered sync roots");
            return Array.Empty<StorageProviderSyncRootInfo>();
        }
    }

    /// <summary>
    /// Generates a unique sync root ID based on the current user and account
    /// </summary>
    private string GetSyncRootId(string accountId)
    {
        // Format: ProviderId!UserSID!AccountId
        var userSid = WindowsIdentity.GetCurrent().User?.Value ?? "UnknownUser";
        return $"{ProviderId}!{userSid}!{accountId}";
    }

    /// <summary>
    /// Gets the icon resource string for the sync root
    /// </summary>
    private static string GetIconResource()
    {
        // Use the Azure icon from the Shell or a system icon
        // Format: path,index or path,-resourceId
        // You can use your own icon by including it in the app package
        return "%SystemRoot%\\System32\\imageres.dll,-1043"; // Cloud icon
    }

    /// <summary>
    /// Adds custom state definitions for the sync root
    /// </summary>
    private void AddCustomStates(StorageProviderSyncRootInfo info)
    {
        var customStates = info.StorageProviderItemPropertyDefinitions;

        // Synced state
        customStates.Add(new StorageProviderItemPropertyDefinition
        {
            DisplayNameResource = "Synced",
            Id = 1
        });

        // Syncing state
        customStates.Add(new StorageProviderItemPropertyDefinition
        {
            DisplayNameResource = "Syncing",
            Id = 2
        });

        // Error state
        customStates.Add(new StorageProviderItemPropertyDefinition
        {
            DisplayNameResource = "Error",
            Id = 3
        });

        // Pending upload state
        customStates.Add(new StorageProviderItemPropertyDefinition
        {
            DisplayNameResource = "Pending Upload",
            Id = 4
        });
    }
}

/// <summary>
/// Provides custom state information for files in the sync root.
/// Implement IStorageProviderItemPropertySource for COM registration.
/// </summary>
public class CustomStateProvider
{
    /// <summary>
    /// Gets the custom properties for a file
    /// </summary>
    public static IEnumerable<StorageProviderItemProperty> GetItemProperties(string itemPath)
    {
        var properties = new List<StorageProviderItemProperty>();

        try
        {
            if (!File.Exists(itemPath) && !Directory.Exists(itemPath))
                return properties;

            var attributes = File.GetAttributes(itemPath);
            
            // Check if it's a placeholder
            if ((attributes & System.IO.FileAttributes.ReparsePoint) != 0)
            {
                // Placeholder file
                if ((attributes & System.IO.FileAttributes.Offline) != 0)
                {
                    // Cloud-only (not hydrated)
                    properties.Add(new StorageProviderItemProperty
                    {
                        Id = 1,
                        Value = "Available online",
                        IconResource = "%SystemRoot%\\System32\\imageres.dll,-1043"
                    });
                }
                else
                {
                    // Hydrated
                    properties.Add(new StorageProviderItemProperty
                    {
                        Id = 1,
                        Value = "Available offline",
                        IconResource = "%SystemRoot%\\System32\\imageres.dll,-1024"
                    });
                }
            }
            else
            {
                // Regular file, synced
                properties.Add(new StorageProviderItemProperty
                {
                    Id = 1,
                    Value = "Synced",
                    IconResource = "%SystemRoot%\\System32\\imageres.dll,-1025"
                });
            }
        }
        catch
        {
            // Return empty on error
        }

        return properties;
    }
}
