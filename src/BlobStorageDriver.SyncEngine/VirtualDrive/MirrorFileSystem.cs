using System.Security.AccessControl;
using DokanNet;
using Microsoft.Extensions.Logging;
using FileAccess = DokanNet.FileAccess;

namespace BlobStorageDriver.SyncEngine.VirtualDrive;

/// <summary>
/// Simple mirror file system that just mirrors a local folder.
/// Used as a stable base for the virtual drive.
/// </summary>
public class MirrorFileSystem : IDokanOperations
{
    private readonly string _rootPath;
    private readonly ILogger _logger;

    public MirrorFileSystem(string rootPath, ILogger logger)
    {
        _rootPath = rootPath;
        _logger = logger;
        
        // Ensure root path exists
        if (!Directory.Exists(_rootPath))
            Directory.CreateDirectory(_rootPath);
    }

    private string GetPath(string fileName)
    {
        var path = fileName;
        if (path.StartsWith("\\"))
            path = path.Substring(1);
        return Path.Combine(_rootPath, path);
    }

    public NtStatus CreateFile(string fileName, FileAccess access, System.IO.FileShare share,
        System.IO.FileMode mode, FileOptions options, FileAttributes attributes, IDokanFileInfo info)
    {
        try
        {
            var path = GetPath(fileName);

            if (info.IsDirectory)
            {
                switch (mode)
                {
                    case System.IO.FileMode.Open:
                        if (!Directory.Exists(path))
                            return NtStatus.ObjectPathNotFound;
                        break;
                    case System.IO.FileMode.CreateNew:
                        if (Directory.Exists(path))
                            return NtStatus.ObjectNameCollision;
                        Directory.CreateDirectory(path);
                        break;
                }
                return NtStatus.Success;
            }

            var pathExists = File.Exists(path);
            var directoryExists = Directory.Exists(Path.GetDirectoryName(path));

            switch (mode)
            {
                case System.IO.FileMode.Open:
                    if (!pathExists)
                        return Directory.Exists(path) ? NtStatus.Success : NtStatus.ObjectNameNotFound;
                    break;

                case System.IO.FileMode.CreateNew:
                    if (pathExists)
                        return NtStatus.ObjectNameCollision;
                    break;

                case System.IO.FileMode.Truncate:
                    if (!pathExists)
                        return NtStatus.ObjectNameNotFound;
                    break;
            }

            if (!directoryExists)
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
            }

            info.Context = path;
            return NtStatus.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CreateFile error: {FileName}", fileName);
            return NtStatus.Success;
        }
    }

    public void Cleanup(string fileName, IDokanFileInfo info)
    {
        try
        {
            if (info.DeleteOnClose)
            {
                var path = GetPath(fileName);
                if (info.IsDirectory)
                {
                    if (Directory.Exists(path))
                        Directory.Delete(path, true);
                }
                else
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
            }
            info.Context = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cleanup error: {FileName}", fileName);
        }
    }

    public void CloseFile(string fileName, IDokanFileInfo info)
    {
        info.Context = null;
    }

    public NtStatus ReadFile(string fileName, byte[] buffer, out int bytesRead, long offset, IDokanFileInfo info)
    {
        bytesRead = 0;
        try
        {
            var path = GetPath(fileName);
            if (!File.Exists(path))
                return NtStatus.ObjectNameNotFound;

            using var stream = new FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
            stream.Position = offset;
            bytesRead = stream.Read(buffer, 0, buffer.Length);
            return NtStatus.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ReadFile error: {FileName}", fileName);
            return NtStatus.Success;
        }
    }

    public NtStatus WriteFile(string fileName, byte[] buffer, out int bytesWritten, long offset, IDokanFileInfo info)
    {
        bytesWritten = 0;
        try
        {
            var path = GetPath(fileName);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using var stream = new FileStream(path, System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite);
            stream.Position = offset;
            stream.Write(buffer, 0, buffer.Length);
            bytesWritten = buffer.Length;
            return NtStatus.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WriteFile error: {FileName}", fileName);
            return NtStatus.Success;
        }
    }

    public NtStatus FlushFileBuffers(string fileName, IDokanFileInfo info) => NtStatus.Success;

    public NtStatus GetFileInformation(string fileName, out FileInformation fileInfo, IDokanFileInfo info)
    {
        fileInfo = new FileInformation();
        try
        {
            var path = GetPath(fileName);
            
            if (File.Exists(path))
            {
                var fi = new FileInfo(path);
                fileInfo = new FileInformation
                {
                    FileName = fi.Name,
                    Attributes = fi.Attributes,
                    CreationTime = fi.CreationTime,
                    LastAccessTime = fi.LastAccessTime,
                    LastWriteTime = fi.LastWriteTime,
                    Length = fi.Length
                };
                return NtStatus.Success;
            }
            
            if (Directory.Exists(path))
            {
                var di = new DirectoryInfo(path);
                fileInfo = new FileInformation
                {
                    FileName = di.Name,
                    Attributes = di.Attributes,
                    CreationTime = di.CreationTime,
                    LastAccessTime = di.LastAccessTime,
                    LastWriteTime = di.LastWriteTime
                };
                return NtStatus.Success;
            }

            // For root
            if (string.IsNullOrEmpty(fileName) || fileName == "\\")
            {
                fileInfo = new FileInformation
                {
                    FileName = "\\",
                    Attributes = FileAttributes.Directory,
                    CreationTime = DateTime.Now,
                    LastAccessTime = DateTime.Now,
                    LastWriteTime = DateTime.Now
                };
                return NtStatus.Success;
            }

            return NtStatus.ObjectNameNotFound;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetFileInformation error: {FileName}", fileName);
            return NtStatus.Success;
        }
    }

    public NtStatus FindFiles(string fileName, out IList<FileInformation> files, IDokanFileInfo info)
    {
        return FindFilesWithPattern(fileName, "*", out files, info);
    }

    public NtStatus FindFilesWithPattern(string fileName, string searchPattern, out IList<FileInformation> files, IDokanFileInfo info)
    {
        files = new List<FileInformation>();
        try
        {
            var path = GetPath(fileName);
            if (!Directory.Exists(path))
            {
                // Return empty list instead of error
                return NtStatus.Success;
            }

            var dirInfo = new DirectoryInfo(path);
            foreach (var fsi in dirInfo.EnumerateFileSystemInfos(searchPattern))
            {
                files.Add(new FileInformation
                {
                    FileName = fsi.Name,
                    Attributes = fsi.Attributes,
                    CreationTime = fsi.CreationTime,
                    LastAccessTime = fsi.LastAccessTime,
                    LastWriteTime = fsi.LastWriteTime,
                    Length = fsi is FileInfo fi ? fi.Length : 0
                });
            }
            return NtStatus.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FindFilesWithPattern error: {FileName}", fileName);
            return NtStatus.Success;
        }
    }

    public NtStatus SetFileAttributes(string fileName, FileAttributes attributes, IDokanFileInfo info) => NtStatus.Success;
    public NtStatus SetFileTime(string fileName, DateTime? creationTime, DateTime? lastAccessTime, DateTime? lastWriteTime, IDokanFileInfo info) => NtStatus.Success;
    public NtStatus DeleteFile(string fileName, IDokanFileInfo info) => NtStatus.Success;
    public NtStatus DeleteDirectory(string fileName, IDokanFileInfo info) => NtStatus.Success;

    public NtStatus MoveFile(string oldName, string newName, bool replace, IDokanFileInfo info)
    {
        try
        {
            var oldPath = GetPath(oldName);
            var newPath = GetPath(newName);

            if (info.IsDirectory)
            {
                if (Directory.Exists(newPath))
                {
                    if (replace)
                        Directory.Delete(newPath, true);
                    else
                        return NtStatus.ObjectNameCollision;
                }
                Directory.Move(oldPath, newPath);
            }
            else
            {
                if (File.Exists(newPath))
                {
                    if (replace)
                        File.Delete(newPath);
                    else
                        return NtStatus.ObjectNameCollision;
                }

                var dir = Path.GetDirectoryName(newPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.Move(oldPath, newPath);
            }
            return NtStatus.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MoveFile error: {OldName} to {NewName}", oldName, newName);
            return NtStatus.Success;
        }
    }

    public NtStatus SetEndOfFile(string fileName, long length, IDokanFileInfo info)
    {
        try
        {
            var path = GetPath(fileName);
            using var stream = new FileStream(path, System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite);
            stream.SetLength(length);
            return NtStatus.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SetEndOfFile error: {FileName}", fileName);
            return NtStatus.Success;
        }
    }

    public NtStatus SetAllocationSize(string fileName, long length, IDokanFileInfo info) => SetEndOfFile(fileName, length, info);

    public NtStatus LockFile(string fileName, long offset, long length, IDokanFileInfo info) => NtStatus.Success;
    public NtStatus UnlockFile(string fileName, long offset, long length, IDokanFileInfo info) => NtStatus.Success;

    public NtStatus GetDiskFreeSpace(out long freeBytesAvailable, out long totalNumberOfBytes, out long totalNumberOfFreeBytes, IDokanFileInfo info)
    {
        var driveInfo = new DriveInfo(Path.GetPathRoot(_rootPath) ?? "C:");
        totalNumberOfBytes = driveInfo.TotalSize;
        freeBytesAvailable = driveInfo.AvailableFreeSpace;
        totalNumberOfFreeBytes = driveInfo.TotalFreeSpace;
        return NtStatus.Success;
    }

    public NtStatus GetVolumeInformation(out string volumeLabel, out FileSystemFeatures features,
        out string fileSystemName, out uint maximumComponentLength, IDokanFileInfo info)
    {
        volumeLabel = "Azure Blob";
        fileSystemName = "NTFS";
        maximumComponentLength = 256;
        features = FileSystemFeatures.CasePreservedNames |
                   FileSystemFeatures.CaseSensitiveSearch |
                   FileSystemFeatures.UnicodeOnDisk;
        return NtStatus.Success;
    }

    public NtStatus GetFileSecurity(string fileName, out FileSystemSecurity? security, AccessControlSections sections, IDokanFileInfo info)
    {
        security = null;
        return NtStatus.NotImplemented;
    }

    public NtStatus SetFileSecurity(string fileName, FileSystemSecurity security, AccessControlSections sections, IDokanFileInfo info)
    {
        return NtStatus.NotImplemented;
    }

    public NtStatus Mounted(string mountPoint, IDokanFileInfo info)
    {
        _logger.LogInformation("Mirror mounted at {MountPoint}, root: {RootPath}", mountPoint, _rootPath);
        return NtStatus.Success;
    }

    public NtStatus Unmounted(IDokanFileInfo info)
    {
        _logger.LogInformation("Mirror unmounted");
        return NtStatus.Success;
    }

    public NtStatus FindStreams(string fileName, out IList<FileInformation> streams, IDokanFileInfo info)
    {
        streams = Array.Empty<FileInformation>();
        return NtStatus.NotImplemented;
    }
}
