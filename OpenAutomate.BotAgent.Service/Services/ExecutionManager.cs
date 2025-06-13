using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenAutomate.BotAgent.Service.Core;
using OpenAutomate.BotAgent.Executor.Services;
using OpenAutomate.BotAgent.Executor.Models;

namespace OpenAutomate.BotAgent.Service.Services
{
    /// <summary>
    /// Enhanced execution manager that handles package download and bot execution with real-time status updates
    /// </summary>
    public class ExecutionManager : IExecutionManager
    {
        private readonly ILogger<ExecutionManager> _logger;
        private readonly IConfigurationService _configService;
        private readonly IPackageDownloadService _packageDownloadService;
        private readonly string _executorPath;
        private readonly string _botScriptsPath = @"C:\ProgramData\OpenAutomate\BotScripts";
        private readonly ConcurrentDictionary<string, Process> _runningExecutions = new();
        private readonly Timer _statusUpdateTimer;
        private SignalRBroadcaster _signalRBroadcaster;

        // Standardized log message templates
        private static class LogMessages
        {
            public const string ExecutionStarting = "Starting execution {ExecutionId} for package {PackageName} v{Version}";
            public const string PackageDownloadStarted = "Downloading package {PackageId} version {Version}";
            public const string PackageDownloadCompleted = "Package downloaded successfully to: {DownloadPath}";
            public const string PackageDownloadFailed = "Failed to download package {PackageId}";
            public const string TaskQueuedSuccessfully = "Task {ExecutionId} queued successfully";
            public const string ExecutorProcessStarted = "Executor process started for execution {ExecutionId} with PID: {ProcessId}";
            public const string ExecutorProcessCompleted = "Executor process completed for execution {ExecutionId} with exit code: {ExitCode}";
            public const string ExecutorProcessFailed = "Executor process failed for execution {ExecutionId}";
            public const string ExecutionCancelled = "Canceling execution {ExecutionId}";
            public const string StatusUpdateSent = "Status update sent for execution {ExecutionId}: {Status}";
            public const string ExecutorPathResolved = "Resolved executor full path: {ExecutorPath}";
            public const string ExecutorFileNotFound = "Executor file not found at: {ExecutorPath}";
        }

        public ExecutionManager(
            ILogger<ExecutionManager> logger,
            IConfigurationService configService,
            IPackageDownloadService packageDownloadService)
        {
            _logger = logger;
            _configService = configService;
            _packageDownloadService = packageDownloadService;
            _signalRBroadcaster = null; // Will be set later by BotAgentService
            
            // Fix executor path to point to the Executor folder in the installation directory
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            _logger.LogInformation("Base directory (AppDomain.CurrentDomain.BaseDirectory): {BaseDirectory}", baseDirectory);

            string executorPath;

            // Check if we're running from the installed location (Service folder exists)
            if (baseDirectory.EndsWith("Service\\", StringComparison.OrdinalIgnoreCase) ||
                baseDirectory.EndsWith("Service", StringComparison.OrdinalIgnoreCase))
            {
                // Running from installed location - go up one level and into Executor folder
                var installationRoot = Path.GetDirectoryName(baseDirectory.TrimEnd('\\'));
                executorPath = Path.Combine(installationRoot, "Executor", "OpenAutomate.BotAgent.Executor.exe");
                _logger.LogInformation("Detected installed location. Installation root: {InstallationRoot}", installationRoot);
            }
            else
            {
                // Running from development location - use relative path to development executor
                executorPath = Path.Combine(baseDirectory, "..", "..", "..", "..", "OpenAutomate.BotAgent.Executor", "bin", "Debug", "net8.0", "OpenAutomate.BotAgent.Executor.exe");
                _logger.LogInformation("Detected development location. Using relative path to development executor");
            }
            
            _executorPath = Path.GetFullPath(executorPath);
            _logger.LogInformation(LogMessages.ExecutorPathResolved, _executorPath);
            
            var executorExists = File.Exists(_executorPath);
            _logger.LogInformation("Executor file exists: {ExecutorExists}", executorExists);
            
            if (!executorExists)
            {
                _logger.LogWarning(LogMessages.ExecutorFileNotFound, _executorPath);
            }
            
            // Start periodic status update timer (every 5 seconds)
            _statusUpdateTimer = new Timer(SendPeriodicStatusUpdates, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }

        /// <summary>
        /// Sets the SignalR broadcaster (called by BotAgentService after SignalR is initialized)
        /// </summary>
        public void SetSignalRBroadcaster(SignalRBroadcaster signalRBroadcaster)
        {
            _signalRBroadcaster = signalRBroadcaster;
            _logger.LogInformation("SignalR broadcaster injected into ExecutionManager");
        }

        public async Task<string> StartExecutionAsync(object executionData)
        {
            _logger.LogInformation("=== StartExecutionAsync called ===");
            try
            {
                _logger.LogInformation("Received execution data: {ExecutionData}", JsonSerializer.Serialize(executionData));

                ExecutionCommand execution;
                
                // Try to deserialize the execution data directly if it's already the right type
                if (executionData is ExecutionCommand cmd)
                {
                    execution = cmd;
                }
                else
                {
                    try
                    {
                        // If it's a JsonElement (from SignalR), convert it properly with case-insensitive options
                        var jsonString = JsonSerializer.Serialize(executionData);
                        var options = new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true,
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                        };
                        
                        execution = JsonSerializer.Deserialize<ExecutionCommand>(jsonString, options);
                    }
                    catch (Exception deserEx)
                    {
                        _logger.LogError(deserEx, "Failed to deserialize execution data: {ExecutionData}", JsonSerializer.Serialize(executionData));
                        return string.Empty;
                    }
                }

                if (execution == null || string.IsNullOrEmpty(execution.ExecutionId) || string.IsNullOrEmpty(execution.PackageId))
                {
                    _logger.LogError("Invalid execution data - missing ExecutionId or PackageId. ExecutionId: {ExecutionId}, PackageId: {PackageId}", 
                        execution?.ExecutionId, execution?.PackageId);
                    return string.Empty;
                }

                _logger.LogInformation(LogMessages.ExecutionStarting, execution.ExecutionId, execution.PackageName, execution.Version);

                // Download package from backend
                _logger.LogInformation(LogMessages.PackageDownloadStarted, execution.PackageId, execution.Version);
                var downloadPath = await _packageDownloadService.DownloadPackageAsync(
                    execution.PackageId, execution.Version, execution.PackageName, execution.TenantSlug);

                if (string.IsNullOrEmpty(downloadPath))
                {
                    _logger.LogError(LogMessages.PackageDownloadFailed, execution.PackageId);
                    await BroadcastExecutionStatus(execution.ExecutionId, "Failed", "Package download failed");
                    return execution.ExecutionId;
                }

                _logger.LogInformation(LogMessages.PackageDownloadCompleted, downloadPath);

                // Create a single task for this execution and spawn dedicated executor
                await CreateSingleTaskAndSpawnExecutorAsync(execution, downloadPath);

                _logger.LogInformation(LogMessages.TaskQueuedSuccessfully, execution.ExecutionId);
                return execution.ExecutionId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting execution");
                return string.Empty;
            }
            finally
            {
                _logger.LogInformation("=== StartExecutionAsync completed ===");
            }
        }

        /// <summary>
        /// Creates a single task for the execution and spawns a dedicated executor process
        /// </summary>
        private async Task CreateSingleTaskAndSpawnExecutorAsync(ExecutionCommand execution, string downloadPath)
        {
            // Create a dedicated task queue file for this execution
            var taskQueuePath = Path.Combine(Path.GetTempPath(), $"TaskQueue_{execution.ExecutionId}.json");
            
            var task = new BotTask
            {
                TaskId = Guid.NewGuid().ToString(),
                ExecutionId = execution.ExecutionId,
                PackageId = execution.PackageId,
                PackageName = execution.PackageName,
                Version = execution.Version,
                ScriptPath = downloadPath,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            };

            var taskQueue = new TaskQueue
            {
                Tasks = new List<BotTask> { task }
            };

            // Save the single task to the dedicated queue file
            var json = JsonSerializer.Serialize(taskQueue, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(taskQueuePath, json);

            // Spawn dedicated executor process for this single task
            await SpawnDedicatedExecutorAsync(execution.ExecutionId, taskQueuePath);
        }

        /// <summary>
        /// Spawns a dedicated executor process for a single execution
        /// </summary>
        private async Task SpawnDedicatedExecutorAsync(string executionId, string taskQueuePath)
        {
            try
            {
                if (!File.Exists(_executorPath))
                {
                    _logger.LogError(LogMessages.ExecutorFileNotFound, _executorPath);
                    await BroadcastExecutionStatus(executionId, "Failed", "Executor not found");
                    return;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = _executorPath,
                    Arguments = $"--task-queue \"{taskQueuePath}\" --execution-id \"{executionId}\"",
                    UseShellExecute = true, // Show console window for each task
                    CreateNoWindow = false,
                    WindowStyle = ProcessWindowStyle.Normal
                };

                var process = new Process { StartInfo = startInfo };
                
                // Enable events to track process completion
                process.EnableRaisingEvents = true;
                process.Exited += async (sender, e) => await OnExecutorProcessExited(executionId, process, taskQueuePath);
                
                process.Start();
                
                // Track the running process
                _runningExecutions.TryAdd(executionId, process);
                
                _logger.LogInformation(LogMessages.ExecutorProcessStarted, executionId, process.Id);
                await BroadcastExecutionStatus(executionId, "Running", "Execution started");
                
                // Don't wait for the process - let it run independently
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to spawn executor for execution {ExecutionId}", executionId);
                await BroadcastExecutionStatus(executionId, "Failed", $"Failed to start executor: {ex.Message}");
                
                // Clean up the temporary task queue file
                try
                {
                    if (File.Exists(taskQueuePath))
                        File.Delete(taskQueuePath);
                }
                catch { }
            }
        }

        /// <summary>
        /// Handles executor process completion
        /// </summary>
        private async Task OnExecutorProcessExited(string executionId, Process process, string taskQueuePath)
        {
            try
            {
                _runningExecutions.TryRemove(executionId, out _);
                
                var exitCode = process.ExitCode;
                _logger.LogInformation(LogMessages.ExecutorProcessCompleted, executionId, exitCode);
                
                // Determine final status based on exit code
                string finalStatus = exitCode == 0 ? "Completed" : "Failed";
                string statusMessage = exitCode == 0 ? "Execution completed successfully" : $"Execution failed with exit code {exitCode}";
                
                await BroadcastExecutionStatus(executionId, finalStatus, statusMessage);
                
                // Clean up the temporary task queue file
                try
                {
                    if (File.Exists(taskQueuePath))
                        File.Delete(taskQueuePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to clean up task queue file: {TaskQueuePath}", taskQueuePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling executor process exit for execution {ExecutionId}", executionId);
            }
            finally
            {
                process?.Dispose();
            }
        }

        public async Task CancelExecutionAsync(string executionId)
        {
            _logger.LogInformation(LogMessages.ExecutionCancelled, executionId);
            
            if (_runningExecutions.TryGetValue(executionId, out var process))
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(true); // Kill process tree
                        await BroadcastExecutionStatus(executionId, "Cancelled", "Execution was cancelled");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error cancelling execution {ExecutionId}", executionId);
                }
                finally
                {
                    _runningExecutions.TryRemove(executionId, out _);
                }
            }
        }

        public async Task SendStatusUpdatesAsync()
        {
            // This method can be used to send periodic status updates
            await Task.CompletedTask;
        }

        public async Task<bool> HasActiveExecutionsAsync()
        {
            // Check if there are any running executor processes
            await Task.CompletedTask;
            return _runningExecutions.Count > 0;
        }

        /// <summary>
        /// Broadcasts execution status updates through SignalR
        /// </summary>
        private async Task BroadcastExecutionStatus(string executionId, string status, string message = null)
        {
            try
            {
                if (_signalRBroadcaster != null)
                {
                    await _signalRBroadcaster.BroadcastExecutionStatusAsync(executionId, status, message);
                }
                _logger.LogInformation(LogMessages.StatusUpdateSent, executionId, status);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to broadcast execution status for {ExecutionId}", executionId);
            }
        }

        /// <summary>
        /// Sends periodic status updates for all running executions
        /// </summary>
        private async void SendPeriodicStatusUpdates(object state)
        {
            try
            {
                foreach (var kvp in _runningExecutions.ToList())
                {
                    var executionId = kvp.Key;
                    var process = kvp.Value;
                    
                    if (process.HasExited)
                    {
                        // Process has exited but event might not have fired yet
                        _runningExecutions.TryRemove(executionId, out _);
                    }
                    else
                    {
                        // Send periodic heartbeat status
                        await BroadcastExecutionStatus(executionId, "Running", "Execution in progress");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during periodic status updates");
            }
        }

        public void Dispose()
        {
            _statusUpdateTimer?.Dispose();
            
            // Clean up any running processes
            foreach (var kvp in _runningExecutions.ToList())
            {
                try
                {
                    var process = kvp.Value;
                    if (!process.HasExited)
                    {
                        process.Kill(true);
                    }
                    process.Dispose();
                }
                catch { }
            }
            _runningExecutions.Clear();
        }
    }

    /// <summary>
    /// Execution command data structure
    /// </summary>
    public class ExecutionCommand
    {
        [JsonPropertyName("executionId")]
        public string ExecutionId { get; set; } = string.Empty;
        
        [JsonPropertyName("packageId")]
        public string PackageId { get; set; } = string.Empty;
        
        [JsonPropertyName("packageName")]
        public string PackageName { get; set; } = string.Empty;
        
        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;
        
        [JsonPropertyName("tenantSlug")]
        public string TenantSlug { get; set; } = string.Empty;
    }
} 