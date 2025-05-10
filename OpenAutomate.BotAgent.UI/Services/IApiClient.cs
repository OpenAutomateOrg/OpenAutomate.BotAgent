using System.Threading.Tasks;
using OpenAutomate.BotAgent.UI.Models;

namespace OpenAutomate.BotAgent.UI.Services
{
    /// <summary>
    /// Interface for API client that communicates with the Bot Agent service
    /// </summary>
    public interface IApiClient
    {
        /// <summary>
        /// Gets the current configuration from the service
        /// </summary>
        Task<ConfigurationModel> GetConfigAsync();
        
        /// <summary>
        /// Updates the configuration
        /// </summary>
        Task<bool> UpdateConfigAsync(ConfigurationModel config);
        
        /// <summary>
        /// Gets the connection status
        /// </summary>
        Task<bool> GetConnectionStatusAsync();
        
        /// <summary>
        /// Connects to the server with the current configuration
        /// </summary>
        Task<bool> ConnectAsync();
        
        /// <summary>
        /// Disconnects from the server
        /// </summary>
        Task<bool> DisconnectAsync();
        
        /// <summary>
        /// Resets the cached connection status to force a fresh check on next request
        /// </summary>
        void ResetConnectionStatusCache();
    }
} 