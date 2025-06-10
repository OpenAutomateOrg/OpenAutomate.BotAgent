using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
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
        /// Updates the agent status on the server
        /// </summary>
        /// <param name="status">The new status (use AgentStatus constants)</param>
        /// <param name="executionId">Optional execution ID for context</param>
        public async Task UpdateStatusAsync(string status, string executionId = null)
        {
            if (!IsConnected)
            {
                _logger.LogWarning("Cannot update status to {Status}: Not connected", status);
                return;
            }
            
            try
            {
                _logger.LogInformation("Updating agent status to {Status}", status);
                await _signalRClient.SendStatusUpdateAsync(status, executionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating status to {Status}", status);
                // If we can't send the status update, we might be disconnected
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
                
                // Create request with machine key in the body
                var requestData = new { MachineKey = machineKey };
                var content = new StringContent(
                    JsonSerializer.Serialize(requestData),
                    Encoding.UTF8,
                    "application/json");
                
                // POST to the bot-agent assets endpoint
                var response = await _httpClient.PostAsync(
                    $"{serverUrl}/api/bot-agent/assets/key/{key}",
                    content);
                
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
                
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogWarning("Asset '{Key}' not found or bot agent not authorized", key);
                    return null;
                }
                
                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    _logger.LogWarning("Bot agent not authorized to access asset '{Key}'", key);
                    return null;
                }
                
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    _logger.LogWarning("Unauthorized: Invalid machine key for asset '{Key}'", key);
                    return null;
                }
                
                _logger.LogError("Unexpected response status code {StatusCode} for asset '{Key}'", 
                    response.StatusCode, key);
                response.EnsureSuccessStatusCode(); // This will throw
                return null; // We won't reach here
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
                
                // Create request with machine key in the body
                var requestData = new { MachineKey = machineKey };
                var content = new StringContent(
                    JsonSerializer.Serialize(requestData),
                    Encoding.UTF8,
                    "application/json");
                
                // POST to the bot-agent accessible assets endpoint
                var response = await _httpClient.PostAsync(
                    $"{serverUrl}/api/bot-agent/assets/accessible",
                    content);
                
                response.EnsureSuccessStatusCode();
                
                // Log the raw response for debugging
                string rawJson = await response.Content.ReadAsStringAsync();
                _logger.LogDebug("Raw JSON response: {Json}", rawJson);
                
                List<AssetInfo> accessibleAssets;
                try
                {
                    // Try to deserialize using the updated AssetInfo class
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    accessibleAssets = JsonSerializer.Deserialize<List<AssetInfo>>(rawJson, options);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Error deserializing asset list: {Message}", ex.Message);
                    _logger.LogDebug("Attempting to deserialize as dynamic JSON");
                    
                    // Fallback: we'll just extract the keys from the raw JSON
                    accessibleAssets = new List<AssetInfo>();
                    using (JsonDocument doc = JsonSerializer.Deserialize<JsonDocument>(rawJson))
                    {
                        foreach (JsonElement item in doc.RootElement.EnumerateArray())
                        {
                            if (item.TryGetProperty("key", out JsonElement keyElement) && 
                                keyElement.ValueKind == JsonValueKind.String)
                            {
                                accessibleAssets.Add(new AssetInfo { 
                                    Key = keyElement.GetString()
                                });
                            }
                        }
                    }
                }
                
                // Convert the list of asset info to a dictionary of key-value pairs
                var assetDictionary = new Dictionary<string, string>();
                if (accessibleAssets != null)
                {
                    foreach (var asset in accessibleAssets)
                    {
                        if (string.IsNullOrEmpty(asset.Key))
                        {
                            _logger.LogWarning("Skipping asset with null or empty key");
                            continue;
                        }
                        
                        // The actual asset values are not included in the accessible assets list
                        // We need to request each asset value individually
                        var assetValue = await GetAssetAsync(asset.Key, machineKey);
                        if (assetValue != null)
                        {
                            assetDictionary[asset.Key] = assetValue;
                        }
                    }
                    
                    _logger.LogInformation("Retrieved {Count} accessible assets", assetDictionary.Count);
                }
                
                return assetDictionary;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error getting accessible assets: {Message}", ex.Message);
                throw;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "JSON error processing accessible assets: {Message}", ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting accessible assets: {Message}", ex.Message);
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