# Blob Storage Driver

A Windows application that mounts Azure Blob Storage as a local file system with OneDrive/ShareFile-like functionality. Features include smart caching, offline support, and conflict resolution.

## Features

- **Virtual File System**: Mount Azure Blob containers as local folders with placeholder files (similar to OneDrive)
- **Smart Caching**: LRU-based local cache with configurable size limits
- **Offline Support**: Work with files offline; changes sync when connection is restored
- **Real-time Sync**: File system watcher for immediate upload of local changes
- **Conflict Resolution**: Detect and resolve sync conflicts with intuitive UI
- **System Tray Integration**: Monitor sync progress and manage settings from the system tray
- **Windows Service**: Background sync service for enterprise deployments

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                        Tray Application                          │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐              │
│  │  Status UI  │  │ Conflict UI │  │ Settings UI │              │
│  └─────────────┘  └─────────────┘  └─────────────┘              │
└─────────────────────────────────────────────────────────────────┘
                              │
┌─────────────────────────────────────────────────────────────────┐
│                        Sync Engine                               │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐              │
│  │  File Sync  │  │   Conflict  │  │   Change    │              │
│  │   Manager   │  │   Resolver  │  │   Tracker   │              │
│  └─────────────┘  └─────────────┘  └─────────────┘              │
└─────────────────────────────────────────────────────────────────┘
                              │
┌─────────────────────────────────────────────────────────────────┐
│                      Local Cache Manager                         │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐              │
│  │   LiteDB    │  │    File     │  │  Placeholder│              │
│  │  Metadata   │  │    Cache    │  │   Manager   │              │
│  └─────────────┘  └─────────────┘  └─────────────┘              │
└─────────────────────────────────────────────────────────────────┘
                              │
┌─────────────────────────────────────────────────────────────────┐
│                     Cloud Provider                               │
│  ┌──────────────────────────────────────────────────┐          │
│  │           Azure Blob Storage Client               │          │
│  └──────────────────────────────────────────────────┘          │
└─────────────────────────────────────────────────────────────────┘
```

## Prerequisites

- Windows 10 version 1709 (Fall Creators Update) or later
- .NET 8.0 Runtime
- Azure Blob Storage account with a container

## Installation

### From Release

1. Download the latest release from the Releases page
2. Run the installer
3. Configure your Azure Blob Storage connection in Settings

### Building from Source

```powershell
# Clone the repository
git clone https://github.com/yourusername/blobstoragedriver.git
cd blobstoragedriver

# Build the solution
dotnet build -c Release

# Run the tray application
dotnet run --project src/BlobStorageDriver.TrayApp
```

## Configuration

### Azure Blob Storage Connection

You can configure the connection using one of these methods:

1. **Connection String** (recommended for development):
   ```
   DefaultEndpointsProtocol=https;AccountName=your_account;AccountKey=your_key;EndpointSuffix=core.windows.net
   ```

2. **Account Name + Account Key**

3. **Account Name + SAS Token** (for limited access)

4. **Managed Identity** (recommended for production/Azure VMs)

### Configuration File

Settings are stored in:
```
%LOCALAPPDATA%\BlobStorageDriver\config.json
```

Example configuration:
```json
{
  "AzureBlob": {
    "AccountName": "mystorageaccount",
    "ContainerName": "mycontainer",
    "UseManagedIdentity": true
  },
  "Cache": {
    "LocalSyncFolder": "C:\\Users\\Username\\BlobStorage",
    "MaxCacheSizeBytes": 10737418240,
    "KeepAccessedWithinDays": 30
  },
  "Sync": {
    "SyncIntervalSeconds": 300,
    "EnableRealTimeSync": true,
    "MaxConcurrentUploads": 4,
    "MaxConcurrentDownloads": 4
  }
}
```

## Usage

### Basic Usage

1. Start the Blob Storage Driver application
2. The application minimizes to the system tray
3. Your Azure Blob Storage container is mounted at the configured local folder
4. Files appear as placeholders (cloud icons) until accessed
5. Access a file to download it automatically
6. Modified files are uploaded automatically

### System Tray Menu

- **Open Blob Storage Folder**: Opens the local sync folder in Explorer
- **Sync Status**: View current sync progress and statistics
- **Conflicts**: View and resolve file conflicts
- **Sync Now**: Manually trigger a full sync
- **Pause Sync**: Temporarily pause synchronization
- **Settings**: Configure application settings
- **Exit**: Close the application

### Conflict Resolution

When a file is modified both locally and in the cloud, a conflict is detected:

1. **Keep Local**: Upload your local version to the cloud
2. **Keep Cloud**: Download the cloud version, replacing local
3. **Keep Both**: Rename your local file and download cloud version

### File Status Icons

| Icon | Status |
|------|--------|
| ☁️ | Cloud-only (placeholder) |
| ✓ | Synced |
| ↑ | Uploading |
| ↓ | Downloading |
| ⚠️ | Conflict |
| ❌ | Error |

## Windows Service

For enterprise deployments, install as a Windows service:

```powershell
# Install service
sc create BlobStorageDriver binPath="C:\Program Files\BlobStorageDriver\BlobStorageDriver.Service.exe"

# Start service
sc start BlobStorageDriver

# Stop service
sc stop BlobStorageDriver

# Remove service
sc delete BlobStorageDriver
```

## Troubleshooting

### Logs

Logs are stored in:
- Tray App: `%LOCALAPPDATA%\BlobStorageDriver\logs\`
- Service: `%PROGRAMDATA%\BlobStorageDriver\logs\`

### Common Issues

1. **Files not syncing**: Check internet connection and Azure credentials
2. **Placeholder files not working**: Ensure Windows 10 1709 or later
3. **Permission errors**: Run as administrator or check folder permissions
4. **High CPU usage**: Reduce sync frequency in settings

## Security

- Credentials are stored encrypted in the local configuration
- SAS tokens with limited permissions are recommended for shared deployments
- Managed Identity is recommended for Azure-hosted environments
- All communication with Azure uses HTTPS

## Development

### Project Structure

```
src/
├── BlobStorageDriver.Common/        # Shared models and configuration
├── BlobStorageDriver.CloudProvider/ # Azure Blob Storage client
├── BlobStorageDriver.SyncEngine/    # Sync logic and cache management
├── BlobStorageDriver.TrayApp/       # WPF system tray application
└── BlobStorageDriver.Service/       # Windows service
```

### Building

```powershell
dotnet build
```

### Testing

```powershell
dotnet test
```

### Publishing

```powershell
dotnet publish -c Release -r win-x64 --self-contained
```

## License

MIT License - see LICENSE file for details.

## Contributing

1. Fork the repository
2. Create a feature branch
3. Commit your changes
4. Push to the branch
5. Create a Pull Request
