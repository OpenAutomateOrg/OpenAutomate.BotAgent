using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OpenAutomate.BotAgent.Service.Services
{
    /// <summary>
    /// Service to monitor memory usage and help prevent out-of-memory issues
    /// </summary>
    public class MemoryMonitorService : BackgroundService
    {
        private readonly ILogger<MemoryMonitorService> _logger;
        private readonly TimeSpan _monitoringInterval = TimeSpan.FromMinutes(1);
        private readonly long _memoryThresholdMB = 500; // Alert if memory usage exceeds 500MB

        public MemoryMonitorService(ILogger<MemoryMonitorService> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Memory monitoring service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await MonitorMemoryUsageAsync();
                    await Task.Delay(_monitoringInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancellation is requested
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in memory monitoring service");
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); // Wait longer on error
                }
            }

            _logger.LogInformation("Memory monitoring service stopped");
        }

        private async Task MonitorMemoryUsageAsync()
        {
            try
            {
                var currentProcess = Process.GetCurrentProcess();
                var workingSetMB = currentProcess.WorkingSet64 / 1024 / 1024;
                var privateMemoryMB = currentProcess.PrivateMemorySize64 / 1024 / 1024;
                var gcMemoryMB = GC.GetTotalMemory(false) / 1024 / 1024;

                _logger.LogDebug("Memory Usage - Working Set: {WorkingSetMB}MB, Private: {PrivateMemoryMB}MB, GC: {GcMemoryMB}MB",
                    workingSetMB, privateMemoryMB, gcMemoryMB);

                // Alert if memory usage is high
                if (workingSetMB > _memoryThresholdMB)
                {
                    _logger.LogWarning("High memory usage detected - Working Set: {WorkingSetMB}MB (threshold: {ThresholdMB}MB)",
                        workingSetMB, _memoryThresholdMB);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error monitoring memory usage");
            }
        }

        public override void Dispose()
        {
            _logger.LogInformation("Disposing memory monitor service");
            base.Dispose();
        }
    }
} 