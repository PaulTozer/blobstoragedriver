using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using BlobStorageDriver.WinUI.Pages;
using System;
using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using WinRT.Interop;
using System.Drawing;
using System.Runtime.InteropServices;

namespace BlobStorageDriver.WinUI;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        this.InitializeComponent();
        
        // Set window title
        Title = "Blob Storage Driver";
        
        // Set the window icon
        SetWindowIcon();
    }
    
    private void SetWindowIcon()
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            
            // Try to use icon file first
            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "appicon.ico");
            if (System.IO.File.Exists(iconPath))
            {
                appWindow.SetIcon(iconPath);
            }
            else
            {
                // Fallback: Create icon from embedded bitmap
                using var bitmap = CreateStorageIconBitmap();
                var hIcon = bitmap.GetHicon();
                
                // Set both small and large icons using Win32 API
                SendMessage(hwnd, WM_SETICON, ICON_SMALL, hIcon);
                SendMessage(hwnd, WM_SETICON, ICON_BIG, hIcon);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to set window icon: {ex.Message}");
        }
    }
    
    private static Bitmap CreateStorageIconBitmap()
    {
        // Create a 32x32 icon with storage-like appearance (Azure blue cylinders)
        var bitmap = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        
        // Azure blue colors
        using var darkBrush = new SolidBrush(Color.FromArgb(0, 120, 212));
        using var lightBrush = new SolidBrush(Color.FromArgb(80, 200, 230));
        using var highlightBrush = new SolidBrush(Color.FromArgb(128, 200, 240));
        
        // Draw stacked cylinders (storage icon)
        // Bottom cylinder
        g.FillEllipse(darkBrush, 2, 22, 28, 8);
        g.FillRectangle(darkBrush, 2, 16, 28, 8);
        g.FillEllipse(lightBrush, 2, 12, 28, 8);
        
        // Top cylinder
        g.FillRectangle(darkBrush, 2, 6, 28, 8);
        g.FillEllipse(lightBrush, 2, 2, 28, 8);
        g.FillEllipse(highlightBrush, 8, 4, 16, 4);
        
        return bitmap;
    }
    
    // Win32 constants for icon setting
    private const int WM_SETICON = 0x0080;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;
    
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, IntPtr lParam);
    
    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        // Navigate to Status page by default
        ContentFrame.Navigate(typeof(StatusPage));
    }
    
    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer is NavigationViewItem selectedItem)
        {
            var tag = selectedItem.Tag?.ToString();
            Type? pageType = tag switch
            {
                "Status" => typeof(StatusPage),
                "Activity" => typeof(ActivityPage),
                "Conflicts" => typeof(ConflictsPage),
                "Settings" => typeof(SettingsPage),
                _ => null
            };
            
            if (pageType != null)
            {
                ContentFrame.Navigate(pageType);
            }
        }
    }
    
    private void OpenSyncFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var configPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BlobStorageDriver", "config.json");
            
            if (System.IO.File.Exists(configPath))
            {
                var json = System.IO.File.ReadAllText(configPath);
                // Simple extraction of LocalSyncFolder - in production use proper JSON parsing
                var folderMatch = System.Text.RegularExpressions.Regex.Match(json, "\"LocalSyncFolder\"\\s*:\\s*\"([^\"]+)\"");
                if (folderMatch.Success)
                {
                    var folder = folderMatch.Groups[1].Value.Replace("\\\\", "\\");
                    if (System.IO.Directory.Exists(folder))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = folder,
                            UseShellExecute = true
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error opening sync folder: {ex.Message}");
        }
    }
    
    private void SyncNow_Click(object sender, RoutedEventArgs e)
    {
        // TODO: Trigger sync via service
        Debug.WriteLine("Sync Now clicked");
    }
}
