using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using OpenAutomate.BotAgent.Service.Core;

namespace OpenAutomate.BotAgent.Service.Services
{
    /// <summary>
    /// Handles broadcasting messages to SignalR clients
    /// </summary>
    public class SignalRBroadcaster
    {
        private readonly ILogger<SignalRBroadcaster> _logger;
        private readonly IHubContext<BotAgentLocalHub> _hubContext;
        private readonly IServerCommunication _serverCommunication;
        
        /// <summary>
        /// Initializes a new instance of the SignalRBroadcaster class
        /// </summary>
        public SignalRBroadcaster(
            ILogger<SignalRBroadcaster> logger,
            IHubContext<BotAgentLocalHub> hubContext,
            IServerCommunication serverCommunication)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
            _serverCommunication = serverCommunication ?? throw new ArgumentNullException(nameof(serverCommunication));
            
            // Subscribe to server connection changes
            _serverCommunication.ConnectionChanged += OnServerConnectionChanged;
            
            _logger.LogInformation("SignalRBroadcaster initialized");
        }
        
        /// <summary>
        /// Broadcasts a message to all connected clients
        /// </summary>
        public async Task BroadcastAsync(string methodName, object data)
        {
            try
            {
                _logger.LogDebug("Broadcasting message: {Method}", methodName);
                await _hubContext.Clients.All.SendAsync(methodName, data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error broadcasting message: {Method}", methodName);
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