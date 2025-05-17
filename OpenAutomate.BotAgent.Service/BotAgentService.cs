using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Hosting.Server.Features;
using OpenAutomate.BotAgent.Service.Core;
using OpenAutomate.BotAgent.Service.Services;

namespace OpenAutomate.BotAgent.Service
{
    /// <summary>
    /// Windows service implementation for the Bot Agent
    /// </summary>
    public class BotAgentService : BackgroundService
    {
        private readonly ILogger<BotAgentService> _logger;
        private readonly IApiServer _apiServer;
        private readonly IServerCommunication _serverCommunication;
        private readonly IAssetManager _assetManager;
        private readonly IExecutionManager _executionManager;
        private readonly IMachineKeyManager _machineKeyManager;
        private readonly IConfigurationService _configService;
        private IWebHost _signalRHost;
        private SignalRBroadcaster _signalRBroadcaster;
        
        /// <summary>
        /// Initializes a new instance of the BotAgentService class
        /// </summary>
        public BotAgentService(
            ILogger<BotAgentService> logger,
            IApiServer apiServer,
            IServerCommunication serverCommunication,
            IAssetManager assetManager,
            IExecutionManager executionManager,
            IMachineKeyManager machineKeyManager,
            IConfigurationService configService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _apiServer = apiServer ?? throw new ArgumentNullException(nameof(apiServer));
            _serverCommunication = serverCommunication ?? throw new ArgumentNullException(nameof(serverCommunication));
            _assetManager = assetManager ?? throw new ArgumentNullException(nameof(assetManager));
            _executionManager = executionManager ?? throw new ArgumentNullException(nameof(executionManager));
            _machineKeyManager = machineKeyManager ?? throw new ArgumentNullException(nameof(machineKeyManager));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        }
        
        /// <summary>
        /// Executes the service
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Bot Agent Service starting");
            
            try
            {
                // Start SignalR hub
                await StartSignalRHubAsync();
                
                // Start API server
                await _apiServer.StartAsync();
                
                // Attempt to connect to server if configured to auto-start
                var config = _configService.GetConfiguration();
                if (_machineKeyManager.HasMachineKey() && config.AutoStart)
                {
                    _logger.LogInformation("Auto-connecting to server");
                    await _serverCommunication.ConnectAsync();
                    
                    // Note: We no longer sync assets on startup
                    // Assets will be retrieved on-demand directly from the server
                    // This ensures we always have the latest values and don't store sensitive data in memory
                }
                
                // Use a longer interval for health checks (5 minutes instead of 1)
                var healthCheckInterval = TimeSpan.FromMinutes(5);
                var lastHealthCheck = DateTime.UtcNow;
                
                while (!stoppingToken.IsCancellationRequested)
                {
                    // Health check and status update only at the defined interval
                    // This reduces unnecessary network traffic
                    if (_serverCommunication.IsConnected && 
                        (DateTime.UtcNow - lastHealthCheck) >= healthCheckInterval)
                    {
                        await _serverCommunication.SendHealthCheckAsync();
                        
                        // Check if we're running any executions
                        bool isBusy = await _executionManager.HasActiveExecutionsAsync();
                        
                        // Update status based on execution state
                        string status = isBusy ? AgentStatus.Busy : AgentStatus.Available;
                        await _serverCommunication.UpdateStatusAsync(status);
                        
                        lastHealthCheck = DateTime.UtcNow;
                        _logger.LogDebug("Performed periodic health check and status update: {Status}", status);
                    }
                    
                    // Use a shorter delay for the loop to remain responsive
                    // while reducing the frequency of active health checks
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Bot Agent Service");
                throw;
            }
            finally
            {
                // Clean up
                await _apiServer.StopAsync();
                
                if (_signalRHost != null)
                {
                    await _signalRHost.StopAsync();
                }
                
                _logger.LogInformation("Bot Agent Service stopped");
            }
        }

        /// <summary>
        /// Starts the SignalR hub for UI real-time communication
        /// this feature is for future use, not in capstone project scope
        /// </summary>
        private async Task StartSignalRHubAsync()
        {
            try
            {
                var config = _configService.GetConfiguration();
                // Use a different port (API port + 1) for SignalR to avoid conflict with API server
                var signalRPort = config.ApiPort + 1; // Typically 8082 if API is on 8081
                
                _logger.LogInformation("Starting SignalR hub on port {Port}", signalRPort);
                
                // Create a WebHostBuilder directly instead of using Host.CreateDefaultBuilder()
                var webHostBuilder = new WebHostBuilder()
                    .UseKestrel(options =>
                    {
                        options.ListenLocalhost(signalRPort);
                    })
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        
                        // Configure SignalR with optimized settings for minimal network traffic
                        services.AddSignalR(options => 
                        {
                            // Increase keepalive interval to reduce traffic (default is 15 seconds)
                            options.KeepAliveInterval = TimeSpan.FromMinutes(2);
                            
                            // Increase client timeout to accommodate the longer keepalive interval
                            // (should be at least double the keepalive interval)
                            options.ClientTimeoutInterval = TimeSpan.FromMinutes(5);
                            
                            // Enable detailed errors in development only
                            options.EnableDetailedErrors = false;
                            
                            // Allow binary protocol for more efficient transport (smaller payload size)
                            options.MaximumReceiveMessageSize = 64 * 1024; // 64KB should be sufficient
                        })
                        .AddHubOptions<BotAgentLocalHub>(options =>
                        {
                            // Set maximum concurrent connections based on expected load
                            options.MaximumParallelInvocationsPerClient = 1;
                            
                            // Stream buffer capacity (lower value means more frequent but smaller chunks)
                            options.StreamBufferCapacity = 8;
                        });
                        
                        services.AddSingleton(_serverCommunication);
                        services.AddSingleton<BotAgentLocalHub>();
                        
                        // Register the broadcaster service after SignalR is registered
                        services.AddSingleton<SignalRBroadcaster>();
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapHub<BotAgentLocalHub>("/hubs/client", options =>
                            {
                                // Prioritize WebSockets with LongPolling fallback
                                options.Transports = HttpTransportType.WebSockets | 
                                                    HttpTransportType.LongPolling;
                                
                                // Set longer polling delays for LongPolling transport to reduce traffic
                                options.LongPolling.PollTimeout = TimeSpan.FromSeconds(90);
                                
                                // WebSockets is read-only, so we can't assign to it
                                // Just use the default WebSockets configuration
                            });
                        });
                    });
                
                // Build and start the web host
                _signalRHost = webHostBuilder.Build();
                await _signalRHost.StartAsync();
                
                // Get the SignalRBroadcaster service from the service provider
                _signalRBroadcaster = _signalRHost.Services.GetService<SignalRBroadcaster>();
                
                if (_signalRBroadcaster == null)
                {
                    _logger.LogWarning("SignalRBroadcaster service could not be resolved");
                }
                else
                {
                    _logger.LogInformation("SignalRBroadcaster service initialized");
                }
                
                // Log the actual URLs the server is listening on
                var serverAddresses = _signalRHost.ServerFeatures.Get<IServerAddressesFeature>()?.Addresses;
                if (serverAddresses != null)
                {
                    foreach (var address in serverAddresses)
                    {
                        _logger.LogInformation("SignalR hub listening on: {Address}", address);
                    }
                }
                
                _logger.LogInformation("SignalR hub started successfully with optimized keepalive settings");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start SignalR hub");
                throw;
            }
        }
    }
} 