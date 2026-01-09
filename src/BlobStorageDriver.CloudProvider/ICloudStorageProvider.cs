using BlobStorageDriver.Common.Models;

namespace BlobStorageDriver.CloudProvider;

/// <summary>
/// Interface for cloud storage operations
/// </summary>
public interface ICloudStorageProvider
{
    /// <summary>
    /// Tests the connection to the cloud storage
    /// </summary>
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Lists all items in the specified path
    /// </summary>
    Task<IEnumerable<CloudFileItem>> ListItemsAsync(string path = "", CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets metadata for a specific item
    /// </summary>
    Task<CloudFileItem?> GetItemAsync(string path, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Downloads a file to the specified local path
    /// </summary>
    Task DownloadFileAsync(string remotePath, string localPath, IProgress<long>? progress = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Downloads a file to a stream
    /// </summary>
    Task<Stream> DownloadToStreamAsync(string remotePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Uploads a file from the specified local path
    /// </summary>
    Task<CloudFileItem> UploadFileAsync(string localPath, string remotePath, IProgress<long>? progress = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Uploads content from a stream
    /// </summary>
    Task<CloudFileItem> UploadFromStreamAsync(Stream content, string remotePath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Creates a directory
    /// </summary>
    Task<CloudFileItem> CreateDirectoryAsync(string path, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Deletes a file or directory
    /// </summary>
    Task DeleteItemAsync(string path, bool recursive = false, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Moves or renames an item
    /// </summary>
    Task<CloudFileItem> MoveItemAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Copies an item
    /// </summary>
    Task<CloudFileItem> CopyItemAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Checks if an item exists
    /// </summary>
    Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets changes since a specific time or continuation token
    /// </summary>
    Task<(IEnumerable<CloudFileItem> Items, string? ContinuationToken)> GetChangesAsync(
        string? continuationToken = null, 
        DateTime? since = null,
        CancellationToken cancellationToken = default);
}
