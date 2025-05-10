using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using OpenAutomate.BotAgent.UI.Models;
using OpenAutomate.BotAgent.Service.Models;
using OpenAutomate.BotAgent.UI.Services;

namespace OpenAutomate.BotAgent.UI.Services
{
    /// <summary>
    /// Implementation of the API client that communicates with the Bot Agent service
    /// </summary>
    public class ApiClient : IApiClient, IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly int _apiPort = 8081; // Default port for the Bot Agent service
        private bool _disposed;
        
        // Throttling for health and status checks
        private DateTime _lastHealthCheckTime = DateTime.MinValue;
        private DateTime _lastStatusCheckTime = DateTime.MinValue;
        private TimeSpan _healthCheckThrottle = TimeSpan.FromSeconds(30); // Increase from 5s to 30s
        private TimeSpan _statusCheckThrottle = TimeSpan.FromSeconds(30); // Increase from 5s to 30s
        private bool? _cachedHealthStatus = null;
        private bool? _cachedConnectionStatus = null;
        
        /// <summary>
        /// Initializes a new instance of the ApiClient class
        /// </summary>
        public ApiClient()
        {
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri($"http://localhost:{_apiPort}/api/"),
                Timeout = TimeSpan.FromSeconds(15)
            };
        }
        
        /// <inheritdoc/>
        public async Task<ConfigurationModel> GetConfigAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("config");
                
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    // Config not found on server, return a default one
                    LoggingService.Warning("Configuration not found on server, using default");
                    return CreateDefaultConfig();
                }
                
                response.EnsureSuccessStatusCode();
                
                // The service uses BotAgentConfig, so we need to convert it to our UI model
                var serviceConfig = await response.Content.ReadFromJsonAsync<BotAgentConfig>();
                return serviceConfig != null 
                    ? ConvertServiceConfigToUiModel(serviceConfig) 
                    : CreateDefaultConfig();
            }
            catch (Exception ex)
            {
                LoggingService.Error(ex, "Error getting configuration from service");
                return CreateDefaultConfig();
            }
        }
        
        /// <inheritdoc/>
        public async Task<bool> UpdateConfigAsync(ConfigurationModel config)
        {
            try
            {
                // Convert our UI model to the service model
                var serviceConfig = ConvertUiModelToServiceConfig(config);
                
                var content = new StringContent(
                    JsonSerializer.Serialize(serviceConfig),
                    Encoding.UTF8,
                    "application/json");
                    
                var response = await _httpClient.PostAsync("config", content);
                
                if (response.IsSuccessStatusCode)
                {
                    LoggingService.Information("Configuration updated successfully");
                    return true;
                }
                else
                {
                    LoggingService.Warning("Failed to update configuration: {StatusCode}", response.StatusCode);
                    return false;
                }
            }
            catch (Exception ex)
            {
                LoggingService.Error(ex, "Error updating configuration");
                return false;
            }
        }
        
        /// <inheritdoc/>
        public async Task<bool> GetConnectionStatusAsync()
        {
            try
            {
                LoggingService.Debug("Checking connection status...");
                
                // First check if the API server is responsive, with throttling
                bool healthCheckResult;
                var now = DateTime.UtcNow;
                
                if (_cachedHealthStatus.HasValue && now - _lastHealthCheckTime < _healthCheckThrottle)
                {
                    // Use cached result if within throttle period
                    healthCheckResult = _cachedHealthStatus.Value;
                    LoggingService.Debug("Using cached health check result: {Result}", 
                        healthCheckResult ? "Healthy" : "Unhealthy");
                }
                else
                {
                    // Time to perform a real health check
                    LoggingService.Debug("Performing fresh health check");
                    try
                    {
                        var healthResponse = await _httpClient.GetAsync("health");
                        healthCheckResult = healthResponse.IsSuccessStatusCode;
                        
                        if (healthCheckResult)
                        {
                            LoggingService.Debug("Health check successful");
                        }
                        else
                        {
                            LoggingService.Warning("Health check failed with status code: {StatusCode}", 
                                healthResponse.StatusCode);
                        }
                        
                        // Update cache
                        _lastHealthCheckTime = now;
                        _cachedHealthStatus = healthCheckResult;
                    }
                    catch (Exception ex)
                    {
                        LoggingService.Warning("Health check failed with exception: {Error}", ex.Message);
                        _lastHealthCheckTime = now;
                        _cachedHealthStatus = false;
                        healthCheckResult = false;
                    }
                }
                
                if (!healthCheckResult)
                {
                    LoggingService.Warning("API server is not responsive, reporting as disconnected");
                    _cachedConnectionStatus = false;
                    return false;
                }
                
                // Then check the actual connection status, with throttling
                if (_cachedConnectionStatus.HasValue && now - _lastStatusCheckTime < _statusCheckThrottle)
                {
                    // Use cached result if within throttle period
                    var cachedStatus = _cachedConnectionStatus.Value;
                    LoggingService.Debug("Using cached connection status: {Status}", 
                        cachedStatus ? "Connected" : "Disconnected");
                    return cachedStatus;
                }
                
                // Time to perform a real status check
                LoggingService.Debug("Performing fresh connection status check");
                try
                {
                    var response = await _httpClient.GetAsync("status");
                    if (!response.IsSuccessStatusCode)
                    {
                        LoggingService.Warning("Status check failed with status code: {StatusCode}", 
                            response.StatusCode);
                            
                        _lastStatusCheckTime = now;
                        _cachedConnectionStatus = false;
                        return false;
                    }
                    
                    var responseContent = await response.Content.ReadAsStringAsync();
                    LoggingService.Debug("Status response received: {Content}", responseContent);
                    
                    var statusData = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    
                    var isConnected = statusData.TryGetProperty("isConnected", out var connectedProp) && 
                                     connectedProp.GetBoolean();
                    
                    // Update cache
                    _lastStatusCheckTime = now;
                    _cachedConnectionStatus = isConnected;
                    
                    LoggingService.Information("Connection status check result: {Status}", 
                        isConnected ? "Connected" : "Disconnected");
                    
                    return isConnected;
                }
                catch (Exception ex)
                {
                    LoggingService.Warning("Connection status check failed with exception: {Error}", ex.Message);
                    _lastStatusCheckTime = now;
                    _cachedConnectionStatus = false;
                    return false;
                }
            }
            catch (Exception ex)
            {
                LoggingService.Error(ex, "Unexpected error checking connection status");
                return false;
            }
        }
        
        /// <inheritdoc/>
        public async Task<bool> ConnectAsync()
        {
            try
            {
                LoggingService.Information("Connecting to server...");
                var response = await _httpClient.PostAsync("connect", null);
                
                if (response.IsSuccessStatusCode)
                {
                    LoggingService.Information("Connected to server successfully");
                    
                    // Explicitly update the cached connection status
                    _cachedConnectionStatus = true;
                    _lastStatusCheckTime = DateTime.UtcNow;
                    
                    return true;
                }
                else
                {
                    // Read error response for more detailed logging
                    string errorContent = await response.Content.ReadAsStringAsync();
                    LoggingService.Warning("Failed to connect to server: Status {StatusCode}, Response: {ErrorContent}", 
                        response.StatusCode, errorContent);
                    
                    // Explicitly update the cached connection status
                    _cachedConnectionStatus = false;
                    _lastStatusCheckTime = DateTime.UtcNow;
                    
                    return false;
                }
            }
            catch (Exception ex)
            {
                LoggingService.Error(ex, "Error connecting to server: {ErrorMessage}", ex.Message);
                
                // Explicitly update the cached connection status
                _cachedConnectionStatus = false;
                _lastStatusCheckTime = DateTime.UtcNow;
                
                return false;
            }
        }
        
        /// <inheritdoc/>
        public async Task<bool> DisconnectAsync()
        {
            try
            {
                LoggingService.Information("Disconnecting from server...");
                var response = await _httpClient.PostAsync("disconnect", null);
                
                if (response.IsSuccessStatusCode)
                {
                    LoggingService.Information("Disconnected from server successfully");
                    
                    // Explicitly update the cached connection status
                    _cachedConnectionStatus = false;
                    _lastStatusCheckTime = DateTime.UtcNow;
                    
                    return true;
                }
                else
                {
                    // Read error response for more detailed logging
                    string errorContent = await response.Content.ReadAsStringAsync();
                    LoggingService.Warning("Failed to disconnect from server: Status {StatusCode}, Response: {ErrorContent}",
                        response.StatusCode, errorContent);
                    
                    return false;
                }
            }
            catch (Exception ex)
            {
                LoggingService.Error(ex, "Error disconnecting from server: {ErrorMessage}", ex.Message);
                return false;
            }
        }
        
        /// <summary>
        /// Converts a service configuration to a UI model
        /// </summary>
        private ConfigurationModel ConvertServiceConfigToUiModel(BotAgentConfig serviceConfig)
        {
            return new ConfigurationModel
            {
                ServerUrl = serviceConfig.ServerUrl,
                MachineKey = serviceConfig.MachineKey,
                MachineName = Environment.MachineName, // This is not stored in the service config
                LoggingLevel = serviceConfig.LogLevel
            };
        }
        
        /// <summary>
        /// Converts a UI model to a service configuration
        /// </summary>
        private BotAgentConfig ConvertUiModelToServiceConfig(ConfigurationModel uiModel)
        {
            // Try to get current config first to preserve values like AutoStart
            try
            {
                var currentConfigTask = _httpClient.GetAsync("config");
                currentConfigTask.Wait(TimeSpan.FromSeconds(2));
                
                if (currentConfigTask.IsCompleted && !currentConfigTask.IsFaulted)
                {
                    var response = currentConfigTask.Result;
                    if (response.IsSuccessStatusCode)
                    {
                        var currentConfig = response.Content.ReadFromJsonAsync<BotAgentConfig>().Result;
                        if (currentConfig != null)
                        {
                            LoggingService.Debug("Preserving AutoStart setting: {AutoStart}", currentConfig.AutoStart);
                            
                            // Return with preserved AutoStart setting
                            return new BotAgentConfig
                            {
                                ServerUrl = uiModel.ServerUrl,
                                MachineKey = uiModel.MachineKey,
                                LogLevel = uiModel.LoggingLevel,
                                ApiPort = _apiPort,
                                AutoStart = currentConfig.AutoStart // Preserve AutoStart setting
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.Debug("Could not fetch current config to preserve AutoStart: {Error}", ex.Message);
                // Continue with default behavior
            }
            
            // Default behavior if we couldn't get the current config
            return new BotAgentConfig
            {
                ServerUrl = uiModel.ServerUrl,
                MachineKey = uiModel.MachineKey,
                LogLevel = uiModel.LoggingLevel,
                ApiPort = _apiPort,
                AutoStart = true // Default to true
            };
        }
        
        /// <summary>
        /// Creates a default configuration with reasonable values
        /// </summary>
        private ConfigurationModel CreateDefaultConfig()
        {
            return new ConfigurationModel
            {
                MachineName = Environment.MachineName,
                LoggingLevel = "INFO"
            };
        }
        
        /// <summary>
        /// Disposes resources
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _httpClient.Dispose();
                _disposed = true;
            }
        }
        
        /// <summary>
        /// Resets the cached connection status to force a fresh check on next request
        /// </summary>
        public void ResetConnectionStatusCache()
        {
            LoggingService.Debug("Resetting connection status cache");
            _cachedConnectionStatus = null;
            _cachedHealthStatus = null;
            _lastStatusCheckTime = DateTime.MinValue;
            _lastHealthCheckTime = DateTime.MinValue;
        }
    }
} 