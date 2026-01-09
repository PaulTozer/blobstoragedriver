using BlobStorageDriver.Common.Configuration;
using BlobStorageDriver.Common.Models;
using BlobStorageDriver.SyncEngine;
using BlobStorageDriver.SyncEngine.Cache;
using Microsoft.Extensions.Logging;

namespace BlobStorageDriver.SyncEngine.Shell;

/// <summary>
/// Provides shell integration features like context menu operations
/// </summary>
public class ShellIntegration
{
    private readonly FileSyncEngine _syncEngine;
    private readonly LocalCacheManager _cacheManager;
    private readonly AzureBlobSettings _blobSettings;
    private readonly ILogger<ShellIntegration> _logger;

    public ShellIntegration(
        FileSyncEngine syncEngine,
        LocalCacheManager cacheManager,
        AzureBlobSettings blobSettings,
        ILogger<ShellIntegration> logger)
    {
        _syncEngine = syncEngine;
        _cacheManager = cacheManager;
        _blobSettings = blobSettings;
        _logger = logger;
    }

    /// <summary>
    /// Gets available context menu actions for a file
    /// </summary>
    public IEnumerable<ShellAction> GetAvailableActions(string localPath)
    {
        var actions = new List<ShellAction>();
        
        try
        {
            var relativePath = _cacheManager.GetRelativePath(localPath);
            var entry = _cacheManager.GetEntryAsync(relativePath).GetAwaiter().GetResult();
            
            if (entry == null)
            {
                return actions;
            }

            // Always available
            actions.Add(new ShellAction
            {
                Id = "sync",
                Name = "Sync Now",
                Icon = "sync.ico"
            });

            // Pin/Unpin
            if (entry.IsPinned)
            {
                actions.Add(new ShellAction
                {
                    Id = "unpin",
                    Name = "Free up space",
                    Icon = "unpin.ico"
                });
            }
            else
            {
                actions.Add(new ShellAction
                {
                    Id = "pin",
                    Name = "Always keep on this device",
                    Icon = "pin.ico"
                });
            }

            // Hydrate if cloud-only
            if (entry.State == FileState.CloudOnly)
            {
                actions.Add(new ShellAction
                {
                    Id = "download",
                    Name = "Make available offline",
                    Icon = "download.ico"
                });
            }

            // Share link
            actions.Add(new ShellAction
            {
                Id = "share",
                Name = "Share",
                Icon = "share.ico"
            });

            // View in Azure Portal
            actions.Add(new ShellAction
            {
                Id = "viewInPortal",
                Name = "View in Azure Portal",
                Icon = "azure.ico"
            });

            // Version history
            actions.Add(new ShellAction
            {
                Id = "history",
                Name = "Version history",
                Icon = "history.ico"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get shell actions for: {Path}", localPath);
        }

        return actions;
    }

    /// <summary>
    /// Executes a shell action
    /// </summary>
    public async Task ExecuteActionAsync(string localPath, string actionId)
    {
        var relativePath = _cacheManager.GetRelativePath(localPath);

        switch (actionId)
        {
            case "sync":
                await _syncEngine.PerformSyncAsync();
                break;

            case "pin":
                await _syncEngine.PinFileAsync(relativePath, true);
                break;

            case "unpin":
                await _syncEngine.PinFileAsync(relativePath, false);
                await _syncEngine.DehydrateFileAsync(relativePath);
                break;

            case "download":
                await _syncEngine.HydrateFileAsync(relativePath);
                break;

            case "share":
                GenerateShareLink(relativePath);
                break;

            case "viewInPortal":
                OpenInAzurePortal(relativePath);
                break;

            case "history":
                // Show version history window
                break;

            default:
                _logger.LogWarning("Unknown shell action: {ActionId}", actionId);
                break;
        }
    }

    private void GenerateShareLink(string relativePath)
    {
        // Generate a SAS URL for sharing
        // This would require access to the storage account settings
        _logger.LogInformation("Generating share link for: {Path}", relativePath);
    }

    private void OpenInAzurePortal(string relativePath)
    {
        // Open Azure Portal to the blob
        if (_blobSettings != null && !string.IsNullOrEmpty(_blobSettings.AccountName))
        {
            var url = $"https://portal.azure.com/#view/Microsoft_Azure_Storage/BlobPropertiesBladeV2/storageAccountId/%2Fsubscriptions%2F...%2FresourceGroups%2F...%2Fproviders%2FMicrosoft.Storage%2FstorageAccounts%2F{_blobSettings.AccountName}/path/{Uri.EscapeDataString(relativePath)}";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        else
        {
            _logger.LogWarning("Cannot open Azure Portal - blob settings not configured");
        }
    }

    /// <summary>
    /// Registers shell extensions
    /// </summary>
    public void RegisterShellExtensions()
    {
        // Register context menu handler with Windows Shell
        // This requires COM registration and elevated permissions
        _logger.LogInformation("Shell extensions registration requested");
    }

    /// <summary>
    /// Unregisters shell extensions
    /// </summary>
    public void UnregisterShellExtensions()
    {
        _logger.LogInformation("Shell extensions unregistration requested");
    }
}

/// <summary>
/// Represents a shell context menu action
/// </summary>
public class ShellAction
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
}
