using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using OpenAutomate.BotAgent.Service.Core;

namespace OpenAutomate.BotAgent.Service.Services
{
    /// <summary>
    /// SignalR Hub for local communication with UI clients
    /// </summary>
    public class BotAgentLocalHub : Hub
    {
        private readonly ILogger<BotAgentLocalHub> _logger;
        private readonly IServerCommunication _serverCommunication;

        /// <summary>
        /// Initializes a new instance of the BotAgentLocalHub class
        /// </summary>
        public BotAgentLocalHub(
            ILogger<BotAgentLocalHub> logger,
            IServerCommunication serverCommunication)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serverCommunication = serverCommunication ?? throw new ArgumentNullException(nameof(serverCommunication));
            
            // Subscribe to server connection changes
            // We don't need to subscribe here because the BotAgentService
            // will handle broadcasting through a different approach
        }

        /// <summary>
        /// Called when a client connects to the hub
        /// </summary>
        public override async Task OnConnectedAsync()
        {
            _logger.LogInformation("UI client connected: {ConnectionId}", Context.ConnectionId);
            
            // Send the current connection status to the newly connected client
            await Clients.Caller.SendAsync("ConnectionStatusChanged", _serverCommunication.IsConnected);
            
            await base.OnConnectedAsync();
        }

        /// <summary>
        /// Called when a client disconnects from the hub
        /// </summary>
        public override async Task OnDisconnectedAsync(Exception exception)
        {
            _logger.LogInformation("UI client disconnected: {ConnectionId}", Context.ConnectionId);
            
            if (exception != null)
            {
                _logger.LogWarning(exception, "UI client disconnected with error");
            }
            
            await base.OnDisconnectedAsync(exception);
        }
    }
} 