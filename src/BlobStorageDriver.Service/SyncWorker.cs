using BlobStorageDriver.SyncEngine;
using BlobStorageDriver.SyncEngine.CloudFilter;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BlobStorageDriver.Service;

/// <summary>
/// Background worker service that runs the sync engine
/// </summary>
public class SyncWorker : BackgroundService
{
    private readonly FileSyncEngine _syncEngine;
    private readonly CloudFilterProvider _cloudFilter;
    private readonly ILogger<SyncWorker> _logger;

    public SyncWorker(
        FileSyncEngine syncEngine,
        CloudFilterProvider cloudFilter,
        ILogger<SyncWorker> logger)
    {
        _syncEngine = syncEngine;
        _cloudFilter = cloudFilter;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Blob Storage Driver Service starting");

        try
        {
            // Register sync root with Windows Cloud Files API
            await _cloudFilter.RegisterSyncRootAsync();
            _logger.LogInformation("Sync root registered");

            // Start sync engine
            await _syncEngine.StartAsync();
            _logger.LogInformation("Sync engine started");

            // Keep running until cancellation
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Service cancellation requested");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Service encountered an error");
            throw;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Blob Storage Driver Service stopping");

        _syncEngine.Stop();
        _cloudFilter.Dispose();

        await base.StopAsync(cancellationToken);
    }
}
