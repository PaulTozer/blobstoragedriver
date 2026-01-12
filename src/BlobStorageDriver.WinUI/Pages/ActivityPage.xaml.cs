using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.DependencyInjection;
using BlobStorageDriver.WinUI.ViewModels;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System;

namespace BlobStorageDriver.WinUI.Pages;

public sealed partial class ActivityPage : Page
{
    private MainViewModel? _viewModel;
    
    public ActivityPage()
    {
        this.InitializeComponent();
        
        _viewModel = App.Services.GetService<MainViewModel>();
        
        if (_viewModel != null)
        {
            ActivityList.ItemsSource = _viewModel.RecentActivity;
            _viewModel.RecentActivity.CollectionChanged += RecentActivity_CollectionChanged;
            UpdateEmptyState();
        }
    }
    
    private void RecentActivity_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(UpdateEmptyState);
    }
    
    private void UpdateEmptyState()
    {
        var hasItems = _viewModel?.RecentActivity.Count > 0;
        EmptyState.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
    }
}

// Extension class for activity display
public class ActivityItemDisplay
{
    public string Icon { get; set; } = "📋";
    public string FileName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime Time { get; set; }
    public string TimeDisplay => Time.ToString("HH:mm:ss");
}
