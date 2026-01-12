using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using BlobStorageDriver.Common.Configuration;

namespace BlobStorageDriver.SyncEngine.CloudFilter;

/// <summary>
/// Windows Cloud Files API wrapper for creating cloud file placeholders
/// </summary>
public class CloudFilterProvider : IDisposable
{
    private readonly ILogger<CloudFilterProvider> _logger;
    private readonly CacheSettings _settings;
#pragma warning disable CS0169 // Field is reserved for future use with Cloud Filter connection
    private IntPtr _connectionKey;
#pragma warning restore CS0169
    private bool _isRegistered;
    private bool _isDisposed;

    // Cloud Files API constants
    private const int CF_PLACEHOLDER_CREATE_FLAG_NONE = 0;
    private const int CF_PLACEHOLDER_CREATE_FLAG_DISABLE_ON_DEMAND_POPULATION = 1;
    private const int CF_PIN_STATE_PINNED = 1;
    private const int CF_PIN_STATE_UNPINNED = 2;
    private const int CF_PIN_STATE_UNSPECIFIED = 0;
    private const int CF_IN_SYNC_STATE_IN_SYNC = 1;
    private const int CF_IN_SYNC_STATE_NOT_IN_SYNC = 2;

    public CloudFilterProvider(CacheSettings settings, ILogger<CloudFilterProvider> logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Registers the sync root with Windows Cloud Files API
    /// </summary>
    public async Task RegisterSyncRootAsync()
    {
        var syncRootPath = _settings.LocalSyncFolder;
        
        if (!Directory.Exists(syncRootPath))
        {
            Directory.CreateDirectory(syncRootPath);
        }

        try
        {
            // Register the sync root with Cloud Files API
            var registration = new CF_SYNC_REGISTRATION
            {
                ProviderName = "BlobStorageDriver",
                ProviderVersion = "1.0",
                SyncRootIdentity = Encoding.Unicode.GetBytes("BlobStorageDriver"),
                SyncRootIdentityLength = (uint)(Encoding.Unicode.GetByteCount("BlobStorageDriver"))
            };

            var policies = new CF_SYNC_POLICIES
            {
                Hydration = new CF_HYDRATION_POLICY
                {
                    Primary = CF_HYDRATION_POLICY_PRIMARY.CF_HYDRATION_POLICY_FULL,
                    Modifier = CF_HYDRATION_POLICY_MODIFIER.CF_HYDRATION_POLICY_MODIFIER_NONE
                },
                Population = new CF_POPULATION_POLICY
                {
                    Primary = CF_POPULATION_POLICY_PRIMARY.CF_POPULATION_POLICY_FULL,
                    Modifier = CF_POPULATION_POLICY_MODIFIER.CF_POPULATION_POLICY_MODIFIER_NONE
                },
                InSync = CF_INSYNC_POLICY.CF_INSYNC_POLICY_TRACK_ALL,
                HardLink = CF_HARDLINK_POLICY.CF_HARDLINK_POLICY_NONE,
                PlaceholderManagement = CF_PLACEHOLDER_MANAGEMENT_POLICY.CF_PLACEHOLDER_MANAGEMENT_POLICY_DEFAULT
            };

            // Note: In production, use P/Invoke to call CfRegisterSyncRoot
            // For now, we'll simulate the registration
            _isRegistered = true;
            
            _logger.LogInformation("Registered sync root at: {Path}", syncRootPath);
            
            // Apply cloud icon overlay to the folder
            await ApplyFolderCustomizationAsync(syncRootPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register sync root");
            throw;
        }
    }

    /// <summary>
    /// Unregisters the sync root
    /// </summary>
    public void UnregisterSyncRoot()
    {
        if (!_isRegistered) return;

        try
        {
            // Note: In production, call CfUnregisterSyncRoot
            _isRegistered = false;
            _logger.LogInformation("Unregistered sync root");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unregister sync root");
        }
    }

    /// <summary>
    /// Creates a placeholder file or directory
    /// </summary>
    public async Task CreatePlaceholderAsync(string relativePath, bool isDirectory, long size, DateTime modifiedTime)
    {
        var fullPath = Path.Combine(_settings.LocalSyncFolder, relativePath);
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (isDirectory)
        {
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }
            
            // Mark as cloud placeholder directory
            await SetPlaceholderStateAsync(fullPath, true, true);
        }
        else
        {
            // Create sparse file as placeholder
            await CreateSparseFilePlaceholderAsync(fullPath, size, modifiedTime);
        }

        _logger.LogDebug("Created placeholder: {Path}", relativePath);
    }

    private async Task CreateSparseFilePlaceholderAsync(string path, long size, DateTime modifiedTime)
    {
        // Create a sparse file that takes minimal disk space
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            if (size > 0)
            {
                // Set file length without allocating disk space
                fs.SetLength(size);
            }
        }

        // Set file times
        File.SetLastWriteTimeUtc(path, modifiedTime);
        
        // Mark file as offline (cloud placeholder)
        var attributes = File.GetAttributes(path);
        File.SetAttributes(path, attributes | FileAttributes.Offline | FileAttributes.SparseFile);

        await Task.CompletedTask;
    }

    /// <summary>
    /// Converts a placeholder to a hydrated (full) file
    /// </summary>
    public async Task HydrateFileAsync(string relativePath, Stream content)
    {
        var fullPath = Path.Combine(_settings.LocalSyncFolder, relativePath);

        // Write content to file
        await using (var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await content.CopyToAsync(fs);
        }

        // Remove offline attribute
        var attributes = File.GetAttributes(fullPath);
        attributes &= ~FileAttributes.Offline;
        attributes &= ~FileAttributes.SparseFile;
        File.SetAttributes(fullPath, attributes);

        _logger.LogDebug("Hydrated file: {Path}", relativePath);
    }

    /// <summary>
    /// Converts a hydrated file back to a placeholder
    /// </summary>
    public async Task DehydrateFileAsync(string relativePath)
    {
        var fullPath = Path.Combine(_settings.LocalSyncFolder, relativePath);

        if (!File.Exists(fullPath))
            return;

        var fileInfo = new FileInfo(fullPath);
        var size = fileInfo.Length;
        var modifiedTime = fileInfo.LastWriteTimeUtc;

        // Delete content but preserve metadata
        await using (var fs = new FileStream(fullPath, FileMode.Truncate, FileAccess.Write, FileShare.None))
        {
            fs.SetLength(size); // Set sparse length
        }

        // Set offline attribute
        var attributes = File.GetAttributes(fullPath);
        File.SetAttributes(fullPath, attributes | FileAttributes.Offline | FileAttributes.SparseFile);
        File.SetLastWriteTimeUtc(fullPath, modifiedTime);

        _logger.LogDebug("Dehydrated file: {Path}", relativePath);
    }

    /// <summary>
    /// Sets the pin state of a file or directory
    /// </summary>
    public async Task SetPinStateAsync(string relativePath, bool pinned)
    {
        var fullPath = Path.Combine(_settings.LocalSyncFolder, relativePath);

        if (File.Exists(fullPath))
        {
            var attributes = File.GetAttributes(fullPath);
            
            if (pinned)
            {
                // Remove sparse file attribute for pinned files
                attributes &= ~FileAttributes.SparseFile;
                attributes |= FileAttributes.Normal;
            }
            
            File.SetAttributes(fullPath, attributes);
        }

        await Task.CompletedTask;
        _logger.LogDebug("Set pin state for {Path}: {Pinned}", relativePath, pinned);
    }

    /// <summary>
    /// Sets the sync state of a file
    /// </summary>
    public async Task SetSyncStateAsync(string relativePath, bool inSync)
    {
        var fullPath = Path.Combine(_settings.LocalSyncFolder, relativePath);

        // In production, use CfSetInSyncState
        // For now, we track this in our database

        await Task.CompletedTask;
        _logger.LogDebug("Set sync state for {Path}: {InSync}", relativePath, inSync);
    }

    /// <summary>
    /// Gets the placeholder state of a file
    /// </summary>
    public PlaceholderState GetPlaceholderState(string relativePath)
    {
        var fullPath = Path.Combine(_settings.LocalSyncFolder, relativePath);

        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            return PlaceholderState.NotExists;
        }

        var attributes = File.GetAttributes(fullPath);

        if (attributes.HasFlag(FileAttributes.Offline))
        {
            return PlaceholderState.CloudOnly;
        }

        if (attributes.HasFlag(FileAttributes.SparseFile))
        {
            return PlaceholderState.Partial;
        }

        return PlaceholderState.Full;
    }

    private async Task SetPlaceholderStateAsync(string path, bool isPlaceholder, bool isDirectory)
    {
        if (isDirectory)
        {
            // Mark directory with hidden .cloud file
            var markerPath = Path.Combine(path, ".cloud");
            if (isPlaceholder && !File.Exists(markerPath))
            {
                await File.WriteAllTextAsync(markerPath, "cloud_folder");
                File.SetAttributes(markerPath, FileAttributes.Hidden | FileAttributes.System);
            }
        }
    }

    private async Task ApplyFolderCustomizationAsync(string folderPath)
    {
        try
        {
            // Create desktop.ini for custom folder icon
            var desktopIniPath = Path.Combine(folderPath, "desktop.ini");
            var iniContent = @"[.ShellClassInfo]
IconResource=C:\Windows\System32\shell32.dll,275
[ViewState]
Mode=
Vid=
FolderType=Generic
";
            // Remove existing attributes if file exists (it might be read-only/system)
            if (File.Exists(desktopIniPath))
            {
                try
                {
                    File.SetAttributes(desktopIniPath, FileAttributes.Normal);
                }
                catch
                {
                    // Ignore if we can't change attributes
                }
            }
            
            try
            {
                await File.WriteAllTextAsync(desktopIniPath, iniContent);
                File.SetAttributes(desktopIniPath, FileAttributes.Hidden | FileAttributes.System);
            }
            catch (UnauthorizedAccessException)
            {
                // File might be locked by Explorer, skip customization
                _logger.LogWarning("Could not update desktop.ini - file may be locked");
            }
            
            // Set folder as system folder to apply customization
            try
            {
                var folderAttributes = File.GetAttributes(folderPath);
                File.SetAttributes(folderPath, folderAttributes | FileAttributes.System);
            }
            catch
            {
                // Ignore if we can't set folder attributes
            }

            _logger.LogInformation("Applied folder customization to: {Path}", folderPath);
        }
        catch (Exception ex)
        {
            // Don't fail the entire registration just because of folder customization
            _logger.LogWarning(ex, "Could not apply folder customization to: {Path}", folderPath);
        }
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            UnregisterSyncRoot();
            _isDisposed = true;
        }
    }
}

/// <summary>
/// Placeholder file state
/// </summary>
public enum PlaceholderState
{
    NotExists,
    CloudOnly,
    Partial,
    Full
}

#region Cloud Files API Structures (for reference)

[StructLayout(LayoutKind.Sequential)]
internal struct CF_SYNC_REGISTRATION
{
    public string ProviderName;
    public string ProviderVersion;
    public byte[] SyncRootIdentity;
    public uint SyncRootIdentityLength;
    public IntPtr FileIdentity;
    public uint FileIdentityLength;
    public Guid ProviderId;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CF_SYNC_POLICIES
{
    public CF_HYDRATION_POLICY Hydration;
    public CF_POPULATION_POLICY Population;
    public CF_INSYNC_POLICY InSync;
    public CF_HARDLINK_POLICY HardLink;
    public CF_PLACEHOLDER_MANAGEMENT_POLICY PlaceholderManagement;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CF_HYDRATION_POLICY
{
    public CF_HYDRATION_POLICY_PRIMARY Primary;
    public CF_HYDRATION_POLICY_MODIFIER Modifier;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CF_POPULATION_POLICY
{
    public CF_POPULATION_POLICY_PRIMARY Primary;
    public CF_POPULATION_POLICY_MODIFIER Modifier;
}

internal enum CF_HYDRATION_POLICY_PRIMARY
{
    CF_HYDRATION_POLICY_PARTIAL = 0,
    CF_HYDRATION_POLICY_PROGRESSIVE = 1,
    CF_HYDRATION_POLICY_FULL = 2,
    CF_HYDRATION_POLICY_ALWAYS_FULL = 3
}

internal enum CF_HYDRATION_POLICY_MODIFIER
{
    CF_HYDRATION_POLICY_MODIFIER_NONE = 0,
    CF_HYDRATION_POLICY_MODIFIER_VALIDATION_REQUIRED = 1,
    CF_HYDRATION_POLICY_MODIFIER_STREAMING_ALLOWED = 2,
    CF_HYDRATION_POLICY_MODIFIER_AUTO_DEHYDRATION_ALLOWED = 4
}

internal enum CF_POPULATION_POLICY_PRIMARY
{
    CF_POPULATION_POLICY_PARTIAL = 0,
    CF_POPULATION_POLICY_FULL = 2,
    CF_POPULATION_POLICY_ALWAYS_FULL = 3
}

internal enum CF_POPULATION_POLICY_MODIFIER
{
    CF_POPULATION_POLICY_MODIFIER_NONE = 0
}

internal enum CF_INSYNC_POLICY
{
    CF_INSYNC_POLICY_NONE = 0,
    CF_INSYNC_POLICY_TRACK_FILE_CREATION_TIME = 1,
    CF_INSYNC_POLICY_TRACK_FILE_READONLY_ATTRIBUTE = 2,
    CF_INSYNC_POLICY_TRACK_FILE_HIDDEN_ATTRIBUTE = 4,
    CF_INSYNC_POLICY_TRACK_FILE_SYSTEM_ATTRIBUTE = 8,
    CF_INSYNC_POLICY_TRACK_DIRECTORY_CREATION_TIME = 16,
    CF_INSYNC_POLICY_TRACK_DIRECTORY_READONLY_ATTRIBUTE = 32,
    CF_INSYNC_POLICY_TRACK_DIRECTORY_HIDDEN_ATTRIBUTE = 64,
    CF_INSYNC_POLICY_TRACK_DIRECTORY_SYSTEM_ATTRIBUTE = 128,
    CF_INSYNC_POLICY_TRACK_FILE_LAST_WRITE_TIME = 256,
    CF_INSYNC_POLICY_TRACK_DIRECTORY_LAST_WRITE_TIME = 512,
    CF_INSYNC_POLICY_TRACK_ALL = 0xFFFFFF
}

internal enum CF_HARDLINK_POLICY
{
    CF_HARDLINK_POLICY_NONE = 0,
    CF_HARDLINK_POLICY_ALLOWED = 1
}

internal enum CF_PLACEHOLDER_MANAGEMENT_POLICY
{
    CF_PLACEHOLDER_MANAGEMENT_POLICY_DEFAULT = 0,
    CF_PLACEHOLDER_MANAGEMENT_POLICY_CREATE_UNRESTRICTED = 1,
    CF_PLACEHOLDER_MANAGEMENT_POLICY_CONVERT_UNRESTRICTED = 2,
    CF_PLACEHOLDER_MANAGEMENT_POLICY_UPDATE_UNRESTRICTED = 4
}

#endregion
