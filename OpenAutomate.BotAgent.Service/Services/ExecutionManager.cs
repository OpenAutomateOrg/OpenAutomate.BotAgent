using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenAutomate.BotAgent.Service.Core;
using OpenAutomate.BotAgent.Executor.Services;
using OpenAutomate.BotAgent.Executor.Models;
using System.Text.Json.Serialization;

namespace OpenAutomate.BotAgent.Service.Services
{
    /// <summary>
    /// Enhanced execution manager that handles package download and bot execution
    /// </summary>
    public class ExecutionManager : IExecutionManager
    {
        private readonly ILogger<ExecutionManager> _logger;
        private readonly IConfigurationService _configService;
        private readonly IPackageDownloadService _packageDownloadService;
        private readonly string _executorPath;
        private readonly string _botScriptsPath = @"C:\ProgramData\OpenAutomate\BotScripts";

        public ExecutionManager(
            ILogger<ExecutionManager> logger,
            IConfigurationService configService,
            IPackageDownloadService packageDownloadService)
        {
            _logger = logger;
            _configService = configService;
            _packageDownloadService = packageDownloadService;
            
            // Fix executor path to point to the Executor folder in the installation directory
            // When running from installed location: C:\Program Files (x86)\OpenAutomate.Agent\Service\ (current service location)
            // To:                                   C:\Program Files (x86)\OpenAutomate.Agent\Executor\
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            _logger.LogInformation("=== ExecutionManager Constructor ===");
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
            
            _logger.LogInformation("Relative executor path: {RelativePath}", executorPath);
            
            _executorPath = Path.GetFullPath(executorPath);
            _logger.LogInformation("Resolved executor full path: {ExecutorPath}", _executorPath);
            
            var executorExists = File.Exists(_executorPath);
            _logger.LogInformation("Executor file exists: {ExecutorExists}", executorExists);
            
            if (!executorExists)
            {
                var directory = Path.GetDirectoryName(_executorPath);
                _logger.LogWarning("Executor directory path: {DirectoryPath}", directory);
                _logger.LogWarning("Executor directory exists: {DirectoryExists}", Directory.Exists(directory));
                
                if (Directory.Exists(directory))
                {
                    var files = Directory.GetFiles(directory, "*.exe");
                    _logger.LogWarning("Executable files in directory: {ExecutableFiles}", string.Join(", ", files.Select(Path.GetFileName)));
                }
            }
            
            _logger.LogInformation("=== ExecutionManager Constructor Complete ===");
        }

        public async Task<string> StartExecutionAsync(object executionData)
        {
            _logger.LogInformation("=== StartExecutionAsync called ===");
            SimpleTaskExecutor executor = null;
            try
            {
                _logger.LogInformation("Received execution data: {ExecutionData}", JsonSerializer.Serialize(executionData));
                _logger.LogInformation("Execution data type: {Type}", executionData?.GetType().Name ?? "null");

                ExecutionCommand execution;
                
                // Try to deserialize the execution data directly if it's already the right type
                if (executionData is ExecutionCommand cmd)
                {
                    execution = cmd;
                    _logger.LogInformation("Execution data is already ExecutionCommand type");
                }
                else
                {
                    try
                    {
                        // If it's a JsonElement (from SignalR), convert it properly with case-insensitive options
                        var jsonString = JsonSerializer.Serialize(executionData);
                        _logger.LogInformation("Serialized execution data: {JsonString}", jsonString);
                        
                        var options = new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true,
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                        };
                        
                        _logger.LogInformation("About to deserialize with options...");
                        execution = JsonSerializer.Deserialize<ExecutionCommand>(jsonString, options);
                        _logger.LogInformation("Successfully deserialized ExecutionCommand: ExecutionId={ExecutionId}, PackageId={PackageId}, PackageName={PackageName}, Version={Version}", 
                            execution?.ExecutionId, execution?.PackageId, execution?.PackageName, execution?.Version);
                    }
                    catch (Exception deserEx)
                    {
                        _logger.LogError(deserEx, "Failed to deserialize execution data: {ExecutionData}", JsonSerializer.Serialize(executionData));
                        return string.Empty;
                    }
                }

                if (execution == null)
                {
                    _logger.LogError("Failed to deserialize execution data - result is null");
                    return string.Empty;
                }
                
                if (string.IsNullOrEmpty(execution.ExecutionId) || string.IsNullOrEmpty(execution.PackageId))
                {
                    _logger.LogError("Invalid execution data - missing ExecutionId or PackageId. ExecutionId: {ExecutionId}, PackageId: {PackageId}", 
                        execution.ExecutionId, execution.PackageId);
                    return string.Empty;
                }

                _logger.LogInformation("Starting execution {ExecutionId} for package {PackageName} v{Version}",
                    execution.ExecutionId, execution.PackageName, execution.Version);

                // Download package from backend
                _logger.LogInformation("About to call DownloadPackageAsync for {PackageId} version {Version}", execution.PackageId, execution.Version);
                var downloadPath = await _packageDownloadService.DownloadPackageAsync(
                    execution.PackageId, execution.Version, execution.PackageName, execution.TenantSlug);

                _logger.LogInformation("DownloadPackageAsync completed. Result: {DownloadPath}", downloadPath ?? "null");

                if (string.IsNullOrEmpty(downloadPath))
                {
                    _logger.LogError("Failed to download package {PackageId}", execution.PackageId);
                    return execution.ExecutionId;
                }

                _logger.LogInformation("Package downloaded successfully to: {DownloadPath}", downloadPath);

                // Add task to executor queue using the actual download path
                _logger.LogInformation("Adding task to executor queue");
                executor = new SimpleTaskExecutor(_logger);
                executor.AddTask(execution.PackageId, execution.PackageName, execution.Version, execution.ExecutionId, downloadPath);

                // Trigger executor (fire-and-forget to allow concurrent executions)
                _logger.LogInformation("Triggering executor");
                _ = TriggerExecutorAsync(); // Don't await - allow concurrent executions

                _logger.LogInformation("Execution {ExecutionId} queued successfully", execution.ExecutionId);
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
                // Dispose executor to free resources
                executor?.Dispose();
                
                // Force garbage collection after execution
                try
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error during garbage collection");
                }
            }
        }

        public async Task CancelExecutionAsync(string executionId)
        {
            _logger.LogInformation("Canceling execution {ExecutionId}", executionId);
            // TODO: Implement cancellation logic by updating task status in queue
            await Task.CompletedTask;
        }

        public async Task SendStatusUpdatesAsync()
        {
            // This method can be used to send periodic status updates
            await Task.CompletedTask;
        }

        public async Task<bool> HasActiveExecutionsAsync()
        {
            // Check if there are running tasks in the queue
            var taskQueuePath = @"C:\ProgramData\OpenAutomate\TaskQueue.json";
            if (!File.Exists(taskQueuePath))
                return false;

            try
            {
                var json = await File.ReadAllTextAsync(taskQueuePath);
                var queue = JsonSerializer.Deserialize<TaskQueue>(json);
                return queue?.Tasks?.Any(t => t.Status == "Running") ?? false;
            }
            catch
            {
                return false;
            }
        }

        private async Task TriggerExecutorAsync()
        {
            try
            {
                _logger.LogInformation("=== TriggerExecutorAsync Starting ===");
                _logger.LogInformation("Attempting to execute: {ExecutorPath}", _executorPath);
                _logger.LogInformation("Executor file exists: {ExecutorExists}", File.Exists(_executorPath));
                
                if (!File.Exists(_executorPath))
                {
                    _logger.LogError("Executor file not found at: {ExecutorPath}", _executorPath);
                    var directory = Path.GetDirectoryName(_executorPath);
                    _logger.LogError("Directory exists: {DirectoryExists}", Directory.Exists(directory));
                    if (Directory.Exists(directory))
                    {
                        var files = Directory.GetFiles(directory);
                        _logger.LogInformation("Files in executor directory: {Files}", string.Join(", ", files.Select(Path.GetFileName)));
                    }
                    return;
                }

                // Configuration: Set to true to show console window, false to hide it
                bool showConsoleWindow = true; // Change this to false if you want to hide the console
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = _executorPath,
                    UseShellExecute = !showConsoleWindow, // Use shell execute when showing console
                    RedirectStandardOutput = !showConsoleWindow, // Only redirect when console is hidden
                    RedirectStandardError = !showConsoleWindow,  // Only redirect when console is hidden
                    CreateNoWindow = !showConsoleWindow, // Show window when showConsoleWindow is true
                    WindowStyle = showConsoleWindow ? ProcessWindowStyle.Normal : ProcessWindowStyle.Hidden
                };

                _logger.LogInformation("Process start info - FileName: {FileName}", startInfo.FileName);
                _logger.LogInformation("Process start info - ShowConsoleWindow: {ShowConsole}", showConsoleWindow);
                _logger.LogInformation("Process start info - WorkingDirectory: {WorkingDirectory}", startInfo.WorkingDirectory);

                using var process = new Process { StartInfo = startInfo };
                
                _logger.LogInformation("Starting executor process...");
                process.Start();
                
                _logger.LogInformation("Executor process started with PID: {ProcessId}", process.Id);
                
                if (showConsoleWindow)
                {
                    // When showing console, we can't capture output, but we can see it live
                    _logger.LogInformation("Console window is visible - executor output will be shown in the console window");
                    await process.WaitForExitAsync();
                    _logger.LogInformation("Executor finished with exit code: {ExitCode}", process.ExitCode);
                }
                else
                {
                    // When console is hidden, capture output as before
                    var outputTask = process.StandardOutput.ReadToEndAsync();
                    var errorTask = process.StandardError.ReadToEndAsync();
                    
                    await process.WaitForExitAsync();

                    _logger.LogInformation("Executor finished with exit code: {ExitCode}", process.ExitCode);
                    
                    // Get the output results
                    var output = await outputTask;
                    var error = await errorTask;
                    
                    if (!string.IsNullOrEmpty(output))
                    {
                        _logger.LogInformation("Executor stdout: {Output}", output);
                    }
                    
                    if (!string.IsNullOrEmpty(error))
                    {
                        _logger.LogWarning("Executor stderr: {Error}", error);
                    }
                }
                
                _logger.LogInformation("=== TriggerExecutorAsync Completed ===");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to trigger executor at path: {ExecutorPath}", _executorPath);
                throw;
            }
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