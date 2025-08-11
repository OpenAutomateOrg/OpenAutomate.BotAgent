using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenAutomate.BotAgent.Executor.Services;
using Serilog;
using System.IO;
using System.Text.Json;

namespace OpenAutomate.BotAgent.Executor
{
    internal class Program
    {
        public static async Task<int> Main(string[] args)
        {
            // Set console title and colors for better visibility
            try
            {
                Console.Title = "OpenAutomate Bot Executor";
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("=".PadRight(60, '='));
                Console.WriteLine("🚀 OpenAutomate Bot Agent Executor");
                Console.WriteLine("=".PadRight(60, '='));
                Console.ResetColor();
            }
            catch
            {
                // Ignore console setup errors (e.g., when running as service)
            }

            // Parse command line arguments
            string taskQueuePath = null;
            string executionId = null;
            string tenantSlug = null;
            string apiBaseUrl = null;
            string machineKey = null;
            
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--task-queue" when i + 1 < args.Length:
                        taskQueuePath = args[i + 1];
                        i++; // Skip next argument as it's the value
                        break;
                    case "--execution-id" when i + 1 < args.Length:
                        executionId = args[i + 1];
                        i++; // Skip next argument as it's the value
                        break;
                    case "--tenant-slug" when i + 1 < args.Length:
                        tenantSlug = args[i + 1];
                        i++; // Skip next argument as it's the value
                        break;
                    case "--api-base-url" when i + 1 < args.Length:
                        apiBaseUrl = args[i + 1];
                        i++; // Skip next argument as it's the value
                        break;
                    case "--machine-key" when i + 1 < args.Length:
                        machineKey = args[i + 1];
                        i++; // Skip next argument as it's the value
                        break;
                }
            }

            // If specific task queue is provided, use it; otherwise fall back to default behavior
            if (!string.IsNullOrEmpty(taskQueuePath))
            {
                return await ProcessSpecificTaskQueue(taskQueuePath, executionId, tenantSlug, apiBaseUrl, machineKey);
            }
            else
            {
                return await ProcessDefaultTaskQueue();
            }
        }

        /// <summary>
        /// Processes a specific task queue file (new dedicated execution model)
        /// </summary>
        private static async Task<int> ProcessSpecificTaskQueue(string taskQueuePath, string executionId, string tenantSlug, string apiBaseUrl, string machineKey)
        {
            // Initialize Serilog logger for file logging with execution ID
            var serilogLogger = Logger.Initialize(executionId);
            
            // Also create Microsoft.Extensions.Logging logger for compatibility (avoid Console provider to prevent runtime assembly issues)
            var loggerFactory = LoggerFactory.Create(builder =>
                builder.AddSerilog(serilogLogger)
                       .SetMinimumLevel(LogLevel.Information));
            var logger = loggerFactory.CreateLogger<Program>();

            DateTime startTime = DateTime.UtcNow;
            DateTime? endTime = null;
            string finalStatus = "Failed";
            string packageName = "Unknown";
            string version = "Unknown";
            string scriptPath = null;
            string executorLogPath = null;

            try
            {
                WriteConsoleMessage($"🎯 Processing dedicated task queue: {Path.GetFileName(taskQueuePath)}", ConsoleColor.Green);
                WriteConsoleMessage($"📋 Execution ID: {executionId ?? "unknown"}", ConsoleColor.Cyan);
                
                logger.LogInformation("Bot Agent Executor started for execution: {ExecutionId}", executionId ?? "unknown");
                logger.LogInformation("Using task queue: {TaskQueuePath}", taskQueuePath);

                if (!File.Exists(taskQueuePath))
                {
                    WriteConsoleMessage($"❌ Task queue file not found: {taskQueuePath}", ConsoleColor.Red);
                    logger.LogError("Task queue file not found: {TaskQueuePath}", taskQueuePath);
                    WriteConsoleMessage("Press any key to close...", ConsoleColor.Yellow);
                    if (Console.IsInputRedirected == false)
                    {
                        Console.ReadKey(true);
                    }
                    return -1;
                }

                // Extract task information for log aggregation
                var taskInfo = ExtractTaskInformation(taskQueuePath);
                if (taskInfo != null)
                {
                    packageName = taskInfo.PackageName ?? "Unknown";
                    version = taskInfo.Version ?? "Unknown";
                    scriptPath = taskInfo.ScriptPath;
                }

                // Determine executor log path
                executorLogPath = Logger.GetLogFilePath(executionId);

                var executor = new SimpleTaskExecutor(logger);
                
                // Set custom task queue path for this execution
                executor.SetCustomTaskQueuePath(taskQueuePath);
                
                // Process all tasks in the dedicated queue
                int tasksProcessed = 0;
                bool hasWork;
                
                do
                {
                    hasWork = await executor.ProcessNextTaskAsync();
                    if (hasWork)
                    {
                        tasksProcessed++;
                        WriteConsoleMessage($"✅ Task {tasksProcessed} completed successfully!", ConsoleColor.Green);
                    }
                } while (hasWork);

                endTime = DateTime.UtcNow;

                if (tasksProcessed > 0)
                {
                    finalStatus = "Completed";
                    WriteConsoleMessage($"🎉 All {tasksProcessed} task(s) processing completed successfully!", ConsoleColor.Green);
                    logger.LogInformation("All {TasksProcessed} task(s) processing completed", tasksProcessed);
                    
                    WriteConsoleMessage("Execution completed! Processing logs...", ConsoleColor.Yellow);
                    
                    return 0; // Success
                }
                else
                {
                    finalStatus = "Failed";
                    WriteConsoleMessage("ℹ️ No tasks to process in the queue", ConsoleColor.Yellow);
                    logger.LogInformation("No tasks to process in the queue");
                    WriteConsoleMessage("Processing logs...", ConsoleColor.Yellow);
                    return 1; // No work
                }
            }
            catch (Exception ex)
            {
                endTime = DateTime.UtcNow;
                finalStatus = "Failed";
                WriteConsoleMessage($"❌ Fatal error: {ex.Message}", ConsoleColor.Red);
                logger.LogError(ex, "Fatal error in executor");
                
                return -1; // Error
            }
            finally
            {
                try
                {
                    // Close and flush the logger before log aggregation
                    Logger.CloseAndFlush();
                    
                    // Dispose the logger factory to ensure all log files are closed
                    loggerFactory.Dispose();

                    // Wait a bit to ensure all file handles are properly released
                    await Task.Delay(2000);
                    
                    // Create a new logger factory for log processing
                    var logProcessingFactory = LoggerFactory.Create(builder =>
                        builder.SetMinimumLevel(LogLevel.Information));
                    
                    try
                    {
                        // Perform log aggregation and upload
                        await HandleLogAggregationAndUpload(
                            logProcessingFactory,
                            executionId,
                            executorLogPath,
                            scriptPath,
                            packageName,
                            version,
                            startTime,
                            endTime,
                            finalStatus,
                            tenantSlug,
                            apiBaseUrl,
                            machineKey);
                    }
                    finally
                    {
                        logProcessingFactory.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    WriteConsoleMessage($"⚠️ Warning: Log processing failed: {ex.Message}", ConsoleColor.Yellow);
                    // Don't fail the entire execution due to log processing issues
                }
                finally
                {
                    // Keep console open for a moment to see the result
                    WriteConsoleMessage("Closing in 3 seconds...", ConsoleColor.Yellow);
                    await Task.Delay(3000);
                }
            }
        }

        /// <summary>
        /// Handles log aggregation and upload after execution completion
        /// </summary>
        private static async Task HandleLogAggregationAndUpload(
            ILoggerFactory loggerFactory,
            string executionId,
            string executorLogPath,
            string scriptPath,
            string packageName,
            string version,
            DateTime startTime,
            DateTime? endTime,
            string finalStatus,
            string tenantSlug,
            string apiBaseUrl,
            string machineKey)
        {
            // Only proceed if we have the required parameters for upload
            if (string.IsNullOrEmpty(executionId) || 
                string.IsNullOrEmpty(tenantSlug) || 
                string.IsNullOrEmpty(apiBaseUrl) || 
                string.IsNullOrEmpty(machineKey))
            {
                WriteConsoleMessage("⚠️ Log upload skipped - missing required parameters", ConsoleColor.Yellow);
                return;
            }

            try
            {
                WriteConsoleMessage("📋 Aggregating execution logs...", ConsoleColor.Cyan);

                // Create loggers for the log processing components
                var aggregatorLogger = loggerFactory.CreateLogger<LogAggregator>();
                var uploaderLogger = loggerFactory.CreateLogger<LogUploader>();

                // Aggregate logs
                var logAggregator = new LogAggregator(aggregatorLogger);
                var comprehensiveLogPath = await logAggregator.AggregateLogsAsync(
                    executionId,
                    executorLogPath,
                    scriptPath,
                    packageName,
                    version,
                    startTime,
                    endTime,
                    finalStatus);

                WriteConsoleMessage($"✅ Log aggregation completed: {Path.GetFileName(comprehensiveLogPath)}", ConsoleColor.Green);

                // Upload logs
                WriteConsoleMessage("📤 Uploading comprehensive logs...", ConsoleColor.Cyan);
                
                var logUploader = new LogUploader(uploaderLogger);
                var uploadSuccess = await logUploader.UploadLogWithRetryAsync(
                    apiBaseUrl,
                    tenantSlug,
                    executionId,
                    comprehensiveLogPath,
                    machineKey,
                    maxRetries: 3);

                if (uploadSuccess)
                {
                    WriteConsoleMessage("✅ Log upload completed successfully!", ConsoleColor.Green);
                }
                else
                {
                    WriteConsoleMessage("❌ Log upload failed after retries", ConsoleColor.Red);
                }

                // Clean up the temporary comprehensive log file
                try
                {
                    if (File.Exists(comprehensiveLogPath))
                    {
                        File.Delete(comprehensiveLogPath);
                    }
                }
                catch
                {
                    // Ignore cleanup errors
                }

                // Dispose the uploader
                logUploader.Dispose();
            }
            catch (Exception ex)
            {
                WriteConsoleMessage($"❌ Log processing error: {ex.Message}", ConsoleColor.Red);
                // Log the error but don't throw - we don't want log processing to fail the execution
            }
        }

        /// <summary>
        /// Extracts task information from the task queue file
        /// </summary>
        private static TaskInformation ExtractTaskInformation(string taskQueuePath)
        {
            try
            {
                var json = File.ReadAllText(taskQueuePath);
                using var document = JsonDocument.Parse(json);
                
                if (document.RootElement.TryGetProperty("Tasks", out var tasksElement))
                {
                    foreach (var task in tasksElement.EnumerateArray())
                    {
                        return new TaskInformation
                        {
                            PackageName = task.TryGetProperty("PackageName", out var pkgName) ? pkgName.GetString() : null,
                            Version = task.TryGetProperty("Version", out var ver) ? ver.GetString() : null,
                            ScriptPath = task.TryGetProperty("ScriptPath", out var path) ? path.GetString() : null
                        };
                    }
                }
                
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Processes the default global task queue (legacy behavior)
        /// </summary>
        private static async Task<int> ProcessDefaultTaskQueue()
        {
            // Try to get execution ID from pending tasks for better logging
            var executionId = GetPendingExecutionId();
            
            // Initialize Serilog logger for file logging with execution ID
            var serilogLogger = Logger.Initialize(executionId);
            
            // Also create Microsoft.Extensions.Logging logger for compatibility
            var loggerFactory = LoggerFactory.Create(builder =>
                builder.AddSerilog(serilogLogger)
                       .SetMinimumLevel(LogLevel.Information));
            var logger = loggerFactory.CreateLogger<Program>();

            try
            {
                // Enhanced console output
                WriteConsoleMessage($"Starting execution for: {executionId ?? "unknown"}", ConsoleColor.Green);
                logger.LogInformation("Bot Agent Executor started for execution: {ExecutionId}", executionId ?? "unknown");

                var executor = new SimpleTaskExecutor(logger);
                
                // Process all pending tasks in a loop
                int tasksProcessed = 0;
                bool hasWork;
                
                do
                {
                    hasWork = await executor.ProcessNextTaskAsync();
                    if (hasWork)
                    {
                        tasksProcessed++;
                        WriteConsoleMessage($"✅ Task {tasksProcessed} completed successfully!", ConsoleColor.Green);
                    }
                } while (hasWork);

                if (tasksProcessed > 0)
                {
                    WriteConsoleMessage($"🎉 All {tasksProcessed} task(s) processing completed successfully!", ConsoleColor.Green);
                    logger.LogInformation("All {TasksProcessed} task(s) processing completed", tasksProcessed);
                    
                    // Keep console open for a moment to see the result
                    WriteConsoleMessage("Press any key to close...", ConsoleColor.Yellow);
                    if (Console.IsInputRedirected == false)
                    {
                        Console.ReadKey(true);
                    }
                    
                    return 0; // Success
                }
                else
                {
                    WriteConsoleMessage("ℹ️ No tasks to process", ConsoleColor.Yellow);
                    logger.LogInformation("No tasks to process");
                    return 1; // No work
                }
            }
            catch (Exception ex)
            {
                WriteConsoleMessage($"❌ Fatal error: {ex.Message}", ConsoleColor.Red);
                logger.LogError(ex, "Fatal error in executor");
                
                // Keep console open to see the error
                WriteConsoleMessage("Press any key to close...", ConsoleColor.Yellow);
                if (Console.IsInputRedirected == false)
                {
                    Console.ReadKey(true);
                }
                
                return -1; // Error
            }
            finally
            {
                // Close and flush the logger
                Logger.CloseAndFlush();
                loggerFactory.Dispose();
            }
        }

        /// <summary>
        /// Writes a colored message to the console if possible
        /// </summary>
        private static void WriteConsoleMessage(string message, ConsoleColor color)
        {
            try
            {
                Console.ForegroundColor = color;
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
                Console.ResetColor();
            }
            catch
            {
                // Fallback to regular console write if colors aren't supported
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
            }
        }

        /// <summary>
        /// Gets the execution ID from the first pending task in the queue for logging purposes
        /// </summary>
        private static string GetPendingExecutionId()
        {
            try
            {
                var taskQueuePath = @"C:\ProgramData\OpenAutomate\TaskQueue.json";
                if (!System.IO.File.Exists(taskQueuePath))
                    return null;

                var json = System.IO.File.ReadAllText(taskQueuePath);
                using var document = System.Text.Json.JsonDocument.Parse(json);
                
                if (document.RootElement.TryGetProperty("Tasks", out var tasksElement))
                {
                    foreach (var task in tasksElement.EnumerateArray())
                    {
                        if (task.TryGetProperty("Status", out var status) && 
                            status.GetString() == "Pending" &&
                            task.TryGetProperty("ExecutionId", out var executionId))
                        {
                            return executionId.GetString();
                        }
                    }
                }
                
                return null;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Helper class to hold task information
    /// </summary>
    internal class TaskInformation
    {
        public string PackageName { get; set; }
        public string Version { get; set; }
        public string ScriptPath { get; set; }
    }
}
