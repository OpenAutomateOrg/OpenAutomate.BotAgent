using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using OpenAutomate.BotAgent.Service.Core;

namespace OpenAutomate.BotAgent.Service.Services
{
    /// <summary>
    /// Service for managing the machine key
    /// </summary>
    public class MachineKeyManager : IMachineKeyManager
    {
        private readonly ILogger<MachineKeyManager> _logger;
        private string _machineKey;
        private readonly string _keyPath;
        
        /// <summary>
        /// Initializes a new instance of the <see cref="MachineKeyManager"/> class
        /// </summary>
        public MachineKeyManager(ILogger<MachineKeyManager> logger)
        {
            _logger = logger;
            
            _keyPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "OpenAutomate", "BotAgent", "machine.key");
                
            LoadMachineKey();
        }
        
        /// <summary>
        /// Gets whether a machine key is available
        /// </summary>
        public bool HasMachineKey()
        {
            return !string.IsNullOrEmpty(_machineKey);
        }
        
        /// <summary>
        /// Gets the machine key
        /// </summary>
        public string GetMachineKey()
        {
            return _machineKey;
        }
        
        /// <summary>
        /// Sets the machine key
        /// </summary>
        public void SetMachineKey(string machineKey)
        {
            if (string.IsNullOrEmpty(machineKey))
            {
                throw new ArgumentNullException(nameof(machineKey));
            }
            
            try
            {
                var keyPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "OpenAutomate", "BotAgent");
                    
                Directory.CreateDirectory(keyPath);
                keyPath = Path.Combine(keyPath, "machine.key");
                
                // Encrypt the machine key using DPAPI
                var dataToEncrypt = Encoding.UTF8.GetBytes(machineKey);
                var encryptedData = ProtectedData.Protect(
                    dataToEncrypt,
                    null,
                    DataProtectionScope.LocalMachine);
                    
                File.WriteAllBytes(keyPath, encryptedData);
                
                _machineKey = machineKey;
                _logger.LogInformation("New machine key saved successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save machine key");
                throw;
            }
        }
        
        /// <summary>
        /// Clears the machine key
        /// </summary>
        public void ClearMachineKey()
        {
            try
            {
                if (File.Exists(_keyPath))
                {
                    File.Delete(_keyPath);
                }
                
                _machineKey = null;
                _logger.LogInformation("Machine key cleared successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clear machine key");
                throw;
            }
        }
        
        /// <summary>
        /// Loads the machine key from disk
        /// </summary>
        private void LoadMachineKey()
        {
            try
            {
                if (File.Exists(_keyPath))
                {
                    // Read and decrypt the machine key using DPAPI
                    var encryptedData = File.ReadAllBytes(_keyPath);
                    var decryptedData = ProtectedData.Unprotect(
                        encryptedData, 
                        null, 
                        DataProtectionScope.LocalMachine);
                    _machineKey = Encoding.UTF8.GetString(decryptedData);
                    
                    _logger.LogInformation("Machine key loaded successfully");
                }
                else
                {
                    _logger.LogInformation("No machine key file found");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load machine key");
                // Continue with null machine key
            }
        }
    }
} 