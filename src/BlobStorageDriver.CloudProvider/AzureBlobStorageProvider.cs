using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using BlobStorageDriver.Common.Configuration;
using BlobStorageDriver.Common.Models;
using Microsoft.Extensions.Logging;

namespace BlobStorageDriver.CloudProvider;

/// <summary>
/// Azure Blob Storage implementation of cloud storage provider
/// </summary>
public class AzureBlobStorageProvider : ICloudStorageProvider
{
    private readonly BlobContainerClient _containerClient;
    private readonly ILogger<AzureBlobStorageProvider> _logger;
    private readonly AzureBlobSettings _settings;
    
    public AzureBlobStorageProvider(AzureBlobSettings settings, ILogger<AzureBlobStorageProvider> logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        _containerClient = CreateContainerClient(settings);
    }
    
    private static BlobContainerClient CreateContainerClient(AzureBlobSettings settings)
    {
        // Validate container name is provided
        if (string.IsNullOrWhiteSpace(settings.ContainerName))
        {
            throw new InvalidOperationException("Container name is required.");
        }

        switch (settings.AuthType)
        {
            case AuthenticationType.ConnectionString:
                if (string.IsNullOrEmpty(settings.ConnectionString))
                    throw new InvalidOperationException("Connection string is required for Connection String authentication.");
                return new BlobContainerClient(settings.ConnectionString, settings.ContainerName);

            case AuthenticationType.AccountKey:
                if (string.IsNullOrEmpty(settings.AccountName) || string.IsNullOrEmpty(settings.AccountKey))
                    throw new InvalidOperationException("Account name and key are required for Account Key authentication.");
                var keyConnectionString = $"DefaultEndpointsProtocol=https;AccountName={settings.AccountName};AccountKey={settings.AccountKey};EndpointSuffix=core.windows.net";
                return new BlobContainerClient(keyConnectionString, settings.ContainerName);

            case AuthenticationType.SasToken:
                if (string.IsNullOrEmpty(settings.AccountName) || string.IsNullOrEmpty(settings.SasToken))
                    throw new InvalidOperationException("Account name and SAS token are required for SAS Token authentication.");
                var sasToken = settings.SasToken.StartsWith("?") ? settings.SasToken : "?" + settings.SasToken;
                var sasUri = new Uri($"https://{settings.AccountName}.blob.core.windows.net/{settings.ContainerName}{sasToken}");
                return new BlobContainerClient(sasUri);

            case AuthenticationType.EntraIdInteractive:
                if (string.IsNullOrEmpty(settings.AccountName))
                    throw new InvalidOperationException("Account name is required for Entra ID authentication.");
                var interactiveUri = new Uri($"https://{settings.AccountName}.blob.core.windows.net/{settings.ContainerName}");
                // Use the MSAL cached token from the embedded sign-in window
                var msalCredential = new MsalCachedTokenCredential(settings.TenantId);
                return new BlobContainerClient(interactiveUri, msalCredential);

            case AuthenticationType.EntraIdDefault:
                if (string.IsNullOrEmpty(settings.AccountName))
                    throw new InvalidOperationException("Account name is required for Entra ID authentication.");
                var defaultUri = new Uri($"https://{settings.AccountName}.blob.core.windows.net/{settings.ContainerName}");
                var defaultOptions = new DefaultAzureCredentialOptions
                {
                    TenantId = string.IsNullOrEmpty(settings.TenantId) ? null : settings.TenantId,
                    ManagedIdentityClientId = string.IsNullOrEmpty(settings.ClientId) ? null : settings.ClientId
                };
                return new BlobContainerClient(defaultUri, new DefaultAzureCredential(defaultOptions));

            case AuthenticationType.ManagedIdentity:
                if (string.IsNullOrEmpty(settings.AccountName))
                    throw new InvalidOperationException("Account name is required for Managed Identity authentication.");
                var managedUri = new Uri($"https://{settings.AccountName}.blob.core.windows.net/{settings.ContainerName}");
                var managedCredential = string.IsNullOrEmpty(settings.ClientId) 
                    ? new ManagedIdentityCredential()
                    : new ManagedIdentityCredential(settings.ClientId);
                return new BlobContainerClient(managedUri, managedCredential);

            default:
                throw new InvalidOperationException($"Unsupported authentication type: {settings.AuthType}");
        }
    }
    
    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _containerClient.ExistsAsync(cancellationToken);
            _logger.LogInformation("Successfully connected to Azure Blob Storage container: {Container}", _settings.ContainerName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Azure Blob Storage container: {Container}", _settings.ContainerName);
            return false;
        }
    }
    
    public async Task<IEnumerable<CloudFileItem>> ListItemsAsync(string path = "", CancellationToken cancellationToken = default)
    {
        var items = new List<CloudFileItem>();
        var prefix = string.IsNullOrEmpty(path) ? "" : path.TrimEnd('/') + "/";
        
        try
        {
            await foreach (var item in _containerClient.GetBlobsByHierarchyAsync(
                prefix: prefix,
                delimiter: "/",
                cancellationToken: cancellationToken))
            {
                if (item.IsPrefix)
                {
                    // Directory
                    var dirName = item.Prefix.TrimEnd('/');
                    if (dirName.Contains('/'))
                        dirName = dirName.Substring(dirName.LastIndexOf('/') + 1);
                        
                    items.Add(new CloudFileItem
                    {
                        Id = item.Prefix,
                        Name = dirName,
                        RelativePath = item.Prefix.TrimEnd('/'),
                        IsDirectory = true,
                        State = FileState.CloudOnly
                    });
                }
                else if (item.Blob != null)
                {
                    // File
                    var blob = item.Blob;
                    var fileName = blob.Name;
                    if (fileName.Contains('/'))
                        fileName = fileName.Substring(fileName.LastIndexOf('/') + 1);
                        
                    items.Add(new CloudFileItem
                    {
                        Id = blob.Name,
                        Name = fileName,
                        RelativePath = blob.Name,
                        IsDirectory = false,
                        Size = blob.Properties.ContentLength ?? 0,
                        CreatedAt = blob.Properties.CreatedOn?.UtcDateTime ?? DateTime.UtcNow,
                        ModifiedAt = blob.Properties.LastModified?.UtcDateTime ?? DateTime.UtcNow,
                        ETag = blob.Properties.ETag?.ToString(),
                        ContentHash = blob.Properties.ContentHash != null 
                            ? Convert.ToBase64String(blob.Properties.ContentHash) 
                            : null,
                        State = FileState.CloudOnly
                    });
                }
            }
            
            _logger.LogDebug("Listed {Count} items in path: {Path}", items.Count, path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list items in path: {Path}", path);
            throw;
        }
        
        return items;
    }
    
    public async Task<CloudFileItem?> GetItemAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            var blobClient = _containerClient.GetBlobClient(path);
            var exists = await blobClient.ExistsAsync(cancellationToken);
            
            if (!exists)
            {
                // Check if it's a directory (has blobs with this prefix)
                await foreach (var _ in _containerClient.GetBlobsAsync(prefix: path.TrimEnd('/') + "/", cancellationToken: cancellationToken))
                {
                    var dirName = path.TrimEnd('/');
                    if (dirName.Contains('/'))
                        dirName = dirName.Substring(dirName.LastIndexOf('/') + 1);
                        
                    return new CloudFileItem
                    {
                        Id = path,
                        Name = dirName,
                        RelativePath = path.TrimEnd('/'),
                        IsDirectory = true,
                        State = FileState.CloudOnly
                    };
                }
                return null;
            }
            
            var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
            var fileName = path;
            if (fileName.Contains('/'))
                fileName = fileName.Substring(fileName.LastIndexOf('/') + 1);
                
            return new CloudFileItem
            {
                Id = path,
                Name = fileName,
                RelativePath = path,
                IsDirectory = false,
                Size = properties.Value.ContentLength,
                CreatedAt = properties.Value.CreatedOn.UtcDateTime,
                ModifiedAt = properties.Value.LastModified.UtcDateTime,
                ETag = properties.Value.ETag.ToString(),
                ContentHash = properties.Value.ContentHash != null 
                    ? Convert.ToBase64String(properties.Value.ContentHash) 
                    : null,
                State = FileState.CloudOnly,
                Metadata = properties.Value.Metadata.ToDictionary(k => k.Key, v => v.Value)
            };
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get item: {Path}", path);
            throw;
        }
    }
    
    public async Task DownloadFileAsync(string remotePath, string localPath, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var blobClient = _containerClient.GetBlobClient(remotePath);
            var directory = Path.GetDirectoryName(localPath);
            
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            var options = new BlobDownloadToOptions
            {
                ProgressHandler = progress != null 
                    ? new Progress<long>(progress.Report) 
                    : null
            };
            
            await blobClient.DownloadToAsync(localPath, options, cancellationToken);
            
            _logger.LogInformation("Downloaded file: {RemotePath} -> {LocalPath}", remotePath, localPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download file: {RemotePath}", remotePath);
            throw;
        }
    }
    
    public async Task<Stream> DownloadToStreamAsync(string remotePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var blobClient = _containerClient.GetBlobClient(remotePath);
            var response = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
            return response.Value.Content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download file to stream: {RemotePath}", remotePath);
            throw;
        }
    }
    
    public async Task<CloudFileItem> UploadFileAsync(string localPath, string remotePath, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var blobClient = _containerClient.GetBlobClient(remotePath);
            
            var options = new BlobUploadOptions
            {
                ProgressHandler = progress != null 
                    ? new Progress<long>(progress.Report) 
                    : null,
                TransferOptions = new Azure.Storage.StorageTransferOptions
                {
                    MaximumConcurrency = 4,
                    InitialTransferSize = 4 * 1024 * 1024,
                    MaximumTransferSize = 4 * 1024 * 1024
                }
            };
            
            await blobClient.UploadAsync(localPath, options, cancellationToken);
            
            _logger.LogInformation("Uploaded file: {LocalPath} -> {RemotePath}", localPath, remotePath);
            
            return await GetItemAsync(remotePath, cancellationToken) 
                ?? throw new InvalidOperationException("Failed to get uploaded file metadata");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload file: {LocalPath}", localPath);
            throw;
        }
    }
    
    public async Task<CloudFileItem> UploadFromStreamAsync(Stream content, string remotePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var blobClient = _containerClient.GetBlobClient(remotePath);
            await blobClient.UploadAsync(content, overwrite: true, cancellationToken);
            
            _logger.LogInformation("Uploaded stream to: {RemotePath}", remotePath);
            
            return await GetItemAsync(remotePath, cancellationToken) 
                ?? throw new InvalidOperationException("Failed to get uploaded file metadata");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload stream to: {RemotePath}", remotePath);
            throw;
        }
    }
    
    public async Task<CloudFileItem> CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        // Azure Blob Storage doesn't have real directories, but we can create a marker blob
        var markerPath = path.TrimEnd('/') + "/.folder";
        var blobClient = _containerClient.GetBlobClient(markerPath);
        
        using var emptyStream = new MemoryStream();
        await blobClient.UploadAsync(emptyStream, overwrite: true, cancellationToken);
        
        var dirName = path.TrimEnd('/');
        if (dirName.Contains('/'))
            dirName = dirName.Substring(dirName.LastIndexOf('/') + 1);
            
        _logger.LogInformation("Created directory: {Path}", path);
        
        return new CloudFileItem
        {
            Id = path,
            Name = dirName,
            RelativePath = path.TrimEnd('/'),
            IsDirectory = true,
            CreatedAt = DateTime.UtcNow,
            ModifiedAt = DateTime.UtcNow,
            State = FileState.CloudOnly
        };
    }
    
    public async Task DeleteItemAsync(string path, bool recursive = false, CancellationToken cancellationToken = default)
    {
        try
        {
            var blobClient = _containerClient.GetBlobClient(path);
            var exists = await blobClient.ExistsAsync(cancellationToken);
            
            if (exists)
            {
                await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
                _logger.LogInformation("Deleted file: {Path}", path);
            }
            else if (recursive)
            {
                // Delete all blobs with this prefix (directory deletion)
                var prefix = path.TrimEnd('/') + "/";
                await foreach (var blob in _containerClient.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken))
                {
                    await _containerClient.DeleteBlobIfExistsAsync(blob.Name, cancellationToken: cancellationToken);
                }
                _logger.LogInformation("Deleted directory recursively: {Path}", path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete item: {Path}", path);
            throw;
        }
    }
    
    public async Task<CloudFileItem> MoveItemAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        var result = await CopyItemAsync(sourcePath, destinationPath, cancellationToken);
        await DeleteItemAsync(sourcePath, recursive: true, cancellationToken);
        
        _logger.LogInformation("Moved item: {Source} -> {Destination}", sourcePath, destinationPath);
        return result;
    }
    
    public async Task<CloudFileItem> CopyItemAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        try
        {
            var sourceBlob = _containerClient.GetBlobClient(sourcePath);
            var destBlob = _containerClient.GetBlobClient(destinationPath);
            
            var copyOperation = await destBlob.StartCopyFromUriAsync(sourceBlob.Uri, cancellationToken: cancellationToken);
            await copyOperation.WaitForCompletionAsync(cancellationToken);
            
            _logger.LogInformation("Copied item: {Source} -> {Destination}", sourcePath, destinationPath);
            
            return await GetItemAsync(destinationPath, cancellationToken) 
                ?? throw new InvalidOperationException("Failed to get copied file metadata");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy item: {Source} -> {Destination}", sourcePath, destinationPath);
            throw;
        }
    }
    
    public async Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        var blobClient = _containerClient.GetBlobClient(path);
        var exists = await blobClient.ExistsAsync(cancellationToken);
        
        if (exists)
            return true;
            
        // Check if it's a directory
        await foreach (var _ in _containerClient.GetBlobsAsync(prefix: path.TrimEnd('/') + "/", cancellationToken: cancellationToken))
        {
            return true;
        }
        
        return false;
    }
    
    public async Task<(IEnumerable<CloudFileItem> Items, string? ContinuationToken)> GetChangesAsync(
        string? continuationToken = null, 
        DateTime? since = null,
        CancellationToken cancellationToken = default)
    {
        var items = new List<CloudFileItem>();
        string? nextToken = null;
        
        try
        {
            var pageable = _containerClient.GetBlobsAsync(cancellationToken: cancellationToken);
            
            await foreach (var blob in pageable)
            {
                // Filter by modification time if specified
                if (since.HasValue && blob.Properties.LastModified?.UtcDateTime < since.Value)
                    continue;
                    
                var fileName = blob.Name;
                if (fileName.Contains('/'))
                    fileName = fileName.Substring(fileName.LastIndexOf('/') + 1);
                    
                // Skip directory markers
                if (fileName == ".folder")
                    continue;
                    
                items.Add(new CloudFileItem
                {
                    Id = blob.Name,
                    Name = fileName,
                    RelativePath = blob.Name,
                    IsDirectory = false,
                    Size = blob.Properties.ContentLength ?? 0,
                    CreatedAt = blob.Properties.CreatedOn?.UtcDateTime ?? DateTime.UtcNow,
                    ModifiedAt = blob.Properties.LastModified?.UtcDateTime ?? DateTime.UtcNow,
                    ETag = blob.Properties.ETag?.ToString(),
                    ContentHash = blob.Properties.ContentHash != null 
                        ? Convert.ToBase64String(blob.Properties.ContentHash) 
                        : null,
                    State = FileState.CloudOnly
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get changes");
            throw;
        }
        
        return (items, nextToken);
    }
}
