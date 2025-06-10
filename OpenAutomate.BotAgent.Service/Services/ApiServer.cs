using System;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR;
using OpenAutomate.BotAgent.Service.Core;
using OpenAutomate.BotAgent.Service.Models;
using System.IO;
using System.Collections.Generic;

namespace OpenAutomate.BotAgent.Service.Services
{
    /// <summary>
    /// Implementation of the API server that listens for HTTP requests from the UI and other clients
    /// </summary>
    public class ApiServer : IApiServer, IDisposable
    {
        private readonly ILogger<ApiServer> _logger;
        private readonly IConfigurationService _configService;
        private readonly IServerCommunication _serverCommunication;
        private readonly IMachineKeyManager _machineKeyManager;
        private readonly IAssetManager _assetManager;
        private HttpListener _listener;
        private bool _isRunning;
        private bool _isDisposed;
        
        /// <summary>
        /// Initializes a new instance of the ApiServer class
        /// </summary>
        public ApiServer(
            ILogger<ApiServer> logger,
            IConfigurationService configService,
            IServerCommunication serverCommunication,
            IMachineKeyManager machineKeyManager,
            IAssetManager assetManager)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _serverCommunication = serverCommunication ?? throw new ArgumentNullException(nameof(serverCommunication));
            _machineKeyManager = machineKeyManager ?? throw new ArgumentNullException(nameof(machineKeyManager));
            _assetManager = assetManager ?? throw new ArgumentNullException(nameof(assetManager));
        }
        
        /// <summary>
        /// Starts the API server
        /// </summary>
        public async Task StartAsync()
        {
            if (_isRunning)
                return;
                
            try
            {
                var config = _configService.GetConfiguration();
                var port = config.ApiPort;
                
                _logger.LogInformation("Starting API server on port {Port}", port);
                
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{port}/api/");
                _listener.Start();
                
                _isRunning = true;
                
                // Start listening for requests in a background task
                await Task.Factory.StartNew(ListenForRequestsAsync, TaskCreationOptions.LongRunning);
                
                _logger.LogInformation("API server started successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start API server");
                throw;
            }
        }
        
        /// <summary>
        /// Stops the API server
        /// </summary>
        public Task StopAsync()
        {
            if (!_isRunning || _listener == null)
                return Task.CompletedTask;
                
            try
            {
                _isRunning = false;
                _listener.Stop();
                _listener.Close();
                _listener = null;
                
                _logger.LogInformation("API server stopped");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping API server");
            }
            
            return Task.CompletedTask;
        }
        
        /// <summary>
        /// Listens for incoming HTTP requests
        /// </summary>
        private async Task ListenForRequestsAsync()
        {
            try
            {
                while (_isRunning)
                {
                    var context = await _listener.GetContextAsync();
                    
                    // Process the request in a separate task
                    _ = Task.Run(() => ProcessRequestAsync(context));
                }
            }
            catch (Exception ex) when (!_isRunning)
            {
                // Ignore exceptions when shutting down
                _logger.LogDebug("API server listener stopped");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in API server listener");
            }
        }
        
        /// <summary>
        /// Processes an HTTP request
        /// </summary>
        private async Task ProcessRequestAsync(HttpListenerContext context)
        {
            try
            {
                var request = context.Request;
                var response = context.Response;
                
                // Extract the endpoint path
                var path = request.Url.AbsolutePath.ToLowerInvariant();
                var method = request.HttpMethod.ToUpperInvariant();
                
                _logger.LogDebug("Received {Method} request for {Path}", method, path);
                
                // Route the request to the appropriate handler
                switch (path)
                {
                    case "/api/health":
                        await HandleHealthCheckAsync(request, response);
                        break;
                        
                    case "/api/status":
                        await HandleStatusAsync(request, response);
                        break;
                        
                    case "/api/config":
                        if (method == "GET")
                            await HandleGetConfigAsync(request, response);
                        else if (method == "POST")
                            await HandleUpdateConfigAsync(request, response);
                        else
                            SendMethodNotAllowed(response);
                        break;
                        
                    case "/api/connect":
                        if (method == "POST"){
                            _logger.LogInformation("Received connect request");
                            await HandleConnectAsync(request, response);
                        }
                        else
                            SendMethodNotAllowed(response);
                        break;
                        
                    case "/api/disconnect":
                        if (method == "POST"){
                            _logger.LogInformation("Received disconnect request");
                            await HandleDisconnectAsync(request, response);
                        }
                        else
                            SendMethodNotAllowed(response);
                        break;
                        
                    // Asset endpoints
                    case "/api/assets":
                        if (method == "GET")
                            await HandleGetAssetsAsync(request, response);
                        else
                            SendMethodNotAllowed(response);
                        break;
                        
                    default:
                        // Check if the path matches /api/assets/{key}
                        if (path.StartsWith("/api/assets/") && method == "GET")
                        {
                            var key = path.Substring("/api/assets/".Length);
                            if (!string.IsNullOrEmpty(key))
                            {
                                await HandleGetAssetByKeyAsync(request, response, key);
                                break;
                            }
                        }
                        
                        SendNotFound(response);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing API request");
                
                try
                {
                    // Send error response
                    SendErrorResponse(context.Response, ex.Message);
                }
                catch
                {
                    // Ignore failures when sending error response
                }
            }
            finally
            {
                try
                {
                    // Ensure the response is closed
                    context.Response.Close();
                }
                catch
                {
                    // Ignore failures when closing the response
                }
            }
        }
        
        #region Request Handlers
        
        /// <summary>
        /// Handles health check requests
        /// </summary>
        private async Task HandleHealthCheckAsync(HttpListenerRequest request, HttpListenerResponse response)
        {
            await SendJsonResponseAsync(response, new { status = "healthy" });
        }
        
        /// <summary>
        /// Handles status requests
        /// </summary>
        private async Task HandleStatusAsync(HttpListenerRequest request, HttpListenerResponse response)
        {
            var isConnected = _serverCommunication.IsConnected;
            await SendJsonResponseAsync(response, new { isConnected });
        }
        
        /// <summary>
        /// Handles configuration get requests
        /// </summary>
        private async Task HandleGetConfigAsync(HttpListenerRequest request, HttpListenerResponse response)
        {
            var config = _configService.GetConfiguration();
            
            // Return a copy of the configuration
            var result = new BotAgentConfig
            {
                ServerUrl = config.ServerUrl,
                MachineKey = config.MachineKey,
                AutoStart = config.AutoStart,
                LogLevel = config.LogLevel,
                ApiPort = config.ApiPort
            };
            
            await SendJsonResponseAsync(response, result);
        }
        
        /// <summary>
        /// Handles configuration update requests
        /// </summary>
        private async Task HandleUpdateConfigAsync(HttpListenerRequest request, HttpListenerResponse response)
        {
            // Read the request body
            using var reader = new System.IO.StreamReader(request.InputStream, request.ContentEncoding);
            var json = await reader.ReadToEndAsync();
            
            try
            {
                // Deserialize the config
                var newConfig = JsonSerializer.Deserialize<BotAgentConfig>(json);
                
                if (newConfig == null)
                {
                    SendBadRequest(response, "Invalid configuration data");
                    return;
                }
                
                // Get the current config to preserve values not included in the update
                var currentConfig = _configService.GetConfiguration();
                
                // Update the configuration
                var updatedConfig = new BotAgentConfig
                {
                    ServerUrl = newConfig.ServerUrl ?? currentConfig.ServerUrl,
                    MachineKey = newConfig.MachineKey ?? currentConfig.MachineKey,
                    AutoStart = newConfig.AutoStart,
                    LogLevel = newConfig.LogLevel ?? currentConfig.LogLevel,
                    ApiPort = newConfig.ApiPort > 0 ? newConfig.ApiPort : currentConfig.ApiPort
                };
                
                // Store the updated config
                _configService.SaveConfiguration(updatedConfig);
                
                // Update the machine key if provided
                if (!string.IsNullOrEmpty(newConfig.MachineKey))
                {
                    _machineKeyManager.SetMachineKey(newConfig.MachineKey);
                }
                
                await SendJsonResponseAsync(response, new { success = true });
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Invalid JSON in configuration update");
                SendBadRequest(response, "Invalid JSON format");
            }
        }
        
        /// <summary>
        /// Handles connect requests
        /// </summary>
        private async Task HandleConnectAsync(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (_serverCommunication.IsConnected)
            {
                await SendJsonResponseAsync(response, new { success = true, message = "Already connected" });
                return;
            }
            
            try
            {
                // Re-enable AutoStart when user manually connects
                var config = _configService.GetConfiguration();
                config.AutoStart = true;
                _configService.SaveConfiguration(config);
                
                _logger.LogInformation("User initiated connection, AutoStart set to true");
                
                await _serverCommunication.ConnectAsync();
                await SendJsonResponseAsync(response, new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error connecting to server");
                SendErrorResponse(response, $"Error connecting to server: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Handles disconnect requests
        /// </summary>
        private async Task HandleDisconnectAsync(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (!_serverCommunication.IsConnected)
            {
                await SendJsonResponseAsync(response, new { success = true, message = "Already disconnected" });
                return;
            }
            
            try
            {
                // Set AutoStart to false to prevent automatic reconnection
                var config = _configService.GetConfiguration();
                config.AutoStart = false;
                _configService.SaveConfiguration(config);
                
                _logger.LogInformation("User initiated disconnect, AutoStart set to false");
                
                await _serverCommunication.DisconnectAsync();
                await SendJsonResponseAsync(response, new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disconnecting from server");
                SendErrorResponse(response, $"Error disconnecting from server: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Handles a request to get all accessible assets
        /// </summary>
        private async Task HandleGetAssetsAsync(HttpListenerRequest request, HttpListenerResponse response)
        {
            try
            {
                // Get all asset keys
                var assetKeys = await _assetManager.GetAllAssetKeysAsync();
                
                // Return the asset keys
                await SendJsonResponseAsync(response, assetKeys);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Unable to get assets: {Message}", ex.Message);
                SendErrorResponse(response, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting assets");
                SendErrorResponse(response, "An error occurred while retrieving assets");
            }
        }
        
        /// <summary>
        /// Handles a request to get an asset by key
        /// </summary>
        private async Task HandleGetAssetByKeyAsync(HttpListenerRequest request, HttpListenerResponse response, string key)
        {
            try
            {
                // Get the asset value
                var assetValue = await _assetManager.GetAssetAsync(key);
                
                // Send the asset value as plain text
                response.ContentType = "text/plain";
                response.StatusCode = (int)HttpStatusCode.OK;
                
                var bytes = Encoding.UTF8.GetBytes(assetValue);
                response.ContentLength64 = bytes.Length;
                await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning(ex, "Asset not found: {Message}", ex.Message);
                SendErrorResponse(response, ex.Message, HttpStatusCode.NotFound);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Unable to get asset: {Message}", ex.Message);
                SendErrorResponse(response, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting asset '{Key}'", key);
                SendErrorResponse(response, $"An error occurred while retrieving asset '{key}'");
            }
        }
        
        #endregion
        
        #region Response Helpers
        
        /// <summary>
        /// Sends a JSON response
        /// </summary>
        private async Task SendJsonResponseAsync<T>(HttpListenerResponse response, T data, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            response.StatusCode = (int)statusCode;
            response.ContentType = "application/json";
            
            var json = JsonSerializer.Serialize(data);
            var bytes = Encoding.UTF8.GetBytes(json);
            
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        }
        
        /// <summary>
        /// Sends a not found response
        /// </summary>
        private void SendNotFound(HttpListenerResponse response)
        {
            response.StatusCode = (int)HttpStatusCode.NotFound;
            response.ContentType = "application/json";
            
            var json = JsonSerializer.Serialize(new { error = "Endpoint not found" });
            var bytes = Encoding.UTF8.GetBytes(json);
            
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes, 0, bytes.Length);
        }
        
        /// <summary>
        /// Sends a method not allowed response
        /// </summary>
        private void SendMethodNotAllowed(HttpListenerResponse response)
        {
            response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
            response.ContentType = "application/json";
            
            var json = JsonSerializer.Serialize(new { error = "Method not allowed" });
            var bytes = Encoding.UTF8.GetBytes(json);
            
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes, 0, bytes.Length);
        }
        
        /// <summary>
        /// Sends a bad request response
        /// </summary>
        private void SendBadRequest(HttpListenerResponse response, string message)
        {
            response.StatusCode = (int)HttpStatusCode.BadRequest;
            response.ContentType = "application/json";
            
            var json = JsonSerializer.Serialize(new { error = message });
            var bytes = Encoding.UTF8.GetBytes(json);
            
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes, 0, bytes.Length);
        }
        
        /// <summary>
        /// Sends an error response
        /// </summary>
        private void SendErrorResponse(HttpListenerResponse response, string message, HttpStatusCode statusCode = HttpStatusCode.InternalServerError)
        {
            try
            {
                response.StatusCode = (int)statusCode;
                response.ContentType = "application/json";
                
                var error = new { error = message };
                var json = JsonSerializer.Serialize(error);
                
                var bytes = Encoding.UTF8.GetBytes(json);
                response.ContentLength64 = bytes.Length;
                response.OutputStream.Write(bytes, 0, bytes.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending error response");
            }
        }
        
        #endregion
        
        /// <summary>
        /// Disposes resources
        /// </summary>
        public void Dispose()
        {
            if (!_isDisposed)
            {
                // Stop the listener if it's running
                if (_isRunning && _listener != null)
                {
                    _listener.Stop();
                    _listener.Close();
                }
                
                _isDisposed = true;
            }
        }
    }
} 