using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenAutomate.BotAgent.Executor.Models;

namespace OpenAutomate.BotAgent.Executor.Services
{
    public class SimpleTaskExecutor : IDisposable
    {
        private static readonly string DefaultTaskQueuePath = @"C:\ProgramData\OpenAutomate\TaskQueue.json";
        private static readonly string BotScriptsPath = @"C:\ProgramData\OpenAutomate\BotScripts";
        private static readonly string VirtualEnvName = "botvenv";

        private readonly ILogger _logger;
        private readonly object _lockObject = new object();
        private readonly SemaphoreSlim _executorSemaphore = new SemaphoreSlim(1, 1); // Only allow one executor at a time
        private bool _disposed = false;
        private string _taskQueuePath;

        // Standardized log message templates
        private static class LogMessages
        {
            public const string TaskExecutionStarted = "Executing task {TaskId} for package {PackageName} v{Version}";
            public const string TaskExecutionCompleted = "Task {TaskId} completed successfully";
            public const string TaskExecutionFailed = "Task {TaskId} failed: {ErrorMessage}";
            public const string ScriptPathNotFound = "Script path does not exist: {ScriptPath}";
            public const string TaskStatusUpdated = "Task {TaskId} status updated to: {Status}";
            public const string VirtualEnvironmentSetup = "Setting up virtual environment for script path: {ScriptPath}";
            public const string PythonBotExecutionStarted = "Starting Python bot execution for script path: {ScriptPath}";
            public const string TaskQueuePathSet = "Using custom task queue path: {TaskQueuePath}";
        }

        public SimpleTaskExecutor(ILogger logger)
        {
            _logger = logger;
            _taskQueuePath = DefaultTaskQueuePath;
            EnsureDirectoryExists();
        }

        /// <summary>
        /// Sets a custom task queue path for this executor instance
        /// </summary>
        public void SetCustomTaskQueuePath(string taskQueuePath)
        {
            _taskQueuePath = taskQueuePath ?? DefaultTaskQueuePath;
            _logger.LogInformation(LogMessages.TaskQueuePathSet, _taskQueuePath);
        }

        /// <summary>
        /// Writes a message to both logger and console (if available)
        /// </summary>
        private void WriteMessage(string message, LogLevel level = LogLevel.Information, ConsoleColor color = ConsoleColor.White)
        {
            // Log to file/standard logging
            switch (level)
            {
                case LogLevel.Information:
                    _logger.LogInformation(message);
                    break;
                case LogLevel.Warning:
                    _logger.LogWarning(message);
                    break;
                case LogLevel.Error:
                    _logger.LogError(message);
                    break;
                case LogLevel.Debug:
                    _logger.LogDebug(message);
                    break;
            }

            // Also write to console if available
            try
            {
                Console.ForegroundColor = color;
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
                Console.ResetColor();
            }
            catch
            {
                // Ignore console errors (e.g., when redirected)
            }
        }

        public async Task<bool> ProcessNextTaskAsync()
        {
            EnsureDirectoryExists();

            // Try to acquire the semaphore with a short timeout to prevent multiple executors running
            var semaphoreAcquired = await _executorSemaphore.WaitAsync(100);

            if (!semaphoreAcquired)
            {
                _logger.LogInformation("Another executor instance is already running, exiting");
                return false;
            }

            try
            {
                _logger.LogDebug("Acquired executor semaphore");

                // Look for pending tasks
                var task = GetNextPendingTask();
                if (task == null)
                {
                    _logger.LogDebug("No pending tasks found");
                    return false;
                }

                _logger.LogInformation(LogMessages.TaskExecutionStarted, task.TaskId, task.PackageName, task.Version);

                // Execute the task
                var success = await ExecuteTaskAsync(task);

                if (success)
                {
                    _logger.LogInformation(LogMessages.TaskExecutionCompleted, task.TaskId);
                }
                else
                {
                    _logger.LogError(LogMessages.TaskExecutionFailed, task.TaskId, "Execution failed");
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during task processing");
                return false;
            }
            finally
            {
                // Always release the semaphore if we acquired it
                try
                {
                    _executorSemaphore.Release();
                    _logger.LogDebug("Released executor semaphore");
                }
                catch (ObjectDisposedException ex)
                {
                    _logger.LogDebug(ex, "Semaphore was already disposed, skipping release");
                }
                catch (SemaphoreFullException ex)
                {
                    _logger.LogDebug(ex, "Semaphore was already at maximum count, skipping release");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Unexpected error releasing semaphore");
                }
            }
        }

        private async Task<bool> ExecuteTaskAsync(BotTask task)
        {
            WriteMessage($"🔄 Executing task {task.TaskId} for package {task.PackageName} v{task.Version}", LogLevel.Information, ConsoleColor.Cyan);

            // Validate script path exists
            if (!Directory.Exists(task.ScriptPath))
            {
                var errorMsg = $"Script path does not exist: {task.ScriptPath}";
                WriteMessage($"❌ {errorMsg}", LogLevel.Error, ConsoleColor.Red);
                _logger.LogError(LogMessages.ScriptPathNotFound, task.ScriptPath);
                UpdateTaskStatus(task, "Failed", errorMessage: errorMsg);
                return false;
            }

            // Update task to running
            UpdateTaskStatus(task, "Running", Process.GetCurrentProcess().Id);
            WriteMessage($"▶️ Task {task.TaskId} is now running", LogLevel.Information, ConsoleColor.Green);

            try
            {
                // Ensure virtual environment
                WriteMessage("🔧 Setting up virtual environment...", LogLevel.Information, ConsoleColor.Yellow);
                _logger.LogInformation(LogMessages.VirtualEnvironmentSetup, task.ScriptPath);
                await EnsureVirtualEnvironmentAsync(task.ScriptPath);

                // Execute Python bot using bot.py (from bot-example structure)
                WriteMessage("🐍 Starting Python bot execution...", LogLevel.Information, ConsoleColor.Magenta);
                _logger.LogInformation(LogMessages.PythonBotExecutionStarted, task.ScriptPath);
                var success = await ExecutePythonBotAsync(task.ScriptPath);

                // Update final status
                var finalStatus = success ? "Completed" : "Failed";
                UpdateTaskStatus(task, finalStatus);
                _logger.LogInformation(LogMessages.TaskStatusUpdated, task.TaskId, finalStatus);
                
                if (success)
                {
                    WriteMessage($"✅ Task {task.TaskId} completed successfully!", LogLevel.Information, ConsoleColor.Green);
                }
                else
                {
                    WriteMessage($"❌ Task {task.TaskId} failed!", LogLevel.Error, ConsoleColor.Red);
                }

                // Cleanup script folder after execution
                WriteMessage("🧹 Cleaning up script folder...", LogLevel.Information, ConsoleColor.Yellow);
                await CleanupScriptFolderAsync(task.ScriptPath);

                // Remove the task from queue after successful cleanup
                RemoveCompletedTask(task);

                return success;
            }
            catch (Exception ex)
            {
                WriteMessage($"💥 Error executing task {task.TaskId}: {ex.Message}", LogLevel.Error, ConsoleColor.Red);
                _logger.LogError(ex, LogMessages.TaskExecutionFailed, task.TaskId, ex.Message);
                UpdateTaskStatus(task, "Failed", errorMessage: ex.Message);
                
                // Cleanup on failure too
                await CleanupScriptFolderAsync(task.ScriptPath);
                
                // Remove the failed task from queue after cleanup
                RemoveCompletedTask(task);
                
                return false;
            }
        }

        private async Task CleanupScriptFolderAsync(string scriptPath)
        {
            try
            {
                if (Directory.Exists(scriptPath))
                {
                    // Wait a bit for any file handles to be released
                    await Task.Delay(1000);
                    
                    Directory.Delete(scriptPath, true);
                    _logger.LogInformation("Cleaned up script folder: {ScriptPath}", scriptPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanup script folder: {ScriptPath}", scriptPath);
            }
        }

        private void ForceGarbageCollection()
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                _logger.LogDebug("Forced garbage collection completed");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during forced garbage collection");
            }
        }

        private BotTask GetNextPendingTask()
        {
            try
            {
                if (!File.Exists(_taskQueuePath))
                    return null;

                var queue = LoadTaskQueue();
                return queue?.Tasks?.FirstOrDefault(t => t.Status == "Pending");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting next pending task from {TaskQueuePath}", _taskQueuePath);
                return null;
            }
        }

        private void UpdateTaskStatus(BotTask task, string status, int? pid = null, string errorMessage = null)
        {
            try
            {
                lock (_lockObject)
                {
                    var queue = LoadTaskQueue();
                    if (queue?.Tasks == null) return;

                    var existingTask = queue.Tasks.FirstOrDefault(t => t.TaskId == task.TaskId);
                    if (existingTask != null)
                    {
                        existingTask.Status = status;
                        existingTask.ProcessId = pid;
                        existingTask.ErrorMessage = errorMessage;
                        existingTask.UpdatedAt = DateTime.UtcNow;

                        if (status == "Running")
                            existingTask.StartedAt = DateTime.UtcNow;
                        else if (status == "Completed" || status == "Failed")
                            existingTask.CompletedAt = DateTime.UtcNow;

                        SaveTaskQueueSafely(queue);
                        _logger.LogDebug(LogMessages.TaskStatusUpdated, task.TaskId, status);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating task status for task {TaskId}", task.TaskId);
            }
        }

        private void RemoveCompletedTask(BotTask task)
        {
            try
            {
                lock (_lockObject)
                {
                    var queue = LoadTaskQueue();
                    if (queue?.Tasks == null) return;

                    queue.Tasks.RemoveAll(t => t.TaskId == task.TaskId);
                    SaveTaskQueueSafely(queue);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing completed task {TaskId}", task.TaskId);
            }
        }

        private TaskQueue LoadTaskQueue()
        {
            try
            {
                if (!File.Exists(_taskQueuePath))
                    return new TaskQueue { Tasks = new List<BotTask>() };

                var json = File.ReadAllText(_taskQueuePath);
                return JsonSerializer.Deserialize<TaskQueue>(json) ?? new TaskQueue { Tasks = new List<BotTask>() };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading task queue from {TaskQueuePath}", _taskQueuePath);
                return new TaskQueue { Tasks = new List<BotTask>() };
            }
        }

        private void SaveTaskQueueSafely(TaskQueue queue)
        {
            try
            {
                var tempPath = _taskQueuePath + ".tmp";
                var json = JsonSerializer.Serialize(queue, new JsonSerializerOptions { WriteIndented = true });
                
                File.WriteAllText(tempPath, json);
                
                if (File.Exists(_taskQueuePath))
                    File.Delete(_taskQueuePath);
                    
                File.Move(tempPath, _taskQueuePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving task queue to {TaskQueuePath}", _taskQueuePath);
            }
        }

        public void AddTask(string packageId, string packageName, string version, string executionId = null)
        {
            var scriptPath = Path.Combine(BotScriptsPath, packageName, version);
            AddTask(packageId, packageName, version, executionId, scriptPath);
        }

        public void AddTask(string packageId, string packageName, string version, string executionId, string scriptPath)
        {
            try
            {
                lock (_lockObject)
                {
                    var queue = LoadTaskQueue();
                    
                    var task = new BotTask
                    {
                        TaskId = Guid.NewGuid().ToString(),
                        ExecutionId = executionId,
                        PackageId = packageId,
                        PackageName = packageName,
                        Version = version,
                        ScriptPath = scriptPath,
                        Status = "Pending",
                        CreatedAt = DateTime.UtcNow
                    };

                    queue.Tasks.Add(task);
                    SaveTaskQueueSafely(queue);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding task for package {PackageName} v{Version}", packageName, version);
            }
        }

        private async Task EnsureVirtualEnvironmentAsync(string scriptPath)
        {
            try
            {
                var venvPath = Path.Combine(scriptPath, VirtualEnvName);
                var pythonExePath = Path.Combine(venvPath, "Scripts", "python.exe");

                if (!File.Exists(pythonExePath))
                {
                    _logger.LogInformation("Creating virtual environment at: {VenvPath}", venvPath);
                    await CreateVirtualEnvironmentAsync(venvPath);
                }

                var requirementsPath = Path.Combine(scriptPath, "requirements.txt");
                if (File.Exists(requirementsPath))
                {
                    await InstallRequirementsAsync(pythonExePath, requirementsPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ensuring virtual environment for script path: {ScriptPath}", scriptPath);
                throw;
            }
        }

        private async Task CreateVirtualEnvironmentAsync(string venvPath)
        {
            var result = await ExecuteCommandAsync("python", $"-m venv \"{venvPath}\"");
            if (result.ExitCode != 0)
            {
                throw new Exception($"Failed to create virtual environment: {result.StandardError}");
            }
        }

        private async Task InstallRequirementsAsync(string pythonExePath, string requirementsPath)
        {
            var result = await ExecuteCommandAsync(pythonExePath, $"-m pip install -r \"{requirementsPath}\"");
            if (result.ExitCode != 0)
            {
                _logger.LogWarning("Failed to install requirements: {Error}", result.StandardError);
            }
        }

        private async Task<bool> ExecutePythonBotAsync(string scriptPath)
        {
            try
            {
                var venvPath = Path.Combine(scriptPath, VirtualEnvName);
                var pythonExePath = Path.Combine(venvPath, "Scripts", "python.exe");
                var botScriptPath = Path.Combine(scriptPath, "bot.py");

                if (!File.Exists(pythonExePath))
                {
                    _logger.LogError("Python executable not found: {PythonExePath}", pythonExePath);
                    return false;
                }

                if (!File.Exists(botScriptPath))
                {
                    _logger.LogError("Bot script not found: {BotScriptPath}", botScriptPath);
                    return false;
                }

                var result = await ExecuteCommandAsync(
                    pythonExePath,
                    $"\"{botScriptPath}\"",
                    scriptPath,
                    300000 // 5 minutes timeout
                );

                if (result.ExitCode == 0)
                {
                    _logger.LogInformation("Python bot executed successfully");
                    if (!string.IsNullOrEmpty(result.StandardOutput))
                    {
                        _logger.LogInformation("Bot output: {Output}", result.StandardOutput);
                    }
                    return true;
                }
                else
                {
                    _logger.LogError("Python bot execution failed with exit code: {ExitCode}", result.ExitCode);
                    if (!string.IsNullOrEmpty(result.StandardError))
                    {
                        _logger.LogError("Bot error: {Error}", result.StandardError);
                    }
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing Python bot");
                return false;
            }
        }

        private async Task<CommandResult> ExecuteCommandAsync(
            string executable, 
            string arguments, 
            string workingDirectory = null, 
            int timeoutMs = 60000)
        {
            try
            {
                using var process = new Process();
                process.StartInfo.FileName = executable;
                process.StartInfo.Arguments = arguments;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.CreateNoWindow = true;

                if (!string.IsNullOrEmpty(workingDirectory))
                {
                    process.StartInfo.WorkingDirectory = workingDirectory;
                }

                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();

                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        outputBuilder.AppendLine(e.Data);
                        _logger.LogDebug("Command output: {Output}", e.Data);
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        errorBuilder.AppendLine(e.Data);
                        _logger.LogDebug("Command error: {Error}", e.Data);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
                
                try
                {
                    await process.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Process timed out
                    try
                    {
                        process.Kill(true);
                        _logger.LogWarning("Command timed out and was killed: {Executable} {Arguments}", executable, arguments);
                    }
                    catch (Exception killEx)
                    {
                        _logger.LogError(killEx, "Error killing timed out process");
                    }
                    
                    return new CommandResult
                    {
                        ExitCode = -1,
                        StandardOutput = outputBuilder.ToString(),
                        StandardError = "Command timed out"
                    };
                }

                return new CommandResult
                {
                    ExitCode = process.ExitCode,
                    StandardOutput = outputBuilder.ToString(),
                    StandardError = errorBuilder.ToString()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing command: {Executable} {Arguments}", executable, arguments);
                return new CommandResult
                {
                    ExitCode = -1,
                    StandardOutput = "",
                    StandardError = ex.Message
                };
            }
        }

        private void EnsureDirectoryExists()
        {
            try
            {
                if (!Directory.Exists(BotScriptsPath))
                {
                    Directory.CreateDirectory(BotScriptsPath);
                }

                var taskQueueDir = Path.GetDirectoryName(_taskQueuePath);
                if (!string.IsNullOrEmpty(taskQueueDir) && !Directory.Exists(taskQueueDir))
                {
                    Directory.CreateDirectory(taskQueueDir);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating required directories");
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            try
            {
                _executorSemaphore?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing executor semaphore");
            }

            _disposed = true;
        }
    }

    public class CommandResult
    {
        public int ExitCode { get; set; }
        public string StandardOutput { get; set; } = "";
        public string StandardError { get; set; } = "";
    }
} 