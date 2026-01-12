# Cloud Filter Implementation

This folder contains a native Windows implementation for cloud file synchronization that **does not require Dokan** or any third-party file system driver.

## Overview

The Cloud Filter API (cfapi.dll) is the native Windows solution for cloud sync providers, introduced in Windows 10 version 1709. This is the same technology used by:
- **Microsoft OneDrive**
- **Dropbox**
- **Google Drive**
- **iCloud for Windows**

## Key Differences from Dokan

| Feature | Dokan | Cloud Filter (Native) |
|---------|-------|----------------------|
| Driver Required | Yes (Dokan driver must be installed) | No (built into Windows) |
| Drive Letter | Creates a new virtual drive letter | Uses sync root folder (with optional drive letter mapping) |
| File System | Full virtual file system | Placeholder files with on-demand hydration |
| Shell Integration | Manual implementation | Automatic (File Explorer, context menus, icons) |
| Performance | Good, but adds overhead | Excellent (kernel-level integration) |
| Stability | Depends on driver quality | Very stable (Microsoft-supported) |
| Progress UI | Must implement manually | Built-in progress indicators |
| System Requirements | Any Windows version | Windows 10 v1709+ |

## Drive Letter Support

The Cloud Filter implementation now supports **optional drive letter mapping** for legacy application compatibility. This uses the Windows `DefineDosDevice` API (same as the `subst` command) to map a folder to a drive letter - no Dokan required!

```csharp
// Without drive letter (default - appears in File Explorer navigation pane)
await cloudFilterManager.ActivateAsync();

// With drive letter (e.g., Z:\)
await cloudFilterManager.ActivateAsync('Z');

// Auto-select first available drive letter
await cloudFilterManager.ActivateAsync(DriveLetterMapper.GetFirstAvailableDriveLetter());

// Map drive letter after activation
cloudFilterManager.MapDriveLetter('X');

// Unmap drive letter
cloudFilterManager.UnmapDriveLetter();
```

**Note:** Drive letter mapping creates a symbolic link from the drive letter to the sync root folder. Files still use Cloud Filter placeholders and on-demand hydration - the drive letter is just an alias to the folder.

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                        Windows Shell                             │
│  (File Explorer, Desktop, Context Menus, Progress Indicators)   │
└────────────────────────────────┬────────────────────────────────┘
                                 │
┌────────────────────────────────┼────────────────────────────────┐
│                    Cloud Filter Driver (cldflt.sys)              │
│              (Built-in Windows minifilter driver)                │
└────────────────────────────────┬────────────────────────────────┘
                                 │
┌────────────────────────────────┼────────────────────────────────┐
│                    Cloud Filter API (cldapi.dll)                 │
│                 (User-mode API for sync providers)               │
└────────────────────────────────┬────────────────────────────────┘
                                 │
┌────────────────────────────────┼────────────────────────────────┐
│                BlobStorageDriver Sync Provider                   │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐  │
│  │ SyncRootRegistrar│  │PlaceholderManager│  │CloudFilterSync │  │
│  │ (Shell registration)│(File placeholders)│  │Provider (callbacks)│
│  └─────────────────┘  └─────────────────┘  └─────────────────┘  │
└────────────────────────────────┬────────────────────────────────┘
                                 │
┌────────────────────────────────┼────────────────────────────────┐
│                    Azure Blob Storage                            │
└─────────────────────────────────────────────────────────────────┘
```

## Components

### 1. CloudFilterNative.cs
P/Invoke definitions for the Windows Cloud Filter API (cldapi.dll). Includes:
- All enumerations (CF_CALLBACK_TYPE, CF_PLACEHOLDER_STATE, etc.)
- All structures (CF_SYNC_REGISTRATION, CF_CALLBACK_INFO, etc.)
- All API functions (CfRegisterSyncRoot, CfConnectSyncRoot, etc.)

### 2. SyncRootRegistrar.cs
Registers and manages sync roots with the Windows Shell using WinRT APIs:
- Creates the sync root folder
- Registers with StorageProviderSyncRootManager
- Configures hydration/population policies
- Adds custom state definitions for File Explorer icons

### 3. PlaceholderManager.cs
Manages placeholder files in the sync root:
- Creates placeholders from cloud file listings
- Updates placeholder metadata
- Converts files to/from placeholders
- Manages hydration/dehydration
- Sets pin state (always available offline)

### 4. CloudFilterSyncProvider.cs
The main sync provider that handles Cloud Filter callbacks:
- FETCH_DATA: Downloads file content on demand
- FETCH_PLACEHOLDERS: Populates directory listings
- NOTIFY_DELETE/RENAME/DEHYDRATE: Handles file operations
- Progress reporting to Windows Shell

### 5. CloudFilterDriveManager.cs
High-level manager (replacement for VirtualDriveManager):
- Activation/deactivation of sync root
- Optional drive letter mapping
- Initial placeholder population
- Hydration/dehydration APIs
- Pin/unpin functionality

### 6. DriveLetterMapper.cs
Maps the sync root folder to a drive letter using Windows APIs:
- Uses `DefineDosDevice` API (same as `subst` command)
- No third-party drivers required
- Auto-selects available drive letters (Z: to D:)
- Clean unmapping on deactivation/dispose

## How Placeholder Files Work

1. **Placeholder Creation**: When the sync root is activated, we create placeholder files that:
   - Consume only ~1KB of disk space each
   - Show the correct file size in File Explorer
   - Display cloud status icons
   - Look and behave like normal files

2. **On-Demand Hydration**: When an app opens a placeholder file:
   - Windows Cloud Filter intercepts the read
   - Calls our FETCH_DATA callback
   - We download content from Azure Blob Storage
   - Windows streams it to the requesting app
   - Progress indicators appear in File Explorer

3. **Dehydration**: To free space:
   - User can right-click and "Free up space"
   - Or system can auto-dehydrate based on policy
   - File reverts to placeholder state
   - Only the file metadata remains locally

## Usage

```csharp
// Create the manager
var cloudFilterManager = new CloudFilterDriveManager(
    cloudProvider,
    cacheManager,
    config,
    logger,
    registrarLogger,
    placeholderLogger,
    providerLogger);

// Check availability
if (!CloudFilterDriveManager.IsCloudFilterAvailable())
{
    Console.WriteLine("Cloud Filter requires Windows 10 v1709 or later");
    return;
}

// Activate the sync root
await cloudFilterManager.ActivateAsync();

// The sync root now appears in File Explorer
// Files are placeholders that download on-demand

// Force download a specific file
await cloudFilterManager.HydrateAsync("path/to/file.txt");

// Free up space (dehydrate)
await cloudFilterManager.DehydrateAsync("path/to/file.txt");

// Pin a file to always be available offline
await cloudFilterManager.PinAsync("important/document.pdf");

// Deactivate when done
await cloudFilterManager.DeactivateAsync();
```

## Comparison: Drive Letter vs Sync Root

### Drive Letter (Dokan approach)
```
Z:\
├── folder1\
│   └── file1.txt
└── folder2\
    └── file2.txt
```
- Requires Dokan driver installation
- Appears as a separate drive
- Full virtual file system
- All file operations go through Dokan

### Sync Root (Cloud Filter approach)
```
C:\Users\Username\AzureBlobStorage\  <-- Sync Root
├── folder1\
│   └── file1.txt (placeholder)
└── folder2\
    └── file2.txt (placeholder)
```
- No driver installation required
- Appears as a branded folder in File Explorer navigation pane
- Files are placeholders (cloud icons)
- Only accessed files are downloaded
- Same UX as OneDrive

## System Requirements

- **Windows 10 version 1709 (Fall Creators Update)** or later
- Windows 11 (all versions)
- NTFS file system (cldflt.sys only supports NTFS)

## Benefits

1. **No Driver Installation**: Works out of the box on Windows 10/11
2. **Native Integration**: Uses the same APIs as OneDrive
3. **Better Performance**: Kernel-level file system integration
4. **Space Savings**: Placeholder files use minimal disk space
5. **Automatic UI**: Progress bars, icons, and context menus for free
6. **Stability**: Microsoft-supported and maintained
7. **Seamless Experience**: Files appear normal to applications

## Limitations

1. **No Drive Letter**: Files appear in a folder, not a separate drive
2. **Windows 10+ Only**: Won't work on older Windows versions
3. **NTFS Required**: Doesn't work on FAT32, exFAT, or ReFS
4. **Requires Registration**: Sync root must be registered with Shell

## Migration from Dokan

To switch from Dokan to Cloud Filter:

1. Replace `VirtualDriveManager` with `CloudFilterDriveManager`
2. Remove DokanNet package reference
3. Update configuration (drive letter → sync folder path)
4. Users will see a sync folder instead of a drive letter

The APIs are similar, so existing code should require minimal changes.
