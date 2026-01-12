using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using BlobStorageDriver.Common.Configuration;
using BlobStorageDriver.CloudProvider;
using BlobStorageDriver.SyncEngine;
using BlobStorageDriver.SyncEngine.Integration;
using BlobStorageDriver.WinUI.ViewModels;
using BlobStorageDriver.WinUI.Services;
using Serilog;
using System;
using System.IO;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using WinRT.Interop;

namespace BlobStorageDriver.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private Window? _mainWindow;
    private IHost? _host;
    private TrayIconService? _trayIconService;
    private AppWindow? _appWindow;
    private bool _isExiting;
    private bool _startMinimized;
    
    public static IServiceProvider Services => ((App)Current)._host!.Services;
    public static Window MainWindow => ((App)Current)._mainWindow!;
    
    /// <summary>
    /// Initializes the singleton application object.
    /// </summary>
    public App()
    {
        this.InitializeComponent();
        
        // Check for --minimized argument
        var args = Environment.GetCommandLineArgs();
        _startMinimized = Array.Exists(args, arg => 
            arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("-m", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("/minimized", StringComparison.OrdinalIgnoreCase));
        
        // Set up global exception handlers
        this.UnhandledException += App_UnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        
        // Configure Serilog
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BlobStorageDriver", "Logs", "app-.log");
        
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
            .CreateLogger();
        
        Log.Information("App constructor complete");
        
        // Build host with DI
        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices((context, services) =>
            {
                // Configuration
                var config = AppConfiguration.Load();
                services.AddSingleton(config);
                services.AddSingleton(config.AzureBlob);
                services.AddSingleton(config.Cache);
                services.AddSingleton(config.Sync);
                services.AddSingleton(config.Ui);
                services.AddSingleton(config.Integration);
                
                // Add logging
                services.AddLogging(builder => builder.AddSerilog());
                
                // Cloud provider
                services.AddSingleton<ICloudStorageProvider, AzureBlobStorageProvider>();
                
                // Cache manager
                services.AddSingleton<BlobStorageDriver.SyncEngine.Cache.LocalCacheManager>();
                
                // Sync engine
                services.AddSingleton<FileSyncEngine>();
                services.AddSingleton<IntegrationManager>();
                
                // ViewModels
                services.AddTransient<MainViewModel>();
                services.AddTransient<SettingsViewModel>();
                
                // Services
                services.AddSingleton<TrayIconService>();
                services.AddSingleton<NotificationService>();
            })
            .Build();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            Log.Information("Creating MainWindow...");
            _mainWindow = new MainWindow();
            Log.Information("MainWindow created successfully");
            
            // Get the AppWindow for window management
            var windowHandle = WindowNative.GetWindowHandle(_mainWindow);
            var windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
            _appWindow = AppWindow.GetFromWindowId(windowId);
            Log.Information("AppWindow obtained");
            
            // Initialize tray icon
            Log.Information("Getting TrayIconService...");
            _trayIconService = Services.GetRequiredService<TrayIconService>();
            Log.Information("Initializing TrayIconService...");
            _trayIconService.Initialize(_mainWindow);
            Log.Information("TrayIconService initialized");
            
            // Wire up tray icon events
            _trayIconService.ShowRequested += (s, e) => ShowMainWindow();
            _trayIconService.ExitRequested += (s, e) => ExitApplication();
            _trayIconService.SyncNowRequested += async (s, e) =>
            {
                var syncEngine = Services.GetService<FileSyncEngine>();
                if (syncEngine != null)
                {
                    await syncEngine.PerformSyncAsync();
                }
            };
            _trayIconService.PauseRequested += (s, e) =>
            {
                var syncEngine = Services.GetService<FileSyncEngine>();
                if (syncEngine != null)
                {
                    if (syncEngine.IsPaused)
                        syncEngine.Resume();
                    else
                        syncEngine.Pause();
                }
            };
            
            // Handle window closing - minimize to tray instead of exiting
            _appWindow.Closing += (s, e) =>
            {
                if (!_isExiting)
                {
                    e.Cancel = true;
                    HideMainWindow();
                    _trayIconService?.ShowBalloon("Blob Storage Driver", "Application minimized to system tray");
                }
            };
            
            Log.Information("Activating MainWindow...");
            
            // Start minimized if requested via command line
            if (_startMinimized)
            {
                Log.Information("Starting minimized to system tray");
                HideMainWindow();
                _trayIconService?.ShowBalloon("Blob Storage Driver", "Application started in system tray");
            }
            else
            {
                _mainWindow.Activate();
            }
            
            Log.Information("Application launched successfully");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application startup failed");
            
            // Write to a crash log file
            var crashLogPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BlobStorageDriver", "crash.log");
            File.WriteAllText(crashLogPath, $"{DateTime.Now}: {ex}");
            
            throw;
        }
    }
    
    private void ShowMainWindow()
    {
        if (_mainWindow != null)
        {
            _mainWindow.Activate();
            _appWindow?.Show();
        }
    }
    
    private void HideMainWindow()
    {
        _appWindow?.Hide();
    }
    
    private void ExitApplication()
    {
        _isExiting = true;
        _trayIconService?.Dispose();
        _host?.Dispose();
        _mainWindow?.Close();
        Exit();
    }
    
    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "Unhandled exception in WinUI");
        WriteCrashLog($"WinUI UnhandledException: {e.Exception}");
        e.Handled = true; // Try to keep app running
    }
    
    private void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        Log.Fatal(ex, "Unhandled AppDomain exception");
        WriteCrashLog($"AppDomain UnhandledException: {ex}");
    }
    
    private void TaskScheduler_UnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unobserved task exception");
        WriteCrashLog($"Unobserved Task Exception: {e.Exception}");
        e.SetObserved();
    }
    
    private static void WriteCrashLog(string message)
    {
        try
        {
            var crashLogPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BlobStorageDriver", "crash.log");
            File.AppendAllText(crashLogPath, $"{DateTime.Now}: {message}\n\n");
        }
        catch { }
    }
}
