using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Drawing;
using BlobStorageDriver.Common.Configuration;
using BlobStorageDriver.CloudProvider;
using BlobStorageDriver.SyncEngine;
using BlobStorageDriver.SyncEngine.Cache;
using BlobStorageDriver.SyncEngine.CloudFilter;
using BlobStorageDriver.TrayApp.Services;
using BlobStorageDriver.TrayApp.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client.Desktop;
using Serilog;
using Hardcodet.Wpf.TaskbarNotification;
using Application = System.Windows.Application;
using IntegrationManager = BlobStorageDriver.SyncEngine.Integration.IntegrationManager;

namespace BlobStorageDriver.TrayApp;

public partial class App : Application
{
    private TaskbarIcon? _taskbarIcon;
    private IServiceProvider? _serviceProvider;
    private IntegrationManager? _integrationManager;
    
    public static IServiceProvider Services => ((App)Application.Current)._serviceProvider!;
    
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        // Set up global exception handlers
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            Log.Fatal(ex, "Unhandled AppDomain exception");
            System.Windows.MessageBox.Show($"Fatal error: {ex?.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        };
        
        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved(); // Prevent crash
        };
        
        DispatcherUnhandledException += (s, args) =>
        {
            Log.Error(args.Exception, "Dispatcher unhandled exception");
            args.Handled = true; // Prevent crash
        };
        
        try
        {
            // Configure Serilog
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(
                    Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "BlobStorageDriver",
                        "logs",
                        "app-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7)
                .CreateLogger();
            
            Log.Information("Blob Storage Driver starting...");
            
            // Load configuration
            var config = AppConfiguration.Load();
            
            // Setup DI
            var services = new ServiceCollection();
            ConfigureServices(services, config);
            _serviceProvider = services.BuildServiceProvider();
            
            // Create taskbar icon programmatically
            _taskbarIcon = CreateTrayIcon();
            
            Log.Information("Tray icon created");
            
            // Check if configured
            var isConfigured = !string.IsNullOrEmpty(config.AzureBlob.AccountName) || 
                               !string.IsNullOrEmpty(config.AzureBlob.ConnectionString);
            
            if (!isConfigured)
            {
                // Not configured - show main window for setup
                Dispatcher.InvokeAsync(() => ShowMainWindow());
            }
            else if (config.AzureBlob.AuthType == Common.Configuration.AuthenticationType.EntraIdInteractive)
            {
                // Configured with Entra ID - prompt for authentication then start
                Dispatcher.InvokeAsync(async () => await PromptAuthenticationAndStartAsync(config));
            }
            else
            {
                // Configured with other auth type - start automatically
                Dispatcher.InvokeAsync(async () => await StartIntegrationAsync());
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fatal error during startup");
            System.Windows.MessageBox.Show(
                $"Failed to start application: {ex.Message}",
                "Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    
    /// <summary>
    /// Prompts for Entra ID authentication then starts the integration
    /// </summary>
    private async Task PromptAuthenticationAndStartAsync(AppConfiguration config)
    {
        try
        {
            Log.Information("Prompting for Entra ID authentication...");
            
            // Check if we already have a cached token
            if (MsalTokenProvider.HasValidToken)
            {
                Log.Information("Using cached Entra ID token");
                await StartIntegrationAsync();
                return;
            }
            
            // Get tenant ID from config
            var tenantId = string.IsNullOrWhiteSpace(config.AzureBlob.TenantId) 
                ? null 
                : config.AzureBlob.TenantId;

            // Use MSAL with embedded WebView for authentication
            var appBuilder = Microsoft.Identity.Client.PublicClientApplicationBuilder
                .Create(MsalTokenProvider.ClientId)
                .WithAuthority(Microsoft.Identity.Client.AzureCloudInstance.AzurePublic, tenantId ?? "common")
                .WithDefaultRedirectUri()
                .WithWindowsEmbeddedBrowserSupport();
            
            var app = appBuilder.Build();
            
            // Request a token - this opens the embedded browser
            var scopes = new[] { "https://storage.azure.com/.default" };
            var result = await app.AcquireTokenInteractive(scopes)
                .WithUseEmbeddedWebView(true)
                .ExecuteAsync();
            
            // Store the token
            MsalTokenProvider.SetCachedToken(result);
            
            Log.Information("Entra ID authentication successful");
            
            // Start the integration
            await StartIntegrationAsync();
        }
        catch (Microsoft.Identity.Client.MsalException ex)
        {
            Log.Warning(ex, "Entra ID authentication was cancelled or failed");
            // Show tray notification
            _taskbarIcon?.ShowBalloonTip(
                "Authentication Required",
                "Sign in to access Azure Blob Storage. Right-click the tray icon to open settings.",
                Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Warning);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during startup authentication");
        }
    }
    
    private async Task InitializeSyncEngineAsync(AppConfiguration config)
    {
        try
        {
            var syncEngine = _serviceProvider!.GetRequiredService<FileSyncEngine>();
            await syncEngine.StartAsync();
            Log.Information("Sync engine started");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start sync engine");
            
            // Show error on UI thread
            await Dispatcher.InvokeAsync(() =>
            {
                System.Windows.MessageBox.Show(
                    $"Failed to connect to cloud storage: {ex.Message}\n\nPlease check your configuration.",
                    "Connection Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            });
        }
        
        // Show main window on first run or if not configured
        if (string.IsNullOrEmpty(config.AzureBlob.AccountName) && string.IsNullOrEmpty(config.AzureBlob.ConnectionString))
        {
            await Dispatcher.InvokeAsync(() => ShowMainWindow());
        }
    }
    
    private TaskbarIcon CreateTrayIcon()
    {
        var icon = new TaskbarIcon
        {
            ToolTipText = "Azure Blob Storage",
            Icon = CreateStorageIcon(),
            MenuActivation = PopupActivationMode.LeftOrRightClick
        };
        
        // Create context menu
        var contextMenu = new ContextMenu();
        
        var openItem = new MenuItem { Header = "Open Blob Storage Driver" };
        openItem.Click += (s, e) => ShowMainWindow();
        contextMenu.Items.Add(openItem);
        
        contextMenu.Items.Add(new Separator());
        
        var syncNowItem = new MenuItem { Header = "Sync Now" };
        syncNowItem.Click += async (s, e) => 
        {
            var syncEngine = _serviceProvider?.GetService<FileSyncEngine>();
            if (syncEngine != null)
            {
                await syncEngine.PerformSyncAsync();
            }
        };
        contextMenu.Items.Add(syncNowItem);
        
        var pauseItem = new MenuItem { Header = "Pause Sync" };
        pauseItem.Click += (s, e) => 
        {
            var syncEngine = _serviceProvider?.GetService<FileSyncEngine>();
            if (syncEngine != null)
            {
                if (syncEngine.IsPaused)
                {
                    syncEngine.Resume();
                    ((MenuItem)s!).Header = "Pause Sync";
                }
                else
                {
                    syncEngine.Pause();
                    ((MenuItem)s!).Header = "Resume Sync";
                }
            }
        };
        contextMenu.Items.Add(pauseItem);
        
        contextMenu.Items.Add(new Separator());
        
        var settingsItem = new MenuItem { Header = "Settings" };
        settingsItem.Click += (s, e) => ShowMainWindow();
        contextMenu.Items.Add(settingsItem);
        
        contextMenu.Items.Add(new Separator());
        
        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (s, e) => ExitApplication();
        contextMenu.Items.Add(exitItem);
        
        icon.ContextMenu = contextMenu;
        
        // Double-click to open main window
        icon.TrayMouseDoubleClick += (s, e) => ShowMainWindow();
        
        return icon;
    }
    
    /// <summary>
    /// Creates a custom storage icon for the system tray
    /// </summary>
    private static Icon CreateStorageIcon()
    {
        // Create a 16x16 bitmap for the tray icon
        using var bitmap = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bitmap);
        
        // Enable anti-aliasing
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        
        // Draw a stylized storage/database icon (stacked cylinders)
        using var azureBrush = new SolidBrush(Color.FromArgb(0, 120, 212)); // Azure blue
        using var lightBrush = new SolidBrush(Color.FromArgb(80, 170, 230)); // Lighter blue
        using var pen = new Pen(Color.FromArgb(0, 90, 160), 1f);
        
        // Bottom cylinder (storage)
        g.FillEllipse(azureBrush, 2, 10, 12, 4);
        g.FillRectangle(azureBrush, 2, 8, 12, 4);
        g.FillEllipse(lightBrush, 2, 6, 12, 4);
        
        // Middle cylinder
        g.FillRectangle(azureBrush, 2, 4, 12, 4);
        g.FillEllipse(lightBrush, 2, 2, 12, 4);
        
        // Top highlight
        g.FillEllipse(lightBrush, 4, 3, 8, 2);
        
        // Convert to icon
        var hIcon = bitmap.GetHicon();
        return Icon.FromHandle(hIcon);
    }
    
    private void ConfigureServices(IServiceCollection services, AppConfiguration config)
    {
        // Logging
        services.AddLogging(builder =>
        {
            builder.AddSerilog();
        });
        
        // Configuration
        services.AddSingleton(config);
        services.AddSingleton(config.AzureBlob);
        services.AddSingleton(config.Cache);
        services.AddSingleton(config.Sync);
        services.AddSingleton(config.Ui);
        
        // Cloud provider - use factory to delay creation until settings are configured
        // Don't create at startup since container name may be empty
        services.AddSingleton<Func<ICloudStorageProvider>>(sp => () => 
        {
            var settings = sp.GetRequiredService<AzureBlobSettings>();
            var logger = sp.GetRequiredService<ILogger<AzureBlobStorageProvider>>();
            return new AzureBlobStorageProvider(settings, logger);
        });
        
        // Cache manager
        services.AddSingleton<LocalCacheManager>();
        
        // Cloud filter
        services.AddSingleton<CloudFilterProvider>();
        
        // Sync engine - don't create at startup
        // services.AddSingleton<FileSyncEngine>();
        
        // Services
        services.AddSingleton<NotificationService>();
        services.AddSingleton<TrayIconService>();
        
        // ViewModels - use parameterless constructors, sync engine set later when configured
        services.AddSingleton<MainViewModel>();
        services.AddTransient<SyncProgressViewModel>();
        services.AddTransient<ConflictViewModel>();
        services.AddSingleton<SettingsViewModel>();
    }
    
    protected override void OnExit(ExitEventArgs e)
    {
        _taskbarIcon?.Dispose();
        
        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
        
        Log.CloseAndFlush();
        base.OnExit(e);
    }
    
    public void ShowMainWindow()
    {
        if (MainWindow == null)
        {
            MainWindow = new Views.MainWindow();
        }
        
        MainWindow.Show();
        MainWindow.Activate();
    }
    
    /// <summary>
    /// Start the integration (Cloud Files/shell namespace) with the current settings
    /// </summary>
    public async Task<bool> StartIntegrationAsync()
    {
        try
        {
            var config = _serviceProvider!.GetRequiredService<AppConfiguration>();
            
            // Create the cloud provider with current settings
            var blobSettings = config.AzureBlob;
            
            // Check if configured
            if (string.IsNullOrEmpty(blobSettings.AccountName) && string.IsNullOrEmpty(blobSettings.ConnectionString))
            {
                Log.Warning("Cannot start integration: Azure settings not configured");
                return false;
            }
            
            Log.Information("Starting integration with mode {Mode}", config.Integration.Mode);
            
            // Create the provider (uses MSAL cached token for Entra ID)
            var loggerFactory = _serviceProvider?.GetRequiredService<ILoggerFactory>();
            if (loggerFactory == null)
            {
                Log.Warning("Cannot start integration: LoggerFactory not available");
                return false;
            }
            var provider = new AzureBlobStorageProvider(blobSettings, loggerFactory.CreateLogger<AzureBlobStorageProvider>());
            
            // Test connection first
            var canConnect = await provider.TestConnectionAsync();
            if (!canConnect)
            {
                Log.Warning("Cannot start integration: Failed to connect to Azure Blob Storage");
                return false;
            }
            
            // Create and start integration manager
            var cacheManager = _serviceProvider!.GetRequiredService<LocalCacheManager>();
            var integrationManager = new BlobStorageDriver.SyncEngine.Integration.IntegrationManager(
                provider,
                cacheManager,
                config,
                loggerFactory.CreateLogger<BlobStorageDriver.SyncEngine.Integration.IntegrationManager>(),
                loggerFactory);
            
            // Hook up file activity events to MainViewModel
            var mainVm = _serviceProvider!.GetService<MainViewModel>();
            if (mainVm != null)
            {
                integrationManager.FileActivity += (s, e) =>
                {
                    // Update on UI thread
                    Dispatcher.InvokeAsync(() =>
                    {
                        mainVm.AddActivity(e);
                    });
                };
            }
            
            // Store for later access
            _integrationManager = integrationManager;
            
            var success = await integrationManager.StartAsync();
            
            if (success)
            {
                Log.Information("Integration started successfully in {Mode} mode", config.Integration.Mode);
                
                // Update main view model status
                if (mainVm != null)
                {
                    mainVm.StatusMessage = $"✓ Connected - {config.Integration.Mode} active";
                    mainVm.IsConnected = true;
                }
            }
            else
            {
                Log.Warning("Failed to start integration");
            }
            
            return success;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error starting integration");
            return false;
        }
    }
    
    public void ExitApplication()
    {
        _integrationManager?.Dispose();
        
        var syncEngine = _serviceProvider?.GetService<FileSyncEngine>();
        syncEngine?.Stop();
        
        Shutdown();
    }
}
