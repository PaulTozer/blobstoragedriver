using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace BlobStorageDriver.SyncEngine.CloudFilter;

/// <summary>
/// Maps a folder to a drive letter using the Windows DefineDosDevice API.
/// This allows the Cloud Filter sync root to appear as a traditional drive letter
/// for compatibility with legacy applications that require drive paths.
/// 
/// Unlike Dokan, this doesn't create a virtual file system - it simply creates
/// a symbolic link from a drive letter to the sync root folder. The folder
/// still uses Cloud Filter placeholders and on-demand hydration.
/// </summary>
public class DriveLetterMapper : IDisposable
{
    private readonly ILogger<DriveLetterMapper> _logger;
    private char? _mappedDriveLetter;
    private string? _mappedPath;
    private bool _disposed;

    // Windows API constants
    private const uint DDD_RAW_TARGET_PATH = 0x00000001;
    private const uint DDD_REMOVE_DEFINITION = 0x00000002;
    private const uint DDD_EXACT_MATCH_ON_REMOVE = 0x00000004;
    private const uint DDD_NO_BROADCAST_SYSTEM = 0x00000008;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool DefineDosDevice(uint dwFlags, string lpDeviceName, string lpTargetPath);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint QueryDosDevice(string lpDeviceName, char[] lpTargetPath, uint ucchMax);

    public DriveLetterMapper(ILogger<DriveLetterMapper> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets the currently mapped drive letter, if any
    /// </summary>
    public char? MappedDriveLetter => _mappedDriveLetter;

    /// <summary>
    /// Gets the path that is mapped to the drive letter
    /// </summary>
    public string? MappedPath => _mappedPath;

    /// <summary>
    /// Checks if a specific drive letter is available
    /// </summary>
    public static bool IsDriveLetterAvailable(char driveLetter)
    {
        driveLetter = char.ToUpperInvariant(driveLetter);
        if (driveLetter < 'A' || driveLetter > 'Z')
            return false;

        var drivePath = $"{driveLetter}:\\";
        return !Directory.Exists(drivePath) && !DriveInfo.GetDrives().Any(d => 
            char.ToUpperInvariant(d.Name[0]) == driveLetter);
    }

    /// <summary>
    /// Gets the first available drive letter starting from Z and going backwards
    /// </summary>
    public static char? GetFirstAvailableDriveLetter()
    {
        for (char c = 'Z'; c >= 'D'; c--)
        {
            if (IsDriveLetterAvailable(c))
                return c;
        }
        return null;
    }

    /// <summary>
    /// Maps a folder path to a drive letter using Windows DefineDosDevice
    /// </summary>
    /// <param name="folderPath">The folder to map (typically the Cloud Filter sync root)</param>
    /// <param name="driveLetter">The drive letter to use (e.g., 'Z'). If null, auto-selects first available.</param>
    /// <returns>The mapped drive letter, or null if mapping failed</returns>
    public char? MapDriveLetter(string folderPath, char? driveLetter = null)
    {
        if (_mappedDriveLetter.HasValue)
        {
            _logger.LogWarning("Drive letter {Letter}: is already mapped to {Path}. Unmapping first.", 
                _mappedDriveLetter.Value, _mappedPath);
            UnmapDriveLetter();
        }

        // Validate folder exists
        if (!Directory.Exists(folderPath))
        {
            _logger.LogError("Cannot map drive letter: folder does not exist: {Path}", folderPath);
            return null;
        }

        // Get absolute path
        folderPath = Path.GetFullPath(folderPath);

        // Auto-select drive letter if not specified
        if (!driveLetter.HasValue)
        {
            driveLetter = GetFirstAvailableDriveLetter();
            if (!driveLetter.HasValue)
            {
                _logger.LogError("No available drive letters found");
                return null;
            }
        }

        char letter = char.ToUpperInvariant(driveLetter.Value);
        if (letter < 'A' || letter > 'Z')
        {
            _logger.LogError("Invalid drive letter: {Letter}", driveLetter);
            return null;
        }

        if (!IsDriveLetterAvailable(letter))
        {
            _logger.LogError("Drive letter {Letter}: is not available", letter);
            return null;
        }

        // Format the device name (e.g., "Z:")
        string deviceName = $"{letter}:";
        
        // Format the target path for DefineDosDevice
        // For directory mapping, we use the path with \??\ prefix
        string targetPath = $"\\??\\{folderPath}";

        _logger.LogInformation("Mapping drive letter {Letter}: to {Path}", letter, folderPath);

        // Create the drive mapping
        if (!DefineDosDevice(DDD_RAW_TARGET_PATH | DDD_NO_BROADCAST_SYSTEM, deviceName, targetPath))
        {
            var error = Marshal.GetLastWin32Error();
            _logger.LogError("Failed to map drive letter {Letter}: Error code {ErrorCode}", 
                letter, error);
            return null;
        }

        _mappedDriveLetter = letter;
        _mappedPath = folderPath;

        _logger.LogInformation("Successfully mapped {Letter}: to {Path}", letter, folderPath);
        return letter;
    }

    /// <summary>
    /// Unmaps the currently mapped drive letter
    /// </summary>
    public bool UnmapDriveLetter()
    {
        if (!_mappedDriveLetter.HasValue)
        {
            _logger.LogDebug("No drive letter is currently mapped");
            return true;
        }

        string deviceName = $"{_mappedDriveLetter.Value}:";
        string targetPath = $"\\??\\{_mappedPath}";

        _logger.LogInformation("Unmapping drive letter {Letter}:", _mappedDriveLetter.Value);

        // Remove the drive mapping
        if (!DefineDosDevice(DDD_REMOVE_DEFINITION | DDD_EXACT_MATCH_ON_REMOVE | DDD_NO_BROADCAST_SYSTEM, 
            deviceName, targetPath))
        {
            var error = Marshal.GetLastWin32Error();
            
            // Try without exact match (fallback)
            if (!DefineDosDevice(DDD_REMOVE_DEFINITION, deviceName, null!))
            {
                _logger.LogWarning("Failed to unmap drive letter {Letter}: Error code {ErrorCode}", 
                    _mappedDriveLetter.Value, error);
                return false;
            }
        }

        _logger.LogInformation("Successfully unmapped drive letter {Letter}:", _mappedDriveLetter.Value);
        
        _mappedDriveLetter = null;
        _mappedPath = null;
        return true;
    }

    /// <summary>
    /// Gets the target path for a given drive letter
    /// </summary>
    public static string? GetDriveTarget(char driveLetter)
    {
        driveLetter = char.ToUpperInvariant(driveLetter);
        string deviceName = $"{driveLetter}:";
        
        var buffer = new char[1024];
        uint result = QueryDosDevice(deviceName, buffer, (uint)buffer.Length);
        
        if (result == 0)
            return null;
            
        var target = new string(buffer, 0, (int)result).TrimEnd('\0');
        
        // Remove the \??\ prefix if present
        if (target.StartsWith("\\??\\"))
            target = target.Substring(4);
            
        return target;
    }

    /// <summary>
    /// Checks if a drive letter is mapped to a specific folder
    /// </summary>
    public static bool IsDriveMappedToFolder(char driveLetter, string folderPath)
    {
        var target = GetDriveTarget(driveLetter);
        if (target == null)
            return false;
            
        folderPath = Path.GetFullPath(folderPath);
        return string.Equals(target, folderPath, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        UnmapDriveLetter();
    }
}
