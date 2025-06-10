using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenAutomate.BotAgent.Service.Core;

namespace OpenAutomate.BotAgent.Service.Services
{
    /// <summary>
    /// Service for managing assets with machine key-based authentication
    /// </summary>
    public class AssetManager : IAssetManager
    {
        private readonly ILogger<AssetManager> _logger;
        private readonly IServerCommunication _serverComm;
        private readonly IMachineKeyManager _machineKeyManager;
        
        /// <summary>
        /// Initializes a new instance of the <see cref="AssetManager"/> class
        /// </summary>
        public AssetManager(
            ILogger<AssetManager> logger,
            IServerCommunication serverComm,
            IMachineKeyManager machineKeyManager)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serverComm = serverComm ?? throw new ArgumentNullException(nameof(serverComm));
            _machineKeyManager = machineKeyManager ?? throw new ArgumentNullException(nameof(machineKeyManager));
        }
        
        /// <summary>
        /// Gets an asset by key directly from the server without caching
        /// </summary>
        public async Task<string> GetAssetAsync(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentNullException(nameof(key));
            }
            
            _logger.LogDebug("Getting asset with key '{Key}'", key);
            
            // Always fetch fresh from server
            if (!_serverComm.IsConnected)
            {
                _logger.LogWarning("Cannot get asset '{Key}': Not connected to server", key);
                throw new InvalidOperationException("Not connected to server");
            }
            
            var machineKey = _machineKeyManager.GetMachineKey();
            if (string.IsNullOrEmpty(machineKey))
            {
                _logger.LogWarning("Cannot get asset '{Key}': Machine key not available", key);
                throw new InvalidOperationException("Machine key not available");
            }
            
            _logger.LogInformation("Requesting asset '{Key}' from server", key);
            var asset = await _serverComm.GetAssetAsync(key, machineKey);
            
            if (asset != null)
            {
                _logger.LogDebug("Asset '{Key}' retrieved successfully", key);
                return asset;
            }
            
            _logger.LogWarning("Asset '{Key}' not found or not authorized", key);
            throw new KeyNotFoundException($"Asset with key '{key}' not found");
        }
        
        /// <summary>
        /// Gets all available asset keys directly from the server
        /// </summary>
        public async Task<IEnumerable<string>> GetAllAssetKeysAsync()
        {
            _logger.LogDebug("Getting all available asset keys");
            
            if (!_serverComm.IsConnected)
            {
                _logger.LogWarning("Cannot get assets: Not connected to server");
                throw new InvalidOperationException("Not connected to server");
            }
            
            var machineKey = _machineKeyManager.GetMachineKey();
            if (string.IsNullOrEmpty(machineKey))
            {
                _logger.LogWarning("Cannot get assets: Machine key not available");
                throw new InvalidOperationException("Machine key not available");
            }
            
            try
            {
                _logger.LogInformation("Requesting accessible assets from server");
                var assets = await _serverComm.GetAllAssetsAsync(machineKey);
                
                // Extract keys from the dictionary
                return assets.Keys;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all asset keys");
                throw;
            }
        }
        
        /// <summary>
        /// This method is intentionally empty as we no longer cache or sync assets
        /// </summary>
        public Task SyncAssetsAsync()
        {
            _logger.LogDebug("SyncAssetsAsync called but asset caching is disabled");
            return Task.CompletedTask;
        }
    }
} 