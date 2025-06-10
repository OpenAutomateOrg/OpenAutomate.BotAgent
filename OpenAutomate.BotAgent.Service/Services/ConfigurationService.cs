using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAutomate.BotAgent.Service.Core;
using OpenAutomate.BotAgent.Service.Models;

namespace OpenAutomate.BotAgent.Service.Services
{
    /// <summary>
    /// Service for managing Bot Agent configuration
    /// </summary>
    public class ConfigurationService : IConfigurationService
    {
        private readonly ILogger<ConfigurationService> _logger;
        private readonly IOptions<BotAgentConfig> _options;
        private BotAgentConfig _config;
        private readonly string _configFilePath;
        
        /// <summary>
        /// Initializes a new instance of the <see cref="ConfigurationService"/> class
        /// </summary>
        public ConfigurationService(
            ILogger<ConfigurationService> logger,
            IOptions<BotAgentConfig> options)
        {
            _logger = logger;
            _options = options;
            
            // Initialize with app settings
            _config = new BotAgentConfig
            {
                ServerUrl = _options.Value.ServerUrl,
                MachineKey = _options.Value.MachineKey,
                AutoStart = _options.Value.AutoStart,
                LogLevel = _options.Value.LogLevel,
                ApiPort = _options.Value.ApiPort
            };
            
            // Set up config file path
            _configFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "OpenAutomate", "BotAgent", "config.json");
                
            // Load stored configuration if it exists
            LoadConfiguration();
        }
        
        /// <summary>
        /// Gets the current configuration
        /// </summary>
        public BotAgentConfig GetConfiguration()
        {
            return _config;
        }
        
        /// <summary>
        /// Saves the configuration
        /// </summary>
        public void SaveConfiguration(BotAgentConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }
            
            // Update local config
            _config = config;
            
            try
            {
                // Ensure directory exists
                var directory = Path.GetDirectoryName(_configFilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                // Serialize and save to file
                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                
                File.WriteAllText(_configFilePath, json);
                _logger.LogInformation("Configuration saved to {ConfigFilePath}", _configFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving configuration to {ConfigFilePath}", _configFilePath);
                throw;
            }
        }
        
        /// <summary>
        /// Loads configuration from file
        /// </summary>
        private void LoadConfiguration()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    var json = File.ReadAllText(_configFilePath);
                    var fileConfig = JsonSerializer.Deserialize<BotAgentConfig>(json);
                    
                    if (fileConfig != null)
                    {
                        // Update config with values from file
                        _config = fileConfig;
                        _logger.LogInformation("Configuration loaded from {ConfigFilePath}", _configFilePath);
                    }
                }
                else
                {
                    _logger.LogInformation("No configuration file found at {ConfigFilePath}, using defaults", _configFilePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading configuration from {ConfigFilePath}", _configFilePath);
                // Continue with default/appsettings configuration
            }
        }
    }
} 