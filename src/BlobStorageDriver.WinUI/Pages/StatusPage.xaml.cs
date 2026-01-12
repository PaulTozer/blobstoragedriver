using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.DependencyInjection;
using BlobStorageDriver.WinUI.ViewModels;
using BlobStorageDriver.Common.Configuration;
using BlobStorageDriver.Common.Models;
using System;

namespace BlobStorageDriver.WinUI.Pages;

public sealed partial class StatusPage : Page
{
    private MainViewModel? _viewModel;
    private AppConfiguration? _config;
    private bool _isPaused;
    
    public StatusPage()
    {
        this.InitializeComponent();
        
        try
        {
            _viewModel = App.Services.GetService<MainViewModel>();
            _config = App.Services.GetService<AppConfiguration>();
            
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged += ViewModel_PropertyChanged;
                UpdateUI();
            }
            
            LoadConnectionInfo();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"StatusPage init error: {ex}");
            // Continue without ViewModel - show unconfigured state
        }
    }
    
    private void LoadConnectionInfo()
    {
        if (_config == null) return;
        
        AccountText.Text = _config.AzureBlob.AccountName ?? "Not configured";
        ContainerText.Text = _config.AzureBlob.ContainerName ?? "Not configured";
        LocalPathText.Text = _config.Cache.LocalSyncFolder ?? "Not configured";
    }
    
    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() => UpdateUI());
    }
    
    private void UpdateUI()
    {
        if (_viewModel == null) return;
        
        StatusText.Text = _viewModel.StatusMessage;
        LastSyncText.Text = $"Last synced: {(_viewModel.LastSyncTime == default ? "Never" : _viewModel.LastSyncTime.ToString("g"))}";
        PendingCount.Text = _viewModel.PendingCount.ToString();
        ConflictCount.Text = _viewModel.ConflictCount.ToString();
        ErrorCount.Text = _viewModel.ErrorCount.ToString();
        
        // Progress
        var isSyncing = _viewModel.SyncState == SyncState.Syncing;
        ProgressCard.Visibility = isSyncing ? Visibility.Visible : Visibility.Collapsed;
        
        if (isSyncing)
        {
            ProgressText.Text = _viewModel.CurrentFile ?? "Syncing...";
            SyncProgressBar.Value = _viewModel.ProgressPercentage;
            ProgressDetail.Text = $"{_viewModel.ProgressPercentage:F0}%";
        }
        
        // Pause button
        _isPaused = _viewModel.IsPaused;
        PauseIcon.Glyph = _isPaused ? "\uE768" : "\uE769"; // Play or Pause
        PauseText.Text = _isPaused ? "Resume" : "Pause";
    }
    
    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.PauseSyncCommand.Execute(null);
    }
}
