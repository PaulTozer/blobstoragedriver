using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BlobStorageDriver.SyncEngine.CloudFilter;

/// <summary>
/// P/Invoke definitions for Windows Cloud Filter API (cfapi.dll)
/// This is the native Windows API used by OneDrive and other cloud sync providers
/// </summary>
public static class CloudFilterNative
{
    private const string CfApiDll = "cldapi.dll";

    #region Enums

    [Flags]
    public enum CF_REGISTER_FLAGS : uint
    {
        CF_REGISTER_FLAG_NONE = 0x00000000,
        CF_REGISTER_FLAG_UPDATE = 0x00000001,
        CF_REGISTER_FLAG_DISABLE_ON_DEMAND_POPULATION_ON_ROOT = 0x00000002,
        CF_REGISTER_FLAG_MARK_IN_SYNC_ON_ROOT = 0x00000004
    }

    [Flags]
    public enum CF_CONNECT_FLAGS : uint
    {
        CF_CONNECT_FLAG_NONE = 0x00000000,
        CF_CONNECT_FLAG_REQUIRE_PROCESS_INFO = 0x00000002,
        CF_CONNECT_FLAG_REQUIRE_FULL_FILE_PATH = 0x00000004,
        CF_CONNECT_FLAG_BLOCK_SELF_IMPLICIT_HYDRATION = 0x00000008
    }

    public enum CF_HYDRATION_POLICY_PRIMARY : ushort
    {
        CF_HYDRATION_POLICY_PARTIAL = 0,
        CF_HYDRATION_POLICY_PROGRESSIVE = 1,
        CF_HYDRATION_POLICY_FULL = 2,
        CF_HYDRATION_POLICY_ALWAYS_FULL = 3
    }

    [Flags]
    public enum CF_HYDRATION_POLICY_MODIFIER : ushort
    {
        CF_HYDRATION_POLICY_MODIFIER_NONE = 0x0000,
        CF_HYDRATION_POLICY_MODIFIER_VALIDATION_REQUIRED = 0x0001,
        CF_HYDRATION_POLICY_MODIFIER_STREAMING_ALLOWED = 0x0002,
        CF_HYDRATION_POLICY_MODIFIER_AUTO_DEHYDRATION_ALLOWED = 0x0004,
        CF_HYDRATION_POLICY_MODIFIER_ALLOW_FULL_RESTART_HYDRATION = 0x0008
    }

    public enum CF_POPULATION_POLICY_PRIMARY : ushort
    {
        CF_POPULATION_POLICY_PARTIAL = 0,
        CF_POPULATION_POLICY_FULL = 2,
        CF_POPULATION_POLICY_ALWAYS_FULL = 3
    }

    [Flags]
    public enum CF_POPULATION_POLICY_MODIFIER : ushort
    {
        CF_POPULATION_POLICY_MODIFIER_NONE = 0x0000
    }

    public enum CF_INSYNC_POLICY : uint
    {
        CF_INSYNC_POLICY_NONE = 0x00000000,
        CF_INSYNC_POLICY_TRACK_FILE_CREATION_TIME = 0x00000001,
        CF_INSYNC_POLICY_TRACK_FILE_READONLY_ATTRIBUTE = 0x00000002,
        CF_INSYNC_POLICY_TRACK_FILE_HIDDEN_ATTRIBUTE = 0x00000004,
        CF_INSYNC_POLICY_TRACK_FILE_SYSTEM_ATTRIBUTE = 0x00000008,
        CF_INSYNC_POLICY_TRACK_DIRECTORY_CREATION_TIME = 0x00000010,
        CF_INSYNC_POLICY_TRACK_DIRECTORY_READONLY_ATTRIBUTE = 0x00000020,
        CF_INSYNC_POLICY_TRACK_DIRECTORY_HIDDEN_ATTRIBUTE = 0x00000040,
        CF_INSYNC_POLICY_TRACK_DIRECTORY_SYSTEM_ATTRIBUTE = 0x00000080,
        CF_INSYNC_POLICY_TRACK_FILE_LAST_WRITE_TIME = 0x00000100,
        CF_INSYNC_POLICY_TRACK_DIRECTORY_LAST_WRITE_TIME = 0x00000200,
        CF_INSYNC_POLICY_TRACK_FILE_ALL = 0x0055FFFF,
        CF_INSYNC_POLICY_TRACK_DIRECTORY_ALL = 0x00AA0000,
        CF_INSYNC_POLICY_TRACK_ALL = CF_INSYNC_POLICY_TRACK_FILE_ALL | CF_INSYNC_POLICY_TRACK_DIRECTORY_ALL,
        CF_INSYNC_POLICY_PRESERVE_INSYNC_FOR_SYNC_ENGINE = 0x80000000
    }

    public enum CF_HARDLINK_POLICY : uint
    {
        CF_HARDLINK_POLICY_NONE = 0x00000000,
        CF_HARDLINK_POLICY_ALLOWED = 0x00000001
    }

    public enum CF_CALLBACK_TYPE : uint
    {
        CF_CALLBACK_TYPE_FETCH_DATA = 0,
        CF_CALLBACK_TYPE_VALIDATE_DATA = 1,
        CF_CALLBACK_TYPE_CANCEL_FETCH_DATA = 2,
        CF_CALLBACK_TYPE_FETCH_PLACEHOLDERS = 3,
        CF_CALLBACK_TYPE_CANCEL_FETCH_PLACEHOLDERS = 4,
        CF_CALLBACK_TYPE_NOTIFY_FILE_OPEN_COMPLETION = 5,
        CF_CALLBACK_TYPE_NOTIFY_FILE_CLOSE_COMPLETION = 6,
        CF_CALLBACK_TYPE_NOTIFY_DEHYDRATE = 7,
        CF_CALLBACK_TYPE_NOTIFY_DEHYDRATE_COMPLETION = 8,
        CF_CALLBACK_TYPE_NOTIFY_DELETE = 9,
        CF_CALLBACK_TYPE_NOTIFY_DELETE_COMPLETION = 10,
        CF_CALLBACK_TYPE_NOTIFY_RENAME = 11,
        CF_CALLBACK_TYPE_NOTIFY_RENAME_COMPLETION = 12,
        CF_CALLBACK_TYPE_NONE = 0xFFFFFFFF
    }

    [Flags]
    public enum CF_PLACEHOLDER_CREATE_FLAGS : uint
    {
        CF_PLACEHOLDER_CREATE_FLAG_NONE = 0x00000000,
        CF_PLACEHOLDER_CREATE_FLAG_DISABLE_ON_DEMAND_POPULATION = 0x00000001,
        CF_PLACEHOLDER_CREATE_FLAG_MARK_IN_SYNC = 0x00000002,
        CF_PLACEHOLDER_CREATE_FLAG_SUPERSEDE = 0x00000004,
        CF_PLACEHOLDER_CREATE_FLAG_ALWAYS_FULL = 0x00000008
    }

    [Flags]
    public enum CF_CONVERT_FLAGS : uint
    {
        CF_CONVERT_FLAG_NONE = 0x00000000,
        CF_CONVERT_FLAG_MARK_IN_SYNC = 0x00000001,
        CF_CONVERT_FLAG_DEHYDRATE = 0x00000002,
        CF_CONVERT_FLAG_ENABLE_ON_DEMAND_POPULATION = 0x00000004,
        CF_CONVERT_FLAG_ALWAYS_FULL = 0x00000008,
        CF_CONVERT_FLAG_FORCE_CONVERT_TO_CLOUD_FILE = 0x00000010
    }

    [Flags]
    public enum CF_HYDRATE_FLAGS : uint
    {
        CF_HYDRATE_FLAG_NONE = 0x00000000
    }

    [Flags]
    public enum CF_DEHYDRATE_FLAGS : uint
    {
        CF_DEHYDRATE_FLAG_NONE = 0x00000000,
        CF_DEHYDRATE_FLAG_BACKGROUND = 0x00000001
    }

    [Flags]
    public enum CF_SET_IN_SYNC_FLAGS : uint
    {
        CF_SET_IN_SYNC_FLAG_NONE = 0x00000000
    }

    [Flags]
    public enum CF_SET_PIN_FLAGS : uint
    {
        CF_SET_PIN_FLAG_NONE = 0x00000000,
        CF_SET_PIN_FLAG_RECURSE = 0x00000001,
        CF_SET_PIN_FLAG_RECURSE_ONLY = 0x00000002,
        CF_SET_PIN_FLAG_RECURSE_STOP_ON_ERROR = 0x00000004
    }

    public enum CF_PIN_STATE : uint
    {
        CF_PIN_STATE_UNSPECIFIED = 0,
        CF_PIN_STATE_PINNED = 1,
        CF_PIN_STATE_UNPINNED = 2,
        CF_PIN_STATE_EXCLUDED = 3,
        CF_PIN_STATE_INHERIT = 4
    }

    public enum CF_IN_SYNC_STATE : uint
    {
        CF_IN_SYNC_STATE_NOT_IN_SYNC = 0,
        CF_IN_SYNC_STATE_IN_SYNC = 1
    }

    public enum CF_PLACEHOLDER_STATE : uint
    {
        CF_PLACEHOLDER_STATE_NO_STATES = 0x00000000,
        CF_PLACEHOLDER_STATE_PLACEHOLDER = 0x00000001,
        CF_PLACEHOLDER_STATE_SYNC_ROOT = 0x00000002,
        CF_PLACEHOLDER_STATE_ESSENTIAL_PROP_PRESENT = 0x00000004,
        CF_PLACEHOLDER_STATE_IN_SYNC = 0x00000008,
        CF_PLACEHOLDER_STATE_PARTIAL = 0x00000010,
        CF_PLACEHOLDER_STATE_PARTIALLY_ON_DISK = 0x00000020,
        CF_PLACEHOLDER_STATE_INVALID = 0xFFFFFFFF
    }

    public enum CF_SYNC_PROVIDER_STATUS : uint
    {
        CF_PROVIDER_STATUS_DISCONNECTED = 0x00000000,
        CF_PROVIDER_STATUS_IDLE = 0x00000001,
        CF_PROVIDER_STATUS_POPULATE_NAMESPACE = 0x00000002,
        CF_PROVIDER_STATUS_POPULATE_METADATA = 0x00000004,
        CF_PROVIDER_STATUS_POPULATE_CONTENT = 0x00000008,
        CF_PROVIDER_STATUS_SYNC_INCREMENTAL = 0x00000010,
        CF_PROVIDER_STATUS_SYNC_FULL = 0x00000020,
        CF_PROVIDER_STATUS_CONNECTIVITY_LOST = 0x00000040,
        CF_PROVIDER_STATUS_CLEAR_FLAGS = 0x80000000,
        CF_PROVIDER_STATUS_TERMINATED = 0xC0000001,
        CF_PROVIDER_STATUS_ERROR = 0xC0000002
    }

    [Flags]
    public enum CF_OPERATION_TRANSFER_DATA_FLAGS : uint
    {
        CF_OPERATION_TRANSFER_DATA_FLAG_NONE = 0x00000000
    }

    [Flags]
    public enum CF_OPERATION_TRANSFER_PLACEHOLDERS_FLAGS : uint
    {
        CF_OPERATION_TRANSFER_PLACEHOLDERS_FLAG_NONE = 0x00000000,
        CF_OPERATION_TRANSFER_PLACEHOLDERS_FLAG_STOP_ON_ERROR = 0x00000001,
        CF_OPERATION_TRANSFER_PLACEHOLDERS_FLAG_DISABLE_ON_DEMAND_POPULATION = 0x00000002
    }

    [Flags]
    public enum CF_OPERATION_ACK_DATA_FLAGS : uint
    {
        CF_OPERATION_ACK_DATA_FLAG_NONE = 0x00000000
    }

    [Flags]
    public enum CF_OPERATION_ACK_DEHYDRATE_FLAGS : uint
    {
        CF_OPERATION_ACK_DEHYDRATE_FLAG_NONE = 0x00000000
    }

    [Flags]
    public enum CF_OPERATION_ACK_DELETE_FLAGS : uint
    {
        CF_OPERATION_ACK_DELETE_FLAG_NONE = 0x00000000
    }

    [Flags]
    public enum CF_OPERATION_ACK_RENAME_FLAGS : uint
    {
        CF_OPERATION_ACK_RENAME_FLAG_NONE = 0x00000000
    }

    [Flags]
    public enum CF_OPERATION_RESTART_HYDRATION_FLAGS : uint
    {
        CF_OPERATION_RESTART_HYDRATION_FLAG_NONE = 0x00000000,
        CF_OPERATION_RESTART_HYDRATION_FLAG_MARK_IN_SYNC = 0x00000001
    }

    public enum CF_OPERATION_TYPE : uint
    {
        CF_OPERATION_TYPE_TRANSFER_DATA = 0,
        CF_OPERATION_TYPE_RETRIEVE_DATA = 1,
        CF_OPERATION_TYPE_ACK_DATA = 2,
        CF_OPERATION_TYPE_RESTART_HYDRATION = 3,
        CF_OPERATION_TYPE_TRANSFER_PLACEHOLDERS = 4,
        CF_OPERATION_TYPE_ACK_DEHYDRATE = 5,
        CF_OPERATION_TYPE_ACK_DELETE = 6,
        CF_OPERATION_TYPE_ACK_RENAME = 7
    }

    #endregion

    #region Structures

    [StructLayout(LayoutKind.Sequential)]
    public struct CF_SYNC_REGISTRATION
    {
        public uint StructSize;
        public IntPtr ProviderName;
        public IntPtr ProviderVersion;
        public IntPtr SyncRootIdentity;
        public uint SyncRootIdentityLength;
        public IntPtr FileIdentity;
        public uint FileIdentityLength;
        public Guid ProviderId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CF_SYNC_POLICIES
    {
        public uint StructSize;
        public CF_HYDRATION_POLICY Hydration;
        public CF_POPULATION_POLICY Population;
        public CF_INSYNC_POLICY InSync;
        public CF_HARDLINK_POLICY HardLink;
        public CF_PLACEHOLDER_MANAGEMENT_POLICY PlaceholderManagement;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CF_HYDRATION_POLICY
    {
        public CF_HYDRATION_POLICY_PRIMARY Primary;
        public CF_HYDRATION_POLICY_MODIFIER Modifier;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CF_POPULATION_POLICY
    {
        public CF_POPULATION_POLICY_PRIMARY Primary;
        public CF_POPULATION_POLICY_MODIFIER Modifier;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CF_PLACEHOLDER_MANAGEMENT_POLICY
    {
        public uint Policy;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CF_CALLBACK_REGISTRATION
    {
        public CF_CALLBACK_TYPE Type;
        public IntPtr Callback;

        public static readonly CF_CALLBACK_REGISTRATION CF_CALLBACK_REGISTRATION_END = new()
        {
            Type = CF_CALLBACK_TYPE.CF_CALLBACK_TYPE_NONE,
            Callback = IntPtr.Zero
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CF_CALLBACK_INFO
    {
        public uint StructSize;
        public long ConnectionKey;
        public IntPtr CallbackContext;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string VolumeGuidName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string VolumeDosName;
        public uint VolumeSerialNumber;
        public long SyncRootFileId;
        public IntPtr SyncRootIdentity;
        public uint SyncRootIdentityLength;
        public long FileId;
        public long FileSize;
        public IntPtr FileIdentity;
        public uint FileIdentityLength;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string NormalizedPath;
        public long TransferKey;
        public byte PriorityHint;
        public IntPtr CorrelationVector;
        public IntPtr ProcessInfo;
        public long RequestKey;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CF_CALLBACK_PARAMETERS
    {
        public uint ParamSize;
        // This is a union in native code, we'll handle specific cases
        public CF_CALLBACK_PARAMETERS_UNION Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct CF_CALLBACK_PARAMETERS_UNION
    {
        [FieldOffset(0)]
        public FETCH_DATA FetchData;
        
        [FieldOffset(0)]
        public CANCEL_FETCH_DATA CancelFetchData;
        
        [FieldOffset(0)]
        public FETCH_PLACEHOLDERS FetchPlaceholders;
        
        [FieldOffset(0)]
        public NOTIFY_DELETE Delete;
        
        [FieldOffset(0)]
        public NOTIFY_RENAME Rename;
        
        [FieldOffset(0)]
        public NOTIFY_DEHYDRATE Dehydrate;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FETCH_DATA
    {
        public uint Flags;
        public long RequiredFileOffset;
        public long RequiredLength;
        public long OptionalFileOffset;
        public long OptionalLength;
        public long LastDehydrationTime;
        public uint LastDehydrationReason;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CANCEL_FETCH_DATA
    {
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FETCH_PLACEHOLDERS
    {
        public uint Flags;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string Pattern;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NOTIFY_DELETE
    {
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NOTIFY_RENAME
    {
        public uint Flags;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string TargetPath;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NOTIFY_DEHYDRATE
    {
        public uint Flags;
        public uint Reason;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CF_PLACEHOLDER_CREATE_INFO
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string RelativeFileName;
        public CF_FS_METADATA FsMetadata;
        public IntPtr FileIdentity;
        public uint FileIdentityLength;
        public CF_PLACEHOLDER_CREATE_FLAGS Flags;
        public int Result;
        public long CreateUsn;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CF_FS_METADATA
    {
        public uint FileAttributes;
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public long ChangeTime;
        public long FileSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CF_OPERATION_INFO
    {
        public uint StructSize;
        public CF_OPERATION_TYPE Type;
        public long ConnectionKey;
        public long TransferKey;
        public IntPtr CorrelationVector;
        public IntPtr SyncStatus;
        public long RequestKey;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CF_OPERATION_PARAMETERS
    {
        public uint ParamSize;
        public CF_OPERATION_PARAMETERS_UNION Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct CF_OPERATION_PARAMETERS_UNION
    {
        [FieldOffset(0)]
        public CF_OPERATION_TRANSFER_DATA TransferData;
        
        [FieldOffset(0)]
        public CF_OPERATION_TRANSFER_PLACEHOLDERS TransferPlaceholders;
        
        [FieldOffset(0)]
        public CF_OPERATION_ACK_DATA AckData;
        
        [FieldOffset(0)]
        public CF_OPERATION_ACK_DEHYDRATE AckDehydrate;
        
        [FieldOffset(0)]
        public CF_OPERATION_ACK_DELETE AckDelete;
        
        [FieldOffset(0)]
        public CF_OPERATION_ACK_RENAME AckRename;
        
        [FieldOffset(0)]
        public CF_OPERATION_RESTART_HYDRATION RestartHydration;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CF_OPERATION_TRANSFER_DATA
    {
        public CF_OPERATION_TRANSFER_DATA_FLAGS Flags;
        public int CompletionStatus;
        public IntPtr Buffer;
        public long Offset;
        public long Length;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CF_OPERATION_TRANSFER_PLACEHOLDERS
    {
        public CF_OPERATION_TRANSFER_PLACEHOLDERS_FLAGS Flags;
        public int CompletionStatus;
        public long PlaceholderTotalCount;
        public IntPtr PlaceholderArray;
        public uint PlaceholderCount;
        public uint EntriesProcessed;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CF_OPERATION_ACK_DATA
    {
        public CF_OPERATION_ACK_DATA_FLAGS Flags;
        public int CompletionStatus;
        public long Offset;
        public long Length;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CF_OPERATION_ACK_DEHYDRATE
    {
        public CF_OPERATION_ACK_DEHYDRATE_FLAGS Flags;
        public int CompletionStatus;
        public IntPtr FileIdentity;
        public uint FileIdentityLength;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CF_OPERATION_ACK_DELETE
    {
        public CF_OPERATION_ACK_DELETE_FLAGS Flags;
        public int CompletionStatus;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CF_OPERATION_ACK_RENAME
    {
        public CF_OPERATION_ACK_RENAME_FLAGS Flags;
        public int CompletionStatus;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CF_OPERATION_RESTART_HYDRATION
    {
        public CF_OPERATION_RESTART_HYDRATION_FLAGS Flags;
        public IntPtr FsMetadata;
        public IntPtr FileIdentity;
        public uint FileIdentityLength;
    }

    #endregion

    #region Delegates

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void CF_CALLBACK(
        in CF_CALLBACK_INFO CallbackInfo,
        in CF_CALLBACK_PARAMETERS CallbackParameters);

    #endregion

    #region Functions

    /// <summary>
    /// Registers a sync root with the Windows Cloud Filter platform
    /// </summary>
    [DllImport(CfApiDll, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int CfRegisterSyncRoot(
        [MarshalAs(UnmanagedType.LPWStr)] string SyncRootPath,
        in CF_SYNC_REGISTRATION Registration,
        in CF_SYNC_POLICIES Policies,
        CF_REGISTER_FLAGS RegisterFlags);

    /// <summary>
    /// Unregisters a previously registered sync root
    /// </summary>
    [DllImport(CfApiDll, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int CfUnregisterSyncRoot(
        [MarshalAs(UnmanagedType.LPWStr)] string SyncRootPath);

    /// <summary>
    /// Connects the sync provider to the sync root
    /// </summary>
    [DllImport(CfApiDll, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int CfConnectSyncRoot(
        [MarshalAs(UnmanagedType.LPWStr)] string SyncRootPath,
        [MarshalAs(UnmanagedType.LPArray)] CF_CALLBACK_REGISTRATION[] CallbackTable,
        IntPtr CallbackContext,
        CF_CONNECT_FLAGS ConnectFlags,
        out long ConnectionKey);

    /// <summary>
    /// Disconnects the sync provider from the sync root
    /// </summary>
    [DllImport(CfApiDll, SetLastError = true)]
    public static extern int CfDisconnectSyncRoot(long ConnectionKey);

    /// <summary>
    /// Creates one or more placeholder files or directories
    /// </summary>
    [DllImport(CfApiDll, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int CfCreatePlaceholders(
        [MarshalAs(UnmanagedType.LPWStr)] string BaseDirectoryPath,
        [In, Out] CF_PLACEHOLDER_CREATE_INFO[] PlaceholderArray,
        uint PlaceholderCount,
        CF_PLACEHOLDER_CREATE_FLAGS CreateFlags,
        out uint EntriesProcessed);

    /// <summary>
    /// Updates characteristics of a placeholder file or directory
    /// </summary>
    [DllImport(CfApiDll, SetLastError = true)]
    public static extern int CfUpdatePlaceholder(
        SafeFileHandle FileHandle,
        in CF_FS_METADATA FsMetadata,
        IntPtr FileIdentity,
        uint FileIdentityLength,
        IntPtr DehydrateRangeArray,
        uint DehydrateRangeCount,
        uint UpdateFlags,
        out long UpdateUsn,
        IntPtr Overlapped);

    /// <summary>
    /// Converts a regular file/directory to a placeholder
    /// </summary>
    [DllImport(CfApiDll, SetLastError = true)]
    public static extern int CfConvertToPlaceholder(
        SafeFileHandle FileHandle,
        IntPtr FileIdentity,
        uint FileIdentityLength,
        CF_CONVERT_FLAGS ConvertFlags,
        out long ConvertUsn,
        IntPtr Overlapped);

    /// <summary>
    /// Reverts a placeholder to a regular file
    /// </summary>
    [DllImport(CfApiDll, SetLastError = true)]
    public static extern int CfRevertPlaceholder(
        SafeFileHandle FileHandle,
        uint RevertFlags,
        IntPtr Overlapped);

    /// <summary>
    /// Hydrates a placeholder file
    /// </summary>
    [DllImport(CfApiDll, SetLastError = true)]
    public static extern int CfHydratePlaceholder(
        SafeFileHandle FileHandle,
        long StartingOffset,
        long Length,
        CF_HYDRATE_FLAGS HydrateFlags,
        IntPtr Overlapped);

    /// <summary>
    /// Dehydrates a file (converts full file back to placeholder)
    /// </summary>
    [DllImport(CfApiDll, SetLastError = true)]
    public static extern int CfDehydratePlaceholder(
        SafeFileHandle FileHandle,
        long StartingOffset,
        long Length,
        CF_DEHYDRATE_FLAGS DehydrateFlags,
        IntPtr Overlapped);

    /// <summary>
    /// Sets the in-sync state of a placeholder
    /// </summary>
    [DllImport(CfApiDll, SetLastError = true)]
    public static extern int CfSetInSyncState(
        SafeFileHandle FileHandle,
        CF_IN_SYNC_STATE InSyncState,
        CF_SET_IN_SYNC_FLAGS SetInSyncFlags,
        IntPtr InSyncUsn);

    /// <summary>
    /// Sets the pin state of a placeholder
    /// </summary>
    [DllImport(CfApiDll, SetLastError = true)]
    public static extern int CfSetPinState(
        SafeFileHandle FileHandle,
        CF_PIN_STATE PinState,
        CF_SET_PIN_FLAGS SetPinFlags,
        IntPtr Overlapped);

    /// <summary>
    /// Gets the placeholder state of a file
    /// </summary>
    [DllImport(CfApiDll, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern CF_PLACEHOLDER_STATE CfGetPlaceholderStateFromFileInfo(
        IntPtr InfoBuffer,
        uint InfoClass);

    /// <summary>
    /// Updates the sync provider status
    /// </summary>
    [DllImport(CfApiDll, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int CfUpdateSyncProviderStatus(
        long ConnectionKey,
        CF_SYNC_PROVIDER_STATUS ProviderStatus);

    /// <summary>
    /// Reports progress during data transfer
    /// </summary>
    [DllImport(CfApiDll, SetLastError = true)]
    public static extern int CfReportProviderProgress(
        long ConnectionKey,
        long TransferKey,
        long ProviderProgressTotal,
        long ProviderProgressCompleted);

    /// <summary>
    /// Executes a Cloud Filter operation
    /// </summary>
    [DllImport(CfApiDll, SetLastError = true)]
    public static extern int CfExecute(
        in CF_OPERATION_INFO OpInfo,
        ref CF_OPERATION_PARAMETERS OpParams);

    /// <summary>
    /// Gets a transfer key for a file handle
    /// </summary>
    [DllImport(CfApiDll, SetLastError = true)]
    public static extern int CfGetTransferKey(
        SafeFileHandle FileHandle,
        out long TransferKey);

    /// <summary>
    /// Releases a transfer key
    /// </summary>
    [DllImport(CfApiDll, SetLastError = true)]
    public static extern void CfReleaseTransferKey(
        SafeFileHandle FileHandle,
        long TransferKey);

    /// <summary>
    /// Opens a file with an oplock for cloud file operations
    /// </summary>
    [DllImport(CfApiDll, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int CfOpenFileWithOplock(
        [MarshalAs(UnmanagedType.LPWStr)] string FilePath,
        uint Flags,
        out SafeFileHandle ProtectedHandle);

    /// <summary>
    /// Closes the protected handle
    /// </summary>
    [DllImport(CfApiDll, SetLastError = true)]
    public static extern void CfCloseHandle(SafeFileHandle FileHandle);

    /// <summary>
    /// References a protected handle to get a regular Win32 handle
    /// </summary>
    [DllImport(CfApiDll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CfReferenceProtectedHandle(SafeFileHandle ProtectedHandle);

    /// <summary>
    /// Releases the reference to a protected handle
    /// </summary>
    [DllImport(CfApiDll, SetLastError = true)]
    public static extern void CfReleaseProtectedHandle(SafeFileHandle ProtectedHandle);

    /// <summary>
    /// Gets the Win32 handle from a protected handle
    /// </summary>
    [DllImport(CfApiDll, SetLastError = true)]
    public static extern IntPtr CfGetWin32HandleFromProtectedHandle(SafeFileHandle ProtectedHandle);

    #endregion

    #region Helper Methods

    /// <summary>
    /// Checks if the Cloud Filter API is available on this system
    /// </summary>
    public static bool IsCloudFilterAvailable()
    {
        try
        {
            // Try to load the DLL
            var handle = LoadLibrary(CfApiDll);
            if (handle != IntPtr.Zero)
            {
                FreeLibrary(handle);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(IntPtr hModule);

    /// <summary>
    /// Converts HRESULT to exception if failed
    /// </summary>
    public static void ThrowIfFailed(int hr, string operation)
    {
        if (hr < 0)
        {
            throw Marshal.GetExceptionForHR(hr) ?? new Exception($"{operation} failed with HRESULT: 0x{hr:X8}");
        }
    }

    #endregion
}
