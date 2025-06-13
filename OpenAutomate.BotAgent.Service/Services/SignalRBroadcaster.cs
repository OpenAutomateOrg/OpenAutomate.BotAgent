using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using OpenAutomate.BotAgent.Service.Core;

namespace OpenAutomate.BotAgent.Service.Services
{
    /// <summary>
    /// Service to broadcast execution status updates through SignalR
    /// </summary>
    public class SignalRBroadcaster
    {
        private readonly IHubContext<BotAgentLocalHub> _hubContext;
        private readonly IServerCommunication _serverCommunication;
        private readonly ILogger<SignalRBroadcaster> _logger;

        // Standardized log message templates
        private static class LogMessages
        {
            public const string ExecutionStatusBroadcast = "Broadcasting execution status for {ExecutionId}: {Status}";
            public const string LocalHubBroadcastSuccess = "Local hub broadcast successful for execution {ExecutionId}";
            public const string LocalHubBroadcastFailed = "Local hub broadcast failed for execution {ExecutionId}: {Error}";
            public const string ServerStatusUpdateSuccess = "Server status update successful for execution {ExecutionId}";
            public const string ServerStatusUpdateFailed = "Server status update failed for execution {ExecutionId}: {Error}";
        }

        /// <summary>
        /// Initializes a new instance of the SignalRBroadcaster class
        /// </summary>
        public SignalRBroadcaster(
            IHubContext<BotAgentLocalHub> hubContext,
            IServerCommunication serverCommunication,
            ILogger<SignalRBroadcaster> logger)
        {
            _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
            _serverCommunication = serverCommunication ?? throw new ArgumentNullException(nameof(serverCommunication));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // Subscribe to server connection changes
            _serverCommunication.ConnectionChanged += OnServerConnectionChanged;
            
            _logger.LogInformation("SignalRBroadcaster initialized");
        }
        
        /// <summary>
        /// Broadcasts execution status to both local clients and server
        /// </summary>
        /// <param name="executionId">Execution ID</param>
        /// <param name="status">Current status</param>
        /// <param name="message">Optional status message</param>
        public async Task BroadcastExecutionStatusAsync(string executionId, string status, string message = null)
        {
            if (string.IsNullOrEmpty(executionId) || string.IsNullOrEmpty(status))
            {
                _logger.LogWarning("Invalid execution status broadcast parameters: ExecutionId={ExecutionId}, Status={Status}", executionId, status);
                return;
            }

            _logger.LogInformation(LogMessages.ExecutionStatusBroadcast, executionId, status);

            var statusData = new
            {
                ExecutionId = executionId,
                Status = status,
                Message = message ?? string.Empty,
                Timestamp = DateTime.UtcNow
            };

            // Broadcast to local SignalR clients (UI, etc.)
            await BroadcastToLocalClientsAsync(statusData);

            // Send status update to server
            await SendStatusToServerAsync(executionId, status, message);
        }
        
        /// <summary>
        /// Broadcasts to local SignalR clients
        /// </summary>
        private async Task BroadcastToLocalClientsAsync(object statusData)
        {
            try
            {
                if (_hubContext?.Clients != null)
                {
                    await _hubContext.Clients.All.SendAsync("ExecutionStatusUpdate", statusData);
                    _logger.LogDebug(LogMessages.LocalHubBroadcastSuccess, statusData.GetType().GetProperty("ExecutionId")?.GetValue(statusData));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, LogMessages.LocalHubBroadcastFailed, 
                    statusData.GetType().GetProperty("ExecutionId")?.GetValue(statusData), ex.Message);
            }
        }
        
        /// <summary>
        /// Sends status update to the main server
        /// </summary>
        private async Task SendStatusToServerAsync(string executionId, string status, string message)
        {
            try
            {
                if (_serverCommunication?.IsConnected == true)
                {
                    await _serverCommunication.UpdateExecutionStatusAsync(executionId, status, message);
                    _logger.LogDebug(LogMessages.ServerStatusUpdateSuccess, executionId);
                }
                else
                {
                    _logger.LogDebug("Server communication not available, skipping server status update for execution {ExecutionId}", executionId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, LogMessages.ServerStatusUpdateFailed, executionId, ex.Message);
            }
        }
        
        /// <summary>
        /// Called when the server connection status changes
        /// </summary>
        private async void OnServerConnectionChanged(bool isConnected)
        {
            try
            {
                _logger.LogInformation("Broadcasting connection status change to clients: {IsConnected}", isConnected);
                await _hubContext.Clients.All.SendAsync("ConnectionStatusChanged", isConnected);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error broadcasting connection status change");
            }
        }
    }
} 