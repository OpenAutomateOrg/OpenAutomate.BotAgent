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
        private static readonly string TaskQueuePath = @"C:\ProgramData\OpenAutomate\TaskQueue.json";
        private static readonly string BotScriptsPath = @"C:\ProgramData\OpenAutomate\BotScripts";
        private static readonly string VirtualEnvName = "botvenv";

        private readonly ILogger _logger;
        private readonly object _lockObject = new object();
        private readonly SemaphoreSlim _executorSemaphore = new SemaphoreSlim(1, 1); // Only allow one executor at a time
        private bool _disposed = false;

        public SimpleTaskExecutor(ILogger logger)
        {
            _logger = logger;
            EnsureDirectoryExists();
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

                _logger.LogInformation("Found pending task {TaskId}, executing", task.TaskId);

                // Execute the task
                var success = await ExecuteTaskAsync(task);

                _logger.LogInformation("Task {TaskId} execution completed with result: {Success}", task.TaskId, success);
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
                await EnsureVirtualEnvironmentAsync(task.ScriptPath);

                // Execute Python bot using bot.py (from bot-example structure)
                WriteMessage("🐍 Starting Python bot execution...", LogLevel.Information, ConsoleColor.Magenta);
                var success = await ExecutePythonBotAsync(task.ScriptPath);

                // Update final status
                var finalStatus = success ? "Completed" : "Failed";
                UpdateTaskStatus(task, finalStatus);
                
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
            finally
            {
                // Force garbage collection after cleanup to free memory
                ForceGarbageCollection();
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
            lock (_lockObject)
            {
                var queue = LoadTaskQueue();
                return queue.Tasks
                    .Where(t => t.Status == "Pending")
                    .OrderBy(t => t.CreatedTime)
                    .FirstOrDefault();
            }
        }

        private void UpdateTaskStatus(BotTask task, string status, int? pid = null, string errorMessage = null)
        {
            lock (_lockObject)
            {
                var queue = LoadTaskQueue();
                
                // Find the actual task in the queue by TaskId and update it
                var queueTask = queue.Tasks.FirstOrDefault(t => t.TaskId == task.TaskId);
                if (queueTask == null)
                {
                    _logger.LogWarning("Task {TaskId} not found in queue for status update", task.TaskId);
                    return;
                }

                // Update the task in the queue
                queueTask.Status = status;
                queueTask.ErrorMessage = errorMessage;
                
                if (pid.HasValue)
                    queueTask.Pid = pid.Value;
                
                if (status == "Running")
                    queueTask.StartTime = DateTime.UtcNow;
                
                if (status is "Completed" or "Failed")
                    queueTask.EndTime = DateTime.UtcNow;

                // Also update the original task object for consistency
                task.Status = status;
                task.ErrorMessage = errorMessage;
                if (pid.HasValue) task.Pid = pid.Value;
                if (status == "Running") task.StartTime = queueTask.StartTime;
                if (status is "Completed" or "Failed") task.EndTime = queueTask.EndTime;

                SaveTaskQueueSafely(queue);
                _logger.LogInformation("Task {TaskId} status updated to {Status}", task.TaskId, status);
            }
        }

        /// <summary>
        /// Removes completed or failed tasks from the queue to prevent accumulation
        /// </summary>
        private void RemoveCompletedTask(BotTask task)
        {
            lock (_lockObject)
            {
                var queue = LoadTaskQueue();
                var taskToRemove = queue.Tasks.FirstOrDefault(t => t.TaskId == task.TaskId);
                
                if (taskToRemove != null)
                {
                    queue.Tasks.Remove(taskToRemove);
                    SaveTaskQueueSafely(queue);
                    _logger.LogInformation("Removed completed task {TaskId} from queue", task.TaskId);
                }
            }
        }

        private TaskQueue LoadTaskQueue()
        {
            try
            {
                if (!File.Exists(TaskQueuePath))
                {
                    var emptyQueue = new TaskQueue();
                    SaveTaskQueueSafely(emptyQueue);
                    return emptyQueue;
                }

                var json = File.ReadAllText(TaskQueuePath);
                return JsonSerializer.Deserialize<TaskQueue>(json) ?? new TaskQueue();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading task queue, creating new one");
                return new TaskQueue();
            }
        }

        private void SaveTaskQueueSafely(TaskQueue queue)
        {
            try
            {
                queue.LastUpdated = DateTime.UtcNow;
                var tempPath = TaskQueuePath + ".tmp";
                
                var json = JsonSerializer.Serialize(queue, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(tempPath, json);
                
                if (File.Exists(TaskQueuePath))
                    File.Delete(TaskQueuePath);
                    
                File.Move(tempPath, TaskQueuePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving task queue");
                throw;
            }
        }

        public void AddTask(string packageId, string packageName, string version, string executionId = null)
        {
            var scriptPath = Path.Combine(BotScriptsPath, $"{packageName}_{version}_{Guid.NewGuid():N}");
            AddTask(packageId, packageName, version, executionId, scriptPath);
        }

        public void AddTask(string packageId, string packageName, string version, string executionId, string scriptPath)
        {
            var task = new BotTask
            {
                ScriptPath = scriptPath,
                PackageId = packageId,
                PackageName = packageName,
                Version = version,
                ExecutionId = executionId
            };

            lock (_lockObject)
            {
                var queue = LoadTaskQueue();
                queue.Tasks.Add(task);
                SaveTaskQueueSafely(queue);
            }

            _logger.LogInformation("Task {TaskId} added to queue for package {PackageName} v{Version} at {ScriptPath}", 
                task.TaskId, packageName, version, scriptPath);
        }

        private async Task EnsureVirtualEnvironmentAsync(string scriptPath)
        {
            var venvPath = Path.Combine(scriptPath, VirtualEnvName);
            var pythonExePath = Path.Combine(venvPath, "Scripts", "python.exe");

            // Check if virtual environment exists
            if (!File.Exists(pythonExePath))
            {
                _logger.LogInformation("Creating virtual environment at {VenvPath}", venvPath);
                await CreateVirtualEnvironmentAsync(venvPath);
            }

            // Install requirements if they exist
            var requirementsPath = Path.Combine(scriptPath, "requirements.txt");
            if (File.Exists(requirementsPath))
            {
                _logger.LogInformation("Installing requirements from {RequirementsPath}", requirementsPath);
                await InstallRequirementsAsync(pythonExePath, requirementsPath);
            }
        }

        private async Task CreateVirtualEnvironmentAsync(string venvPath)
        {
            var result = await ExecuteCommandAsync("python", $"-m venv \"{venvPath}\"");
            if (!result.Success)
            {
                throw new InvalidOperationException($"Failed to create virtual environment: {result.Error}");
            }
        }

        private async Task InstallRequirementsAsync(string pythonExePath, string requirementsPath)
        {
            var args = $"-m pip install -r \"{requirementsPath}\" --trusted-host=pypi.python.org --trusted-host=pypi.org --trusted-host=files.pythonhosted.org";
            var result = await ExecuteCommandAsync(pythonExePath, args);
            
            if (!result.Success)
            {
                _logger.LogWarning("Failed to install requirements: {Error}", result.Error);
                // Don't throw - let the bot try to run anyway
            }
        }

        private async Task<bool> ExecutePythonBotAsync(string scriptPath)
        {
            var venvPath = Path.Combine(scriptPath, VirtualEnvName);
            var pythonExePath = Path.Combine(venvPath, "Scripts", "python.exe");
            
            // Look for bot.py (from bot-example structure) or fallback to main.py
            var botScriptPath = Path.Combine(scriptPath, "bot.py");
            var mainScriptPath = Path.Combine(scriptPath, "main.py");
            
            string scriptToRun;
            if (File.Exists(botScriptPath))
            {
                scriptToRun = "bot.py";
                WriteMessage("📄 Found bot.py, using bot-example structure", LogLevel.Information, ConsoleColor.Cyan);
            }
            else if (File.Exists(mainScriptPath))
            {
                scriptToRun = "main.py";
                WriteMessage("📄 Found main.py, using template structure", LogLevel.Information, ConsoleColor.Cyan);
            }
            else
            {
                WriteMessage($"❌ Neither bot.py nor main.py found at {scriptPath}", LogLevel.Error, ConsoleColor.Red);
                return false;
            }

            WriteMessage($"🚀 Executing Python bot: {pythonExePath} {scriptToRun}", LogLevel.Information, ConsoleColor.Green);

            var result = await ExecuteCommandAsync(
                pythonExePath, 
                scriptToRun, 
                scriptPath,
                timeoutMs: 600000 // 10 minutes timeout for bot execution
            );

            if (result.Success)
            {
                WriteMessage("🎉 Python bot completed successfully", LogLevel.Information, ConsoleColor.Green);
                return true;
            }
            else
            {
                WriteMessage($"💀 Python bot failed with exit code {result.ExitCode}: {result.Error}", LogLevel.Error, ConsoleColor.Red);
                return false;
            }
        }

        private async Task<CommandResult> ExecuteCommandAsync(
            string executable, 
            string arguments, 
            string workingDirectory = null, 
            int timeoutMs = 60000)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory
            };

            // Add UTF-8 encoding environment variables to support Unicode output
            startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
            startInfo.Environment["PYTHONLEGACYWINDOWSSTDIO"] = "0";
            startInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
            startInfo.StandardErrorEncoding = System.Text.Encoding.UTF8;

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();
            Process process = null;

            try
            {
                process = new Process { StartInfo = startInfo };
                
                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        outputBuilder.AppendLine(e.Data);
                        _logger.LogInformation("[stdout] {Output}", e.Data);
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        errorBuilder.AppendLine(e.Data);
                        _logger.LogWarning("[stderr] {Error}", e.Data);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var completed = await Task.Run(() => process.WaitForExit(timeoutMs));

                if (!completed)
                {
                    _logger.LogWarning("Process timed out after {TimeoutMs}ms, killing process", timeoutMs);
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(entireProcessTree: true); // Kill entire process tree
                            process.WaitForExit(5000); // Wait up to 5 seconds for cleanup
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to kill timed out process");
                    }
                    
                    return new CommandResult
                    {
                        Success = false,
                        ExitCode = -1,
                        Error = $"Process timed out after {timeoutMs}ms"
                    };
                }

                // Ensure async output reading is complete
                process.WaitForExit();

                return new CommandResult
                {
                    Success = process.ExitCode == 0,
                    ExitCode = process.ExitCode,
                    Output = outputBuilder.ToString(),
                    Error = errorBuilder.ToString()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing command: {Executable} {Arguments}", executable, arguments);
                return new CommandResult
                {
                    Success = false,
                    ExitCode = -1,
                    Error = ex.Message
                };
            }
            finally
            {
                // Ensure proper cleanup
                try
                {
                    if (process != null)
                    {
                        if (!process.HasExited)
                        {
                            process.Kill(entireProcessTree: true);
                            process.WaitForExit(2000);
                        }
                        process.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error during process cleanup");
                }
            }
        }

        private void EnsureDirectoryExists()
        {
            var directory = Path.GetDirectoryName(TaskQueuePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                _logger.LogInformation("Created directory {Directory}", directory);
            }
            
            if (!Directory.Exists(BotScriptsPath))
            {
                Directory.CreateDirectory(BotScriptsPath);
                _logger.LogInformation("Created bot scripts directory {Directory}", BotScriptsPath);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            // Dispose the semaphore
            try
            {
                _executorSemaphore?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error disposing semaphore (non-critical)");
            }
        }
    }
} 