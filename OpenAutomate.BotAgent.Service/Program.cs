using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR;
using Serilog;
using Serilog.Events;
using System;
using System.IO;
using OpenAutomate.BotAgent.Service;
using OpenAutomate.BotAgent.Service.Core;
using OpenAutomate.BotAgent.Service.Services;
using OpenAutomate.BotAgent.Service.Models;
using System.Threading.Tasks;
using System.Collections.Generic;

// Configure Serilog for file-based logging
var logDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "OpenAutomate", "BotAgent", "Logs");

// Ensure log directory exists
Directory.CreateDirectory(logDirectory);

var logFilePath = Path.Combine(logDirectory, "botagent-.log");

// Configure Serilog logger
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .MinimumLevel.Override("System", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        path: logFilePath,
        rollingInterval: RollingInterval.Day,
        fileSizeLimitBytes: 10 * 1024 * 1024, // 10MB per file
        retainedFileCountLimit: 31, // Keep logs for a month
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Starting OpenAutomate Bot Agent");

    // Register temporary mock implementations until full implementations are ready
    // These will be replaced with proper implementations later
    var host = Host.CreateDefaultBuilder(args)
        .UseWindowsService(options =>
        {
            options.ServiceName = "OpenAutomateBotAgent";
        })
        .UseSerilog() // Use Serilog for logging
        .ConfigureServices((hostContext, services) =>
        {
            // Register configuration
            services.Configure<BotAgentConfig>(
                hostContext.Configuration.GetSection("BotAgent"));
            
            // Register core services
            services.AddSingleton<IAssetManager, MockAssetManager>();
            services.AddSingleton<IExecutionManager, MockExecutionManager>();
            services.AddSingleton<IServerCommunication, ServerCommunication>();
            services.AddSingleton<IMachineKeyManager, MachineKeyManager>();
            services.AddSingleton<IConfigurationService, ConfigurationService>();
            
            // Register SignalR client for server communication
            services.AddSingleton<BotAgentSignalRClient>();
            
            // Register SignalR hub
            services.AddSingleton<BotAgentLocalHub>();
            
            // Register API server
            services.AddSingleton<IApiServer, ApiServer>();
            
            // Register the Windows Service
            services.AddHostedService<BotAgentService>();
        })
        .Build();

    await host.RunAsync();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

// Mock implementation for temporarily resolving dependencies
class MockExecutionManager : IExecutionManager
{
    public Task<string> StartExecutionAsync(object executionData) => Task.FromResult("mock-execution-id");
    public Task CancelExecutionAsync(string executionId) => Task.CompletedTask;
    public Task SendStatusUpdatesAsync() => Task.CompletedTask;
}

class MockAssetManager : IAssetManager
{
    public Task<string> GetAssetAsync(string key) => Task.FromResult(string.Empty);
    public Task<IEnumerable<string>> GetAllAssetKeysAsync() => Task.FromResult<IEnumerable<string>>(Array.Empty<string>());
    public Task SyncAssetsAsync() => Task.CompletedTask;

}
