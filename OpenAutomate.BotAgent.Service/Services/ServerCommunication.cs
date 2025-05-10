using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenAutomate.BotAgent.Service.Core;
using OpenAutomate.BotAgent.Service.Models;

namespace OpenAutomate.BotAgent.Service.Services
{
    /// <summary>
    /// Service for communication with the OpenAutomate server
    /// </summary>
    public class ServerCommunication : IServerCommunication, IDisposable
    {
        private readonly ILogger<ServerCommunication> _logger;
        private readonly IConfigurationService _configService;
        private readonly BotAgentSignalRClient _signalRClient;
        private readonly HttpClient _httpClient;
        private bool _isConnected;
        private bool _disposed;

        /// <summary>
        /// Event raised when connection status changes
        /// </summary>
        public event Action<bool> ConnectionChanged;

        /// <summary>
        /// Gets whether the service is connected to the server
        /// </summary>
        public bool IsConnected 
        { 
            get => _isConnected;
            private set
            {
                if (_isConnected != value)
                {
                    _isConnected = value;
                    _logger.LogInformation("Connection status changed to {IsConnected}", _isConnected);
                    ConnectionChanged?.Invoke(_isConnected);
                }
            }
        }
        
        /// <summary>
        /// Initializes a new instance of the <see cref="ServerCommunication"/> class
        /// </summary>
        public ServerCommunication(
            ILogger<ServerCommunication> logger,
            IConfigurationService configService,
            BotAgentSignalRClient signalRClient)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _signalRClient = signalRClient ?? throw new ArgumentNullException(nameof(signalRClient));
            
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            
            // Subscribe to the SignalR client connection events
            _signalRClient.Connected += OnSignalRConnected;
            _signalRClient.Disconnected += OnSignalRDisconnected;
        }
        
        /// <summary>
        /// Connects to the server
        /// </summary>
        public async Task ConnectAsync()
        {
            try
            {
                _logger.LogInformation("Connecting to server...");
                await _signalRClient.InitializeAsync();
                
                // Connection status will be updated via the SignalR client events
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error connecting to server");
                IsConnected = false;
                throw;
            }
        }
        
        /// <summary>
        /// Disconnects from the server
        /// </summary>
        public async Task DisconnectAsync()
        {
            try
            {
                _logger.LogInformation("Disconnecting from server...");
                await _signalRClient.DisconnectAsync();
                
                // Connection status will be updated via the SignalR client events
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disconnecting from server");
                throw;
            }
            finally
            {
                // Ensure we set the status to disconnected
                IsConnected = false;
            }
        }
        
        /// <summary>
        /// Sends a health check to the server
        /// </summary>
        public async Task SendHealthCheckAsync()
        {
            if (!IsConnected)
            {
                _logger.LogWarning("Cannot send health check: Not connected");
                return;
            }
            
            try
            {
                await _signalRClient.SendStatusUpdateAsync("Ready");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending health check");
                // If we can't send the health check, we might be disconnected
                IsConnected = false;
            }
        }
        
        /// <summary>
        /// Gets an asset from the server
        /// </summary>
        public async Task<string> GetAssetAsync(string key, string machineKey)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentNullException(nameof(key));
            }
            
            if (string.IsNullOrEmpty(machineKey))
            {
                throw new ArgumentNullException(nameof(machineKey));
            }
            
            var config = _configService.GetConfiguration();
            if (string.IsNullOrEmpty(config.ServerUrl))
            {
                throw new InvalidOperationException("Server URL not configured");
            }
            
            try
            {
                var serverUrl = config.ServerUrl.TrimEnd('/');
                var response = await _httpClient.GetAsync(
                    $"{serverUrl}/api/assets/{key}?machineKey={machineKey}");
                
                response.EnsureSuccessStatusCode();
                
                return await response.Content.ReadAsStringAsync();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error getting asset {Key}", key);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting asset {Key}", key);
                throw;
            }
        }
        
        /// <summary>
        /// Gets all assets from the server
        /// </summary>
        public async Task<IDictionary<string, string>> GetAllAssetsAsync(string machineKey)
        {
            if (string.IsNullOrEmpty(machineKey))
            {
                throw new ArgumentNullException(nameof(machineKey));
            }
            
            var config = _configService.GetConfiguration();
            if (string.IsNullOrEmpty(config.ServerUrl))
            {
                throw new InvalidOperationException("Server URL not configured");
            }
            
            try
            {
                var serverUrl = config.ServerUrl.TrimEnd('/');
                var response = await _httpClient.GetAsync(
                    $"{serverUrl}/api/assets?machineKey={machineKey}");
                
                response.EnsureSuccessStatusCode();
                
                var assets = await response.Content.ReadFromJsonAsync<IDictionary<string, string>>();
                return assets ?? new Dictionary<string, string>();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error getting all assets");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all assets");
                throw;
            }
        }
        
        /// <summary>
        /// Called when the SignalR client connects
        /// </summary>
        private void OnSignalRConnected()
        {
            IsConnected = true;
        }

        /// <summary>
        /// Called when the SignalR client disconnects
        /// </summary>
        private void OnSignalRDisconnected()
        {
            IsConnected = false;
        }
        
        /// <summary>
        /// Disposes resources
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    // Unsubscribe from events
                    if (_signalRClient != null)
                    {
                        _signalRClient.Connected -= OnSignalRConnected;
                        _signalRClient.Disconnected -= OnSignalRDisconnected;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during resource cleanup");
                }
                
                _disposed = true;
            }
        }
    }
} 