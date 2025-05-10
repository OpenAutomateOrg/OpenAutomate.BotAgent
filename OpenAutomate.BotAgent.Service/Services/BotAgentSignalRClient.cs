using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using OpenAutomate.BotAgent.Service.Core;
using Microsoft.AspNetCore.Http.Connections.Client;
using System.Security.Cryptography;

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
        // Thread-safe random number generator for jitter calculations
        private static readonly Random _jitterRandom = new Random();
        private static readonly object _randomLock = new object();
        
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
            
            // The URL already includes the tenant slug (e.g., https://openautomateapp.com/acme-corp)
            // So we simply append the hub path to it
            _connection = new HubConnectionBuilder()
                .WithUrl($"{serverUrl}/hubs/botagent?machineKey={config.MachineKey}", options => 
                {
                    // Set WebSockets as preferred transport with LongPolling fallback
                    options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets | 
                                         Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
                    
                    // Configure any additional HttpConnectionOptions here
                    options.SkipNegotiation = false;
                    
                    // Note: HandshakeTimeout is not available in this version
                    // We'll rely on the default timeout values
                })
                .WithAutomaticReconnect(new RetryPolicy())
                // Increase server timeout to 2 minutes (longer than the keep-alive interval)
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
                _logger.LogInformation($"SignalR reconnected. ConnectionId: {connectionId}");
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
                    // Try to reconnect if not already reconnecting and not cancelled
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
            // Use a lock to prevent multiple concurrent connection attempts
            await _connectionLock.WaitAsync();
            try
            {
                // Check if already connected or connecting
                if (_connection.State != HubConnectionState.Disconnected)
                {
                    _logger.LogDebug($"Connection is already in state {_connection.State}, not attempting to connect");
                    return;
                }
                
                _logger.LogInformation("Connecting to SignalR hub...");
                await _connection.StartAsync();
                _logger.LogInformation($"Connected to SignalR hub. ConnectionId: {_connection.ConnectionId}");
                
                // Start the keep-alive timer
                StartKeepAliveTimer();
                
                // Raise the Connected event
                Connected?.Invoke();
                
                // Send initial status update
                await SendStatusUpdateAsync("Ready");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to SignalR hub");
                Disconnected?.Invoke();
                
                // Try to reconnect with backoff
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
                
                // Stop the keep-alive timer
                StopKeepAliveTimer();
                
                // Cancel any reconnect attempts
                await _reconnectCts.CancelAsync();
                _reconnectCts.Dispose();
                
                // Create a new token source to prevent previous reconnect tasks from continuing
                _reconnectCts = new CancellationTokenSource();
                
                // Stop the connection
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
                        _logger.LogDebug("Sending keep-alive ping to server");
                        // Instead of calling a non-existent hub method, use a protocol-level ping
                        // or send a lightweight status update which we know exists
                        
                        // Option 1: If the server supports SendStatusUpdate without requiring an execution ID
                        await _connection.InvokeAsync("SendStatusUpdate", "Heartbeat", null);
                        
                        // Option 2 (alternative): Use a lower-level ping that doesn't require a hub method
                        // await _connection.SendCoreAsync("", new object[0]);
                        
                        _logger.LogDebug("Keep-alive ping sent successfully");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send keep-alive ping");
                    
                    // Check if the connection is still in Connected state
                    // If many pings fail, we might want to force a reconnection
                    if (_connection?.State == HubConnectionState.Connected)
                    {
                        _logger.LogDebug("Connection still shows as connected despite ping failure");
                    }
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
                switch (command)
                {
                    case "ExecutePackage":
                        var executionData = JsonSerializer.Deserialize<object>(
                            JsonSerializer.Serialize(payload));
                        await _executionManager.StartExecutionAsync(executionData);
                        break;
                        
                    case "CancelExecution":
                        var executionId = payload.ToString();
                        await _executionManager.CancelExecutionAsync(executionId);
                        break;
                        
                    case "Heartbeat":
                        await SendStatusUpdateAsync("Ready");
                        break;
                        
                    default:
                        _logger.LogWarning($"Unknown command received: {command}");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error handling command: {command}");
            }
        }
        
        /// <summary>
        /// Tries to reconnect with exponential backoff
        /// </summary>
        private async Task TryReconnectWithBackoffAsync(CancellationToken cancellationToken)
        {
            // Check if AutoStart is enabled before attempting to reconnect
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
                    // Check token cancellation before each attempt
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogInformation("Reconnect cancelled by request");
                        break;
                    }
                    
                    // Check again before each reconnect attempt in case it was changed
                    config = _configService.GetConfiguration();
                    if (!config.AutoStart)
                    {
                        _logger.LogInformation("Stopping reconnect attempts because AutoStart is now disabled");
                        break;
                    }
                    
                    retryAttempt++;
                    
                    // Exponential backoff with jitter
                    var delay = Math.Min(Math.Pow(2, retryAttempt), 60);
                    
                    // Generate thread-safe jitter value between 0.85 and 1.15
                    double jitter;
                    lock (_randomLock)
                    {
                        jitter = _jitterRandom.NextDouble() * 0.3 + 0.85;
                    }
                    
                    var delayWithJitter = TimeSpan.FromSeconds(delay * jitter);
                    
                    _logger.LogInformation($"Attempting to reconnect (attempt {retryAttempt}) in {delayWithJitter.TotalSeconds:F1} seconds");
                    
                    await Task.Delay(delayWithJitter, cancellationToken);
                    
                    // Check cancellation after delay
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogInformation("Reconnect cancelled after delay");
                        break;
                    }
                    
                    // Check again right before connecting
                    config = _configService.GetConfiguration();
                    if (!config.AutoStart)
                    {
                        _logger.LogInformation("Stopping reconnect attempt because AutoStart is now disabled");
                        break;
                    }
                    
                    // Use the connection lock to prevent race conditions
                    await _connectionLock.WaitAsync(cancellationToken);
                    try
                    {
                        // Check if we're already connected or reconnecting through automatic reconnect
                        if (_connection.State != HubConnectionState.Disconnected)
                        {
                            _logger.LogInformation($"Not reconnecting because connection is in state {_connection.State}");
                            break;
                        }
                        
                        // Now it's safe to reconnect
                        await _connection.StartAsync(cancellationToken);
                        _logger.LogInformation($"Successfully reconnected after {retryAttempt} attempts");
                        
                        // Exit the loop after successful reconnection
                        break;
                    }
                    finally
                    {
                        _connectionLock.Release();
                    }
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("cannot be started if it is not in the Disconnected state"))
                {
                    // Special case for the race condition exception
                    _logger.LogInformation("Stopped reconnect attempt because the connection state changed");
                    // Exit the loop - if it's already connecting or connected, we don't need to retry
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to reconnect (attempt {retryAttempt})");
                    
                    // If we've tried 10 times, wait longer before continuing
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
                // Stop the keep-alive timer
                StopKeepAliveTimer();
                
                // Stop any automatic reconnection
                _reconnectCts.CancelAsync().GetAwaiter().GetResult();
                
                // Stop the connection if it's not already stopped
                if (_connection != null && _connection.State != HubConnectionState.Disconnected)
                {
                    _connection.StopAsync().GetAwaiter().GetResult();
                }
                
                // Dispose of resources
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
                // Do not attempt to reconnect if maximum retries reached
                if (retryContext.PreviousRetryCount >= 5)
                {
                    return null;
                }
                
                // Exponential backoff: 1s, 2s, 4s, 8s, 16s
                return TimeSpan.FromSeconds(Math.Pow(2, retryContext.PreviousRetryCount));
            }
        }
    }
} 