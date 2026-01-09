using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BlobStorageDriver.Common.Models;
using BlobStorageDriver.SyncEngine;
using System.Collections.ObjectModel;

namespace BlobStorageDriver.TrayApp.ViewModels;

public partial class ConflictViewModel : ObservableObject
{
    private FileSyncEngine? _syncEngine;

    [ObservableProperty]
    private SyncConflict? _selectedConflict;

    [ObservableProperty]
    private ConflictResolution _selectedResolution = ConflictResolution.KeepBoth;

    public ObservableCollection<SyncConflict> Conflicts { get; } = new();

    public ConflictViewModel()
    {
        // Default constructor - sync engine will be set later when configured
    }

    public ConflictViewModel(FileSyncEngine syncEngine)
    {
        SetSyncEngine(syncEngine);
    }

    public void SetSyncEngine(FileSyncEngine syncEngine)
    {
        _syncEngine = syncEngine;
        
        _syncEngine.ConflictDetected += OnConflictDetected;
        
        // Load existing conflicts
        foreach (var conflict in _syncEngine.Conflicts.Values)
        {
            Conflicts.Add(conflict);
        }
    }

    private void OnConflictDetected(object? sender, Common.Events.ConflictDetectedEventArgs e)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            if (!Conflicts.Any(c => c.FilePath == e.Conflict.FilePath))
            {
                Conflicts.Add(e.Conflict);
            }
        });
    }

    [RelayCommand]
    private async Task ResolveSelectedAsync()
    {
        if (SelectedConflict == null || _syncEngine == null) return;

        await _syncEngine.ResolveConflictAsync(SelectedConflict.FilePath, SelectedResolution);
        Conflicts.Remove(SelectedConflict);
        SelectedConflict = Conflicts.FirstOrDefault();
    }

    [RelayCommand]
    private async Task KeepLocalAsync()
    {
        if (SelectedConflict == null || _syncEngine == null) return;
        
        await _syncEngine.ResolveConflictAsync(SelectedConflict.FilePath, ConflictResolution.KeepLocal);
        Conflicts.Remove(SelectedConflict);
        SelectedConflict = Conflicts.FirstOrDefault();
    }

    [RelayCommand]
    private async Task KeepCloudAsync()
    {
        if (SelectedConflict == null || _syncEngine == null) return;
        
        await _syncEngine.ResolveConflictAsync(SelectedConflict.FilePath, ConflictResolution.KeepCloud);
        Conflicts.Remove(SelectedConflict);
        SelectedConflict = Conflicts.FirstOrDefault();
    }

    [RelayCommand]
    private async Task KeepBothAsync()
    {
        if (SelectedConflict == null || _syncEngine == null) return;
        
        await _syncEngine.ResolveConflictAsync(SelectedConflict.FilePath, ConflictResolution.KeepBoth);
        Conflicts.Remove(SelectedConflict);
        SelectedConflict = Conflicts.FirstOrDefault();
    }

    [RelayCommand]
    private async Task ResolveAllAsync()
    {
        if (_syncEngine == null) return;
        
        var conflictsToResolve = Conflicts.ToList();
        
        foreach (var conflict in conflictsToResolve)
        {
            await _syncEngine.ResolveConflictAsync(conflict.FilePath, SelectedResolution);
            Conflicts.Remove(conflict);
        }
    }

    [RelayCommand]
    private void OpenLocalFile()
    {
        if (SelectedConflict == null) return;
        
        var cacheSettings = App.Services.GetService(typeof(Common.Configuration.CacheSettings)) 
            as Common.Configuration.CacheSettings;
        
        if (cacheSettings != null)
        {
            var localPath = Path.Combine(cacheSettings.LocalSyncFolder, SelectedConflict.FilePath);
            if (File.Exists(localPath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = localPath,
                    UseShellExecute = true
                });
            }
        }
    }

    public string ConflictTypeDescription => SelectedConflict?.Type switch
    {
        ConflictType.BothModified => "Both versions were modified since last sync",
        ConflictType.LocalModifiedCloudDeleted => "Local file was modified but deleted from cloud",
        ConflictType.LocalDeletedCloudModified => "Local file was deleted but modified in cloud",
        ConflictType.DuplicateCreate => "File was created in both locations",
        ConflictType.TypeMismatch => "File type changed (file/folder mismatch)",
        _ => "Unknown conflict type"
    };

    public string LocalInfo => SelectedConflict != null
        ? $"Modified: {SelectedConflict.LocalModifiedAt:g}\nSize: {FormatBytes(SelectedConflict.LocalSize ?? 0)}"
        : string.Empty;

    public string CloudInfo => SelectedConflict != null
        ? $"Modified: {SelectedConflict.CloudModifiedAt:g}\nSize: {FormatBytes(SelectedConflict.CloudSize ?? 0)}"
        : string.Empty;

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;
        
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        
        return $"{size:0.##} {sizes[order]}";
    }
}
