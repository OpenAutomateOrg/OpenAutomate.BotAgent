using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using System;
using System.IO;

namespace OpenAutomate.BotAgent.Service.Core
{
    /// <summary>
    /// Provides centralized logging configuration for the application
    /// </summary>
    public static class LoggingService
    {
        /// <summary>
        /// Configures Serilog for the application
        /// </summary>
        /// <param name="applicationName">Optional application name for log context</param>
        /// <returns>Configured Serilog logger</returns>
        public static ILogger ConfigureSerilog(string applicationName = "BotAgent")
        {
            // Configure log file path
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "OpenAutomate", "BotAgent", "Logs");

            // Ensure log directory exists
            Directory.CreateDirectory(logDirectory);

            var logFilePath = Path.Combine(logDirectory, $"{applicationName.ToLower()}-.log");

            // Configure and return Serilog logger
            return new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                .MinimumLevel.Override("System", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", applicationName)
                .WriteTo.Console()
                .WriteTo.File(
                    path: logFilePath,
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: 10 * 1024 * 1024, // 10MB per file
                    retainedFileCountLimit: 31, // Keep logs for a month
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{Application}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
        }

        /// <summary>
        /// Configures Serilog for a Host builder
        /// </summary>
        /// <param name="hostBuilder">The host builder to configure</param>
        /// <param name="applicationName">Optional application name for log context</param>
        /// <returns>The configured host builder</returns>
        public static IHostBuilder ConfigureLogging(this IHostBuilder hostBuilder, string applicationName = "BotAgent")
        {
            // Configure Serilog
            Log.Logger = ConfigureSerilog(applicationName);
            
            // Apply Serilog to the host
            return hostBuilder.UseSerilog();
        }
    }
} 