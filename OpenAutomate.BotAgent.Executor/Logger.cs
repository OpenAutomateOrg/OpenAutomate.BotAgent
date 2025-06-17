using Serilog;
using Serilog.Events;
using System;
using System.IO;
using System.Linq;

namespace OpenAutomate.BotAgent.Executor
{
    /// <summary>
    /// Provides logging capabilities for the Executor application
    /// </summary>
    public static class Logger
    {
        private static ILogger _logger;

        /// <summary>
        /// Initializes the logger for the application
        /// </summary>
        /// <param name="executionId">The execution ID to include in the logs</param>
        /// <returns>Configured Serilog logger</returns>
        public static ILogger Initialize(string executionId = null)
        {
            // Configure log file path
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "OpenAutomate", "BotAgent", "Logs");

            // Ensure log directory exists
            Directory.CreateDirectory(logDirectory);

            string logFilename = string.IsNullOrEmpty(executionId)
                ? "executor-.log"
                : $"execution-{executionId}-.log";

            var logFilePath = Path.Combine(logDirectory, logFilename);

            // Configure and set Serilog logger
            _logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                .MinimumLevel.Override("System", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("ExecutionId", executionId ?? "unspecified")
                .WriteTo.Console()
                .WriteTo.File(
                    path: logFilePath,
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: 5 * 1024 * 1024, // 5MB per file
                    retainedFileCountLimit: 10, // Keep 10 log files
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [Execution:{ExecutionId}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            return _logger;
        }

        /// <summary>
        /// Gets the log file path for a given execution ID
        /// </summary>
        /// <param name="executionId">The execution ID</param>
        /// <returns>The full path to the log file</returns>
        public static string GetLogFilePath(string executionId = null)
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "OpenAutomate", "BotAgent", "Logs");

            if (string.IsNullOrEmpty(executionId))
            {
                return Path.Combine(logDirectory, "executor-.log");
            }

            // Look for the actual log file with timestamp
            var pattern = $"execution-{executionId}-*.log";
            var files = Directory.GetFiles(logDirectory, pattern);
            
            if (files.Length > 0)
            {
                // Return the most recent file if multiple exist
                return files.OrderByDescending(f => File.GetLastWriteTime(f)).First();
            }

            // Fallback to the expected pattern without timestamp
            return Path.Combine(logDirectory, $"execution-{executionId}-.log");
        }

        /// <summary>
        /// Writes an information message to the log
        /// </summary>
        /// <param name="message">The message to log</param>
        public static void Information(string message)
        {
            EnsureInitialized();
            _logger.Information(message);
        }

        /// <summary>
        /// Writes a warning message to the log
        /// </summary>
        /// <param name="message">The message to log</param>
        public static void Warning(string message)
        {
            EnsureInitialized();
            _logger.Warning(message);
        }

        /// <summary>
        /// Writes an error message to the log
        /// </summary>
        /// <param name="message">The message to log</param>
        /// <param name="exception">Optional exception to include in the log</param>
        public static void Error(string message, Exception exception = null)
        {
            EnsureInitialized();
            _logger.Error(exception, message);
        }

        /// <summary>
        /// Writes a debug message to the log
        /// </summary>
        /// <param name="message">The message to log</param>
        public static void Debug(string message)
        {
            EnsureInitialized();
            _logger.Debug(message);
        }

        /// <summary>
        /// Ensures the logger is initialized
        /// </summary>
        private static void EnsureInitialized()
        {
            if (_logger == null)
            {
                Initialize();
            }
        }

        /// <summary>
        /// Closes and flushes the logger
        /// </summary>
        public static void CloseAndFlush()
        {
            // Static wrapper around Serilog's Log.CloseAndFlush()
            Log.CloseAndFlush();
        }
    }
}
