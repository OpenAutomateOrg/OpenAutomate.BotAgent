using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenAutomate.BotAgent.Service.Core;

namespace OpenAutomate.BotAgent.Service.Services
{
    /// <summary>
    /// Service to send execution status updates to the backend server
    /// </summary>
    public class SignalRBroadcaster
    {
        private readonly IServerCommunication _serverCommunication;
        private readonly ILogger<SignalRBroadcaster> _logger;

        // Standardized log message templates
        private static class LogMessages
        {
            public const string ExecutionStatusUpdate = "Sending execution status update for {ExecutionId}: {Status}";
            public const string ServerStatusUpdateSuccess = "Server status update successful for execution {ExecutionId}";
            public const string ServerStatusUpdateFailed = "Server status update failed for execution {ExecutionId}: {Error}";
        }

        /// <summary>
        /// Initializes a new instance of the SignalRBroadcaster class
        /// </summary>
        public SignalRBroadcaster(
            IServerCommunication serverCommunication,
            ILogger<SignalRBroadcaster> logger)
        {
            _serverCommunication = serverCommunication ?? throw new ArgumentNullException(nameof(serverCommunication));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            _logger.LogInformation("SignalRBroadcaster initialized for server communication only");
        }
        
        /// <summary>
        /// Sends execution status update to the backend server
        /// </summary>
        /// <param name="executionId">Execution ID</param>
        /// <param name="status">Current status</param>
        /// <param name="message">Optional status message</param>
        public async Task BroadcastExecutionStatusAsync(string executionId, string status, string message = null)
        {
            if (string.IsNullOrEmpty(executionId) || string.IsNullOrEmpty(status))
            {
                _logger.LogWarning("Invalid execution status update parameters: ExecutionId={ExecutionId}, Status={Status}", executionId, status);
                return;
            }

            _logger.LogInformation(LogMessages.ExecutionStatusUpdate, executionId, status);

            // Send status update to server
            await SendStatusToServerAsync(executionId, status, message);
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
    }
} 