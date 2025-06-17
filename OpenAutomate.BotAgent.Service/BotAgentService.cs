using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using OpenAutomate.BotAgent.Service.Core;
using OpenAutomate.BotAgent.Service.Services;

namespace OpenAutomate.BotAgent.Service
{
    /// <summary>
    /// Main Bot Agent Windows Service
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
        private SignalRBroadcaster _signalRBroadcaster;
        private ILoggerFactory _loggerFactory;

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
                // Initialize SignalR broadcaster for server communication
                InitializeSignalRBroadcaster();
                
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
                
                _loggerFactory?.Dispose();
                
                _logger.LogInformation("Bot Agent Service stopped");
            }
        }

        /// <summary>
        /// Initializes the SignalR broadcaster for server communication only
        /// </summary>
        private void InitializeSignalRBroadcaster()
        {
            try
            {
                _logger.LogInformation("Initializing SignalR broadcaster for server communication");
                
                _loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var signalRLogger = _loggerFactory.CreateLogger<SignalRBroadcaster>();
                
                _signalRBroadcaster = new SignalRBroadcaster(_serverCommunication, signalRLogger);
                
                if (!TryInjectSignalRBroadcaster())
                {
                    _logger.LogWarning("Failed to inject SignalRBroadcaster into ExecutionManager. " +
                                     "ExecutionManager implementation does not support SignalR broadcasting. " +
                                     "Type: {ExecutionManagerType}", _executionManager.GetType().Name);
                }
                
                _logger.LogInformation("SignalR broadcaster initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize SignalR broadcaster");
                throw;
            }
        }
        
        private bool TryInjectSignalRBroadcaster()
        {
            var setSignalRMethod = _executionManager.GetType().GetMethod("SetSignalRBroadcaster");
            if (setSignalRMethod != null)
            {
                try
                {
                    setSignalRMethod.Invoke(_executionManager, new object[] { _signalRBroadcaster });
                    _logger.LogInformation("SignalRBroadcaster injected into ExecutionManager via reflection");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to inject SignalRBroadcaster via reflection");
                }
            }
            
            if (_executionManager is ExecutionManager execManager)
            {
                execManager.SetSignalRBroadcaster(_signalRBroadcaster);
                _logger.LogInformation("SignalRBroadcaster injected into ExecutionManager via casting");
                return true;
            }
            
            return false;
        }
    }
} 