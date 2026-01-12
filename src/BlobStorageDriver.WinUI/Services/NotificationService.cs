using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using System;

namespace BlobStorageDriver.WinUI.Services;

public class NotificationService
{
    private bool _isInitialized;
    
    public void Initialize()
    {
        if (_isInitialized) return;
        
        var notificationManager = AppNotificationManager.Default;
        notificationManager.NotificationInvoked += OnNotificationInvoked;
        notificationManager.Register();
        
        _isInitialized = true;
    }
    
    public void ShowNotification(string title, string message)
    {
        if (!_isInitialized) return;
        
        var builder = new AppNotificationBuilder()
            .AddText(title)
            .AddText(message);
        
        var notification = builder.BuildNotification();
        AppNotificationManager.Default.Show(notification);
    }
    
    public void ShowSyncCompleteNotification(int filesUploaded, int filesDownloaded)
    {
        var message = $"Uploaded: {filesUploaded}, Downloaded: {filesDownloaded}";
        ShowNotification("Sync Complete", message);
    }
    
    public void ShowConflictNotification(string fileName)
    {
        ShowNotification("Sync Conflict", $"Conflict detected: {fileName}");
    }
    
    public void ShowErrorNotification(string error)
    {
        ShowNotification("Sync Error", error);
    }
    
    private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        // Handle notification click - could navigate to specific page
    }
    
    public void Unregister()
    {
        if (_isInitialized)
        {
            AppNotificationManager.Default.Unregister();
            _isInitialized = false;
        }
    }
}
