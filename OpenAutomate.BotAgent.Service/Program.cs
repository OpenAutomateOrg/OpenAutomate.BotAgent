using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR;
using Serilog;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using OpenAutomate.BotAgent.Service;
using OpenAutomate.BotAgent.Service.Core;
using OpenAutomate.BotAgent.Service.Services;
using OpenAutomate.BotAgent.Service.Models;

try
{
    Log.Information("Starting OpenAutomate Bot Agent");

    // Register implementations
    var host = Host.CreateDefaultBuilder(args)
        .UseWindowsService(options =>
        {
            options.ServiceName = "OpenAutomateBotAgent";
        })
        .ConfigureLogging("BotAgentService") // Use the extension method from LoggingService
        .ConfigureServices((hostContext, services) =>
        {
            // Register configuration
            services.Configure<BotAgentConfig>(
                hostContext.Configuration.GetSection("BotAgent"));
            
            // Register core services
            services.AddSingleton<IAssetManager, AssetManager>(); // Use the real implementation
            services.AddSingleton<IExecutionManager, MockExecutionManager>(); // Still using mock for execution
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
    public Task<bool> HasActiveExecutionsAsync() => Task.FromResult(false); // Default to no active executions
}
