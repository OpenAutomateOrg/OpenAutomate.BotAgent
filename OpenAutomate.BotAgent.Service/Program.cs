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
            services.AddSingleton<IConfigurationService, ConfigurationService>();
            services.AddSingleton<IMachineKeyManager, MachineKeyManager>();
            services.AddSingleton<IServerCommunication, ServerCommunication>();
            
            // Register package download service and its dependencies
            services.AddHttpClient<IPackageDownloadService, PackageDownloadService>();
            
            // Register SignalR client for server communication
            services.AddSingleton<BotAgentSignalRClient>();
            
            // Register execution manager (SignalRBroadcaster will be injected later by BotAgentService)
            services.AddSingleton<IExecutionManager, ExecutionManager>();
            
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
