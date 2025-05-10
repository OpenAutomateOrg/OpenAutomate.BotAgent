using System;
using System.IO;
using Serilog;
using Serilog.Events;

namespace OpenAutomate.BotAgent.UI.Services
{
    /// <summary>
    /// Service for logging UI application events
    /// </summary>
    public static class LoggingService
    {
        private static ILogger _logger;
        private static readonly string _logDirectory;
        private static readonly string _logFilePath;
        
        /// <summary>
        /// Static constructor to initialize logging
        /// </summary>
        static LoggingService()
        {
            // Set up log directory in the same location as the service logs
            _logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "OpenAutomate", "BotAgent", "Logs");
                
            // Ensure log directory exists
            Directory.CreateDirectory(_logDirectory);
            
            _logFilePath = Path.Combine(_logDirectory, "botagent-ui-.log");
            
            // Initialize the logger
            InitializeLogger();
        }
        
        /// <summary>
        /// Gets the global logger instance
        /// </summary>
        public static ILogger Logger => _logger;
        
        /// <summary>
        /// Sets up the logger configuration
        /// </summary>
        private static void InitializeLogger()
        {
            _logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                .MinimumLevel.Override("System", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .WriteTo.File(
                    path: _logFilePath,
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: 10 * 1024 * 1024, // 10MB per file
                    retainedFileCountLimit: 31, // Keep logs for a month
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
                
            _logger.Information("OpenAutomate Bot Agent UI starting up");
        }
        
        /// <summary>
        /// Logs a debug message
        /// </summary>
        public static void Debug(string messageTemplate, params object[] propertyValues)
        {
            _logger.Debug(messageTemplate, propertyValues);
        }
        
        /// <summary>
        /// Logs an informational message
        /// </summary>
        public static void Information(string messageTemplate, params object[] propertyValues)
        {
            _logger.Information(messageTemplate, propertyValues);
        }
        
        /// <summary>
        /// Logs a warning message
        /// </summary>
        public static void Warning(string messageTemplate, params object[] propertyValues)
        {
            _logger.Warning(messageTemplate, propertyValues);
        }
        
        /// <summary>
        /// Logs an error message
        /// </summary>
        public static void Error(Exception exception, string messageTemplate, params object[] propertyValues)
        {
            _logger.Error(exception, messageTemplate, propertyValues);
        }
        
        /// <summary>
        /// Logs a fatal error message
        /// </summary>
        public static void Fatal(Exception exception, string messageTemplate, params object[] propertyValues)
        {
            _logger.Fatal(exception, messageTemplate, propertyValues);
        }
        
        /// <summary>
        /// Closes and flushes the logger
        /// </summary>
        public static void CloseAndFlush()
        {
            Log.CloseAndFlush();
        }
    }
} 