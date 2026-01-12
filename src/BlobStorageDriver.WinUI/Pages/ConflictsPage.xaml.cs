using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.DependencyInjection;
using BlobStorageDriver.WinUI.ViewModels;
using BlobStorageDriver.Common.Models;
using System.Collections.Specialized;

namespace BlobStorageDriver.WinUI.Pages;

public sealed partial class ConflictsPage : Page
{
    private MainViewModel? _viewModel;
    
    public ConflictsPage()
    {
        this.InitializeComponent();
        
        _viewModel = App.Services.GetService<MainViewModel>();
        
        if (_viewModel != null)
        {
            ConflictsList.ItemsSource = _viewModel.Conflicts;
            _viewModel.Conflicts.CollectionChanged += Conflicts_CollectionChanged;
            UpdateEmptyState();
        }
    }
    
    private void Conflicts_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(UpdateEmptyState);
    }
    
    private void UpdateEmptyState()
    {
        var hasItems = _viewModel?.Conflicts.Count > 0;
        EmptyState.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
        ConflictsList.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
    }
    
    private async void ResolveConflict_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is SyncConflict conflict)
        {
            var resolution = button.Tag?.ToString() switch
            {
                "Local" => ConflictResolution.KeepLocal,
                "Remote" => ConflictResolution.KeepCloud,
                "Both" => ConflictResolution.KeepBoth,
                _ => (ConflictResolution?)null
            };
            
            if (resolution.HasValue && _viewModel != null)
            {
                conflict.Resolution = resolution;
                await _viewModel.ResolveConflictCommand.ExecuteAsync(conflict);
            }
        }
    }
}
