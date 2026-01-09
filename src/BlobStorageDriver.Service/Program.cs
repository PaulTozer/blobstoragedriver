using BlobStorageDriver.CloudProvider;
using BlobStorageDriver.Common.Configuration;
using BlobStorageDriver.SyncEngine;
using BlobStorageDriver.SyncEngine.Cache;
using BlobStorageDriver.SyncEngine.CloudFilter;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace BlobStorageDriver.Service;

public class Program
{
    public static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "BlobStorageDriver",
                    "logs",
                    "service-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30)
            .WriteTo.EventLog("BlobStorageDriver", manageEventSource: true)
            .CreateLogger();

        try
        {
            Log.Information("Starting Blob Storage Driver Service");

            var host = Host.CreateDefaultBuilder(args)
                .UseWindowsService(options =>
                {
                    options.ServiceName = "BlobStorageDriver";
                })
                .ConfigureServices((context, services) =>
                {
                    // Load configuration
                    var config = AppConfiguration.Load();
                    services.AddSingleton(config);
                    services.AddSingleton(config.AzureBlob);
                    services.AddSingleton(config.Cache);
                    services.AddSingleton(config.Sync);

                    // Cloud provider
                    services.AddSingleton<ICloudStorageProvider, AzureBlobStorageProvider>();

                    // Cache manager
                    services.AddSingleton<LocalCacheManager>();

                    // Cloud filter provider
                    services.AddSingleton<CloudFilterProvider>();

                    // Sync engine
                    services.AddSingleton<FileSyncEngine>();

                    // Hosted service
                    services.AddHostedService<SyncWorker>();
                })
                .UseSerilog()
                .Build();

            await host.RunAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Service terminated unexpectedly");
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }
}
