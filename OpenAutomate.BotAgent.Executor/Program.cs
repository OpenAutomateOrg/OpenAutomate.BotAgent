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
                }
            }

            // If specific task queue is provided, use it; otherwise fall back to default behavior
            if (!string.IsNullOrEmpty(taskQueuePath))
            {
                return await ProcessSpecificTaskQueue(taskQueuePath, executionId);
            }
            else
            {
                return await ProcessDefaultTaskQueue();
            }
        }

        /// <summary>
        /// Processes a specific task queue file (new dedicated execution model)
        /// </summary>
        private static async Task<int> ProcessSpecificTaskQueue(string taskQueuePath, string executionId)
        {
            // Initialize Serilog logger for file logging with execution ID
            var serilogLogger = Logger.Initialize(executionId);
            
            // Also create Microsoft.Extensions.Logging logger for compatibility
            var loggerFactory = LoggerFactory.Create(builder =>
                builder.AddConsole()
                       .AddSerilog(serilogLogger)
                       .SetMinimumLevel(LogLevel.Information));
            var logger = loggerFactory.CreateLogger<Program>();

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

                if (tasksProcessed > 0)
                {
                    WriteConsoleMessage($"🎉 All {tasksProcessed} task(s) processing completed successfully!", ConsoleColor.Green);
                    logger.LogInformation("All {TasksProcessed} task(s) processing completed", tasksProcessed);
                    
                    // Keep console open for a moment to see the result
                    WriteConsoleMessage("Execution completed! Closing in 3 seconds...", ConsoleColor.Yellow);
                    await Task.Delay(3000);
                    
                    return 0; // Success
                }
                else
                {
                    WriteConsoleMessage("ℹ️ No tasks to process in the queue", ConsoleColor.Yellow);
                    logger.LogInformation("No tasks to process in the queue");
                    WriteConsoleMessage("Closing in 2 seconds...", ConsoleColor.Yellow);
                    await Task.Delay(2000);
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
                builder.AddConsole()
                       .AddSerilog(serilogLogger)
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
}
