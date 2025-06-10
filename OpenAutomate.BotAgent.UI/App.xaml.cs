using System;
using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Threading;
using OpenAutomate.BotAgent.UI.Services;
using System.IO;
using Serilog;
using Serilog.Events;

namespace OpenAutomate.BotAgent.UI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private SignalRClientService _signalRClient;
    
    /// <summary>
    /// Initialize application and set up error handlers
    /// </summary>
    public App()
    {
        // Set up global exception handling
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
    }
    
    /// <summary>
    /// Application startup - initialize logging
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        ConfigureLogging();
        base.OnStartup(e);
        
        // Set up global exception handling
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            var exception = args.ExceptionObject as Exception;
            LoggingService.Error(exception, "Unhandled AppDomain exception");
            MessageBox.Show($"An unhandled error occurred: {exception?.Message}", 
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        };
        
        DispatcherUnhandledException += (s, args) =>
        {
            LoggingService.Error(args.Exception, "Unhandled dispatcher exception");
            MessageBox.Show($"An error occurred: {args.Exception.Message}", 
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        
        // Initialize SignalR client
        InitializeSignalR();
    }
    
    /// <summary>
    /// Application exit - flush and close logger
    /// </summary>
    protected override void OnExit(ExitEventArgs e)
    {
        // Clean up SignalR resources
        _signalRClient?.Dispose();
        
        // Close and flush logging
        Log.CloseAndFlush();
        
        base.OnExit(e);
    }
    
    /// <summary>
    /// Handle unhandled exceptions in the UI thread
    /// </summary>
    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Log the exception
        LoggingService.Error(e.Exception, "Unhandled exception in UI thread");
        
        MessageBox.Show($"An unexpected error occurred: {e.Exception.Message}\n\nSee logs for details.", 
            "Application Error", MessageBoxButton.OK, MessageBoxImage.Error);
            
        // Prevent the application from crashing
        e.Handled = true;
    }
    
    /// <summary>
    /// Handle unhandled exceptions in non-UI threads
    /// </summary>
    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception;
        
        if (exception != null)
        {
            LoggingService.Fatal(exception, "Unhandled exception in non-UI thread");
        }
        else
        {
            LoggingService.Fatal(new Exception("Unknown exception"), 
                "Unhandled exception in non-UI thread: {0}", e.ExceptionObject?.ToString() ?? "Unknown");
        }
        
        // For non-UI thread exceptions, we can't prevent the application from terminating
        // when IsTerminating is true, but we can at least log it.
        if (e.IsTerminating)
        {
            LoggingService.Fatal(exception ?? new Exception("Unknown exception"), 
                "Application is terminating due to an unhandled exception");
            
            MessageBox.Show("A fatal error has occurred. The application will now close.\n\nSee logs for details.", 
                "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
                
            LoggingService.CloseAndFlush();
        }
    }
    
    /// <summary>
    /// Initializes the SignalR client
    /// </summary>
    private void InitializeSignalR()
    {
        try
        {
            _signalRClient = new SignalRClientService();
            _signalRClient.InitializeAsync().ConfigureAwait(false);
            LoggingService.Information("SignalR client initialized");
        }
        catch (Exception ex)
        {
            LoggingService.Error(ex, "Failed to initialize SignalR client");
        }
    }
    
    /// <summary>
    /// Configures logging
    /// </summary>
    private void ConfigureLogging()
    {
        try
        {
            // Use the same log directory as the service for consistency
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "OpenAutomate", "BotAgent", "Logs");
            
            // Ensure log directory exists
            Directory.CreateDirectory(logDirectory);
            
            var logFilePath = Path.Combine(logDirectory, "botagent-ui-.log");
            
            // Configure Serilog logger
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                .MinimumLevel.Override("System", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .WriteTo.File(
                    path: logFilePath,
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: 10 * 1024 * 1024, // 10MB per file
                    retainedFileCountLimit: 31, // Keep logs for a month
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
            
            LoggingService.Information("Logging initialized");
        }
        catch (Exception ex)
        {
            // Can't log the exception about logging initialization failure, just show message box
            MessageBox.Show($"Failed to initialize logging: {ex.Message}", 
                "Logging Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

