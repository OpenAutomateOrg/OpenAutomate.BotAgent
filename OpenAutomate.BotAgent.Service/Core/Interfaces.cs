using System.Threading;
using System.Threading.Tasks;
using OpenAutomate.BotAgent.Service.Models;
using System;
using System.Collections.Generic;

namespace OpenAutomate.BotAgent.Service.Core
{
    /// <summary>
    /// Interface for asset management
    /// </summary>
    public interface IAssetManager
    {
        /// <summary>
        /// Gets an asset by key
        /// </summary>
        Task<string> GetAssetAsync(string key);
        
        /// <summary>
        /// Gets all available asset keys
        /// </summary>
        Task<IEnumerable<string>> GetAllAssetKeysAsync();
        
        /// <summary>
        /// Synchronizes assets with the server
        /// </summary>
        Task SyncAssetsAsync();
    }
    
    /// <summary>
    /// Interface for execution management
    /// </summary>
    public interface IExecutionManager
    {
        /// <summary>
        /// Starts execution of an automation package
        /// </summary>
        Task<string> StartExecutionAsync(object executionData);
        
        /// <summary>
        /// Cancels an execution
        /// </summary>
        Task CancelExecutionAsync(string executionId);
        
        /// <summary>
        /// Sends status updates for all active executions
        /// </summary>
        Task SendStatusUpdatesAsync();
        
        /// <summary>
        /// Checks if there are any active executions
        /// </summary>
        /// <returns>True if there are active executions, false otherwise</returns>
        Task<bool> HasActiveExecutionsAsync();
    }
    
    /// <summary>
    /// Interface for communication with the server
    /// </summary>
    public interface IServerCommunication
    {
        /// <summary>
        /// Event raised when connection status changes
        /// </summary>
        event Action<bool> ConnectionChanged;
        
        /// <summary>
        /// Gets whether the service is connected to the server
        /// </summary>
        bool IsConnected { get; }
        
        /// <summary>
        /// Connects to the server
        /// </summary>
        Task ConnectAsync();
        
        /// <summary>
        /// Disconnects from the server
        /// </summary>
        Task DisconnectAsync();
        
        /// <summary>
        /// Sends a health check to the server
        /// </summary>
        Task SendHealthCheckAsync();
        
        /// <summary>
        /// Updates the agent status on the server
        /// </summary>
        /// <param name="status">The new status (use AgentStatus constants)</param>
        /// <param name="executionId">Optional execution ID for context</param>
        Task UpdateStatusAsync(string status, string executionId = null);
        
        /// <summary>
        /// Gets an asset from the server
        /// </summary>
        Task<string> GetAssetAsync(string key, string machineKey);
        
        /// <summary>
        /// Gets all assets from the server
        /// </summary>
        Task<IDictionary<string, string>> GetAllAssetsAsync(string machineKey);
    }
    
    /// <summary>
    /// Interface for machine key management
    /// </summary>
    public interface IMachineKeyManager
    {
        /// <summary>
        /// Gets whether a machine key is available
        /// </summary>
        bool HasMachineKey();
        
        /// <summary>
        /// Gets the machine key
        /// </summary>
        string GetMachineKey();
        
        /// <summary>
        /// Sets the machine key
        /// </summary>
        void SetMachineKey(string machineKey);
        
        /// <summary>
        /// Clears the machine key
        /// </summary>
        void ClearMachineKey();
    }
    
    /// <summary>
    /// Interface for configuration service
    /// </summary>
    public interface IConfigurationService
    {
        /// <summary>
        /// Gets the current configuration
        /// </summary>
        BotAgentConfig GetConfiguration();
        
        /// <summary>
        /// Saves the configuration
        /// </summary>
        void SaveConfiguration(BotAgentConfig config);
    }
    
    /// <summary>
    /// Interface for API server
    /// </summary>
    public interface IApiServer
    {
        /// <summary>
        /// Starts the API server
        /// </summary>
        Task StartAsync();
        
        /// <summary>
        /// Stops the API server
        /// </summary>
        Task StopAsync();
    }
} 