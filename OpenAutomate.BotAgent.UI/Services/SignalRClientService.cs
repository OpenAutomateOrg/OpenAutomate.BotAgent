using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using OpenAutomate.BotAgent.UI.Models;

namespace OpenAutomate.BotAgent.UI.Services
{
    /// <summary>
    /// SignalR client for real-time communication with the Bot Agent service
    /// </summary>
    public class SignalRClientService : IDisposable
    {
        private HubConnection _connection;
        private bool _disposed;
        private int _apiPort = 8080; // Default port
        
        /// <summary>
        /// Event raised when connection status changes
        /// </summary>
        public event Action<bool> ConnectionStatusChanged;
        
        /// <summary>
        /// Event raised when connection to the hub is established
        /// </summary>
        public event Action Connected;
        
        /// <summary>
        /// Event raised when connection to the hub is lost
        /// </summary>
        public event Action Disconnected;
        
        /// <summary>
        /// Gets whether the client is connected to the hub
        /// </summary>
        public bool IsConnected => _connection?.State == HubConnectionState.Connected;
        
        /// <summary>
        /// Initializes the SignalR connection
        /// </summary>
        public async Task InitializeAsync(int apiPort = 8080)
        {
            try
            {
                _apiPort = apiPort;
                
                LoggingService.Information("Initializing SignalR connection to Bot Agent service on port {ApiPort}", _apiPort);
                
                _connection = new HubConnectionBuilder()
                    .WithUrl($"http://localhost:{_apiPort}/hubs/client")
                    .WithAutomaticReconnect()
                    .Build();
                
                // Register event handlers
                _connection.On<bool>("ConnectionStatusChanged", isConnected =>
                {
                    LoggingService.Information("Received connection status update: {IsConnected}", isConnected ? "Connected" : "Disconnected");
                    ConnectionStatusChanged?.Invoke(isConnected);
                });
                
                // Set up connection events
                _connection.Reconnecting += error =>
                {
                    LoggingService.Warning("SignalR connection lost. Attempting to reconnect... Error: {Error}", error?.Message);
                    Disconnected?.Invoke();
                    return Task.CompletedTask;
                };
                
                _connection.Reconnected += connectionId =>
                {
                    LoggingService.Information("SignalR reconnected: {ConnectionId}", connectionId);
                    Connected?.Invoke();
                    return Task.CompletedTask;
                };
                
                _connection.Closed += error =>
                {
                    LoggingService.Warning("SignalR connection closed. Error: {Error}", error?.Message);
                    Disconnected?.Invoke();
                    return Task.CompletedTask;
                };
                
                // Start the connection
                await _connection.StartAsync();
                LoggingService.Information("Connected to SignalR hub on port {ApiPort}", _apiPort);
                Connected?.Invoke();
            }
            catch (Exception ex)
            {
                LoggingService.Error(ex, "Failed to connect to SignalR hub");
                Disconnected?.Invoke();
            }
        }
        
        /// <summary>
        /// Restarts the SignalR connection with a new port
        /// </summary>
        public async Task RestartWithNewPortAsync(int apiPort)
        {
            if (_connection != null)
            {
                try
                {
                    await _connection.StopAsync();
                    await _connection.DisposeAsync();
                    _connection = null;
                }
                catch (Exception ex)
                {
                    LoggingService.Warning(ex.Message, "Error disposing previous SignalR connection");
                }
            }
            
            await InitializeAsync(apiPort);
        }
        
        /// <summary>
        /// Disposes the SignalR client
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    _connection?.StopAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                    _connection?.DisposeAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    LoggingService.Error(ex, "Error disposing SignalR client");
                }
                
                _disposed = true;
            }
        }
    }
} 