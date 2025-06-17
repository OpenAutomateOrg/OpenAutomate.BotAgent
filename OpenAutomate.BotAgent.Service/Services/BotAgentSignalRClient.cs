using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using OpenAutomate.BotAgent.Service.Core;
using Microsoft.AspNetCore.Http.Connections.Client;
using System.Collections.Generic;
using System.Linq;

namespace OpenAutomate.BotAgent.Service.Services
{
    /// <summary>
    /// SignalR client for connecting to the orchestrator server
    /// </summary>
    public class BotAgentSignalRClient : IDisposable
    {
        private readonly ILogger<BotAgentSignalRClient> _logger;
        private readonly IConfigurationService _configService;
        private readonly IExecutionManager _executionManager;
        private HubConnection _connection;
        private bool _isReconnecting = false;
        private CancellationTokenSource _reconnectCts = new CancellationTokenSource();
        private readonly SemaphoreSlim _connectionLock = new SemaphoreSlim(1, 1);
        private Timer _keepAliveTimer;
        private readonly TimeSpan _keepAliveInterval = TimeSpan.FromMinutes(1);
        private static readonly Random _jitterRandom = new Random();
        
        // Simple deduplication for execution commands
        private readonly HashSet<string> _processedExecutionIds = new HashSet<string>();
        private readonly object _executionLock = new object();
        
        /// <summary>
        /// Event raised when connected to the server
        /// </summary>
        public event Action Connected;
        
        /// <summary>
        /// Event raised when disconnected from the server
        /// </summary>
        public event Action Disconnected;
        
        /// <summary>
        /// Gets whether the client is connected to the server
        /// </summary>
        public bool IsConnected => _connection?.State == HubConnectionState.Connected;
        
        /// <summary>
        /// Initializes a new instance of the <see cref="BotAgentSignalRClient"/> class
        /// </summary>
        public BotAgentSignalRClient(
            ILogger<BotAgentSignalRClient> logger,
            IConfigurationService configService,
            IExecutionManager executionManager)
        {
            _logger = logger;
            _configService = configService;
            _executionManager = executionManager;
        }
        
        /// <summary>
        /// Initializes the SignalR connection
        /// </summary>
        public async Task InitializeAsync()
        {
            var config = _configService.GetConfiguration();
            if (string.IsNullOrEmpty(config.MachineKey) || string.IsNullOrEmpty(config.ServerUrl))
            {
                _logger.LogError("Cannot initialize SignalR connection: Missing machine key or server URL");
                return;
            }
            
            var serverUrl = config.ServerUrl.TrimEnd('/');
            
            _connection = new HubConnectionBuilder()
                .WithUrl($"{serverUrl}/hubs/botagent?machineKey={config.MachineKey}", options => 
                {
                    options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets | 
                                         Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
                    options.SkipNegotiation = false;
                })
                .WithAutomaticReconnect(new RetryPolicy())
                .WithServerTimeout(TimeSpan.FromMinutes(2))
                .Build();
            
            // Register command handler
            _connection.On<string, object>("ReceiveCommand", async (command, payload) => 
            {
                _logger.LogInformation($"Received command: {command}");
                await HandleCommandAsync(command, payload);
            });
            
            // Handle connection events
            _connection.Reconnecting += error => 
            {
                _isReconnecting = true;
                StopKeepAliveTimer();
                _logger.LogWarning($"SignalR connection lost. Attempting to reconnect... Error: {error?.Message}");
                Disconnected?.Invoke();
                return Task.CompletedTask;
            };
            
            _connection.Reconnected += connectionId => 
            {
                _isReconnecting = false;
                _logger.LogInformation($"SignalR reconnected.");
                StartKeepAliveTimer();
                Connected?.Invoke();
                return Task.CompletedTask;
            };
            
            _connection.Closed += error =>
            {
                _logger.LogWarning($"SignalR connection closed. Error: {error?.Message}");
                StopKeepAliveTimer();
                Disconnected?.Invoke();
                
                if (!_isReconnecting && !_reconnectCts.IsCancellationRequested)
                {
                    _ = TryReconnectWithBackoffAsync(_reconnectCts.Token);
                }
                
                return Task.CompletedTask;
            };
            
            await ConnectAsync();
        }
        
        /// <summary>
        /// Connects to the SignalR hub
        /// </summary>
        public async Task ConnectAsync()
        {
            await _connectionLock.WaitAsync();
            try
            {
                if (_connection.State != HubConnectionState.Disconnected)
                {
                    _logger.LogDebug($"Connection is already in state {_connection.State}");
                    return;
                }
                
                _logger.LogInformation("Connecting to SignalR hub...");
                await _connection.StartAsync();
                _logger.LogInformation("Connected to SignalR hub.");
                
                StartKeepAliveTimer();
                Connected?.Invoke();
                await SendStatusUpdateAsync("Ready");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to SignalR hub");
                Disconnected?.Invoke();
                
                if (!_reconnectCts.IsCancellationRequested)
                {
                    _ = TryReconnectWithBackoffAsync(_reconnectCts.Token);
                }
            }
            finally
            {
                _connectionLock.Release();
            }
        }
        
        /// <summary>
        /// Disconnects from the SignalR hub
        /// </summary>
        public async Task DisconnectAsync()
        {
            await _connectionLock.WaitAsync();
            try
            {
                if (_connection == null || _connection.State != HubConnectionState.Connected)
                {
                    return;
                }
                
                _logger.LogInformation("Disconnecting from SignalR hub...");
                StopKeepAliveTimer();
                await _reconnectCts.CancelAsync();
                _reconnectCts.Dispose();
                _reconnectCts = new CancellationTokenSource();
                await _connection.StopAsync();
                
                _logger.LogInformation("Disconnected from SignalR hub");
                Disconnected?.Invoke();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disconnecting from SignalR hub");
                throw;
            }
            finally
            {
                _connectionLock.Release();
            }
        }
        
        /// <summary>
        /// Sends a status update to the hub
        /// </summary>
        public async Task SendStatusUpdateAsync(string status, string executionId = null)
        {
            if (_connection?.State != HubConnectionState.Connected)
            {
                _logger.LogWarning("Cannot send status update: Not connected");
                return;
            }
            
            try
            {
                await _connection.InvokeAsync("SendStatusUpdate", status, executionId);
                _logger.LogDebug($"Status update sent: {status}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send status update");
            }
        }
        
        /// <summary>
        /// Sends an execution status update to the hub
        /// </summary>
        public async Task SendExecutionStatusUpdateAsync(string executionId, string status, string message = null)
        {
            if (_connection?.State != HubConnectionState.Connected)
            {
                _logger.LogWarning("Cannot send execution status update: Not connected");
                return;
            }
            
            try
            {
                await _connection.InvokeAsync("SendExecutionStatusUpdate", executionId, status, message);
                _logger.LogDebug($"Execution status update sent for {executionId}: {status}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send execution status update for {ExecutionId}", executionId);
            }
        }
        
        /// <summary>
        /// Starts the keep-alive timer to send periodic pings
        /// </summary>
        private void StartKeepAliveTimer()
        {
            StopKeepAliveTimer();
            
            _keepAliveTimer = new Timer(async _ => 
            {
                try 
                {
                    if (_connection?.State == HubConnectionState.Connected)
                    {
                        await _connection.InvokeAsync("KeepAlive");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send keep-alive ping");
                }
            }, null, _keepAliveInterval, _keepAliveInterval);
        }
        
        /// <summary>
        /// Stops the keep-alive timer
        /// </summary>
        private void StopKeepAliveTimer()
        {
            if (_keepAliveTimer != null)
            {
                _keepAliveTimer.Dispose();
                _keepAliveTimer = null;
            }
        }
        
        /// <summary>
        /// Handles a command received from the hub
        /// </summary>
        private async Task HandleCommandAsync(string command, object payload)
        {
            try
            {
                _logger.LogInformation("Received command: {Command} with payload: {Payload}", command, JsonSerializer.Serialize(payload));

                switch (command)
                {
                    case "ExecutePackage":
                        await ProcessExecutePackageCommandAsync(payload);
                        break;

                    case "CancelExecution":
                        await ProcessCancelExecutionCommandAsync(payload);
                        break;

                    case "Heartbeat":
                        await ProcessHeartbeatCommandAsync();
                        break;

                    default:
                        _logger.LogWarning("Unknown command received: {Command}", command);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling command: {Command}", command);
            }
        }

        /// <summary>
        /// Processes the ExecutePackage command with deduplication
        /// </summary>
        private async Task ProcessExecutePackageCommandAsync(object payload)
        {
            // Extract executionId for deduplication
            string executionId = ExtractExecutionIdFromPayload(payload);

            // Check for duplicate execution
            if (IsDuplicateExecution(executionId))
            {
                _logger.LogWarning("Duplicate ExecutePackage command received for executionId: {ExecutionId}. Ignoring.", executionId);
                return;
            }

            _logger.LogInformation("Processing ExecutePackage command for executionId: {ExecutionId}", executionId ?? "unknown");
            await _executionManager.StartExecutionAsync(payload);
        }

        /// <summary>
        /// Processes the CancelExecution command
        /// </summary>
        private async Task ProcessCancelExecutionCommandAsync(object payload)
        {
            var cancelExecutionId = payload.ToString();
            _logger.LogInformation("Processing CancelExecution command for executionId: {ExecutionId}", cancelExecutionId);
            await _executionManager.CancelExecutionAsync(cancelExecutionId);
        }

        /// <summary>
        /// Processes the Heartbeat command
        /// </summary>
        private async Task ProcessHeartbeatCommandAsync()
        {
            _logger.LogDebug("Processing Heartbeat command");
            await SendStatusUpdateAsync("Ready");
        }

        /// <summary>
        /// Extracts the execution ID from the payload for deduplication
        /// </summary>
        private string ExtractExecutionIdFromPayload(object payload)
        {
            try
            {
                var payloadJson = JsonSerializer.Serialize(payload);
                var payloadDict = JsonSerializer.Deserialize<Dictionary<string, object>>(payloadJson);
                if (payloadDict.TryGetValue("executionId", out var execIdObj))
                {
                    return execIdObj.ToString();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract executionId from payload for deduplication");
            }
            return null;
        }

        /// <summary>
        /// Checks if the execution is a duplicate and adds it to the processed list if not
        /// </summary>
        private bool IsDuplicateExecution(string executionId)
        {
            if (string.IsNullOrEmpty(executionId))
                return false;

            lock (_executionLock)
            {
                if (_processedExecutionIds.Contains(executionId))
                {
                    return true;
                }

                _processedExecutionIds.Add(executionId);

                // Clean up old entries to prevent memory buildup (keep only last 100)
                if (_processedExecutionIds.Count > 100)
                {
                    var oldestEntries = _processedExecutionIds.Take(50).ToList();
                    foreach (var entry in oldestEntries)
                    {
                        _processedExecutionIds.Remove(entry);
                    }
                }

                return false;
            }
        }
        
        /// <summary>
        /// Tries to reconnect with exponential backoff
        /// </summary>
        private async Task TryReconnectWithBackoffAsync(CancellationToken cancellationToken)
        {
            var config = _configService.GetConfiguration();
            if (!config.AutoStart)
            {
                _logger.LogInformation("Not attempting to reconnect because AutoStart is disabled");
                return;
            }
            
            int retryAttempt = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    
                    config = _configService.GetConfiguration();
                    if (!config.AutoStart)
                    {
                        _logger.LogInformation("Stopping reconnect attempts because AutoStart is now disabled");
                        break;
                    }
                    
                    retryAttempt++;
                    
                    // Exponential backoff with jitter
                    var delay = Math.Min(Math.Pow(2, retryAttempt), 60);
                    double jitter = _jitterRandom.NextDouble() * 0.3 + 0.85;
                    var delayWithJitter = TimeSpan.FromSeconds(delay * jitter);
                    
                    _logger.LogInformation($"Attempting to reconnect (attempt {retryAttempt}) in {delayWithJitter.TotalSeconds:F1} seconds");
                    await Task.Delay(delayWithJitter, cancellationToken);
                    
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    
                    await _connectionLock.WaitAsync(cancellationToken);
                    try
                    {
                        if (_connection.State != HubConnectionState.Disconnected)
                        {
                            break;
                        }
                        
                        await _connection.StartAsync(cancellationToken);
                        _logger.LogInformation($"Successfully reconnected after {retryAttempt} attempts");
                        break;
                    }
                    finally
                    {
                        _connectionLock.Release();
                    }
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("cannot be started if it is not in the Disconnected state"))
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to reconnect (attempt {retryAttempt})");
                    
                    if (retryAttempt % 10 == 0)
                    {
                        await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                    }
                }
            }
        }
        
        /// <summary>
        /// Disposes the SignalR client
        /// </summary>
        public void Dispose()
        {
            try
            {
                StopKeepAliveTimer();
                _reconnectCts.CancelAsync().GetAwaiter().GetResult();
                
                if (_connection != null && _connection.State != HubConnectionState.Disconnected)
                {
                    _connection.StopAsync().GetAwaiter().GetResult();
                }
                
                _connection?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                _reconnectCts.Dispose();
                _connectionLock.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during SignalR client disposal");
            }
        }
        
        /// <summary>
        /// Custom retry policy for automatic reconnection
        /// </summary>
        private class RetryPolicy : IRetryPolicy
        {
            public TimeSpan? NextRetryDelay(RetryContext retryContext)
            {
                if (retryContext.PreviousRetryCount >= 5)
                {
                    return null;
                }
                
                return TimeSpan.FromSeconds(Math.Pow(2, retryContext.PreviousRetryCount));
            }
        }
    }
} 