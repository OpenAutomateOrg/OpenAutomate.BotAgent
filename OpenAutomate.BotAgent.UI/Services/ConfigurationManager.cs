using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using OpenAutomate.BotAgent.UI.Models;
using OpenAutomate.BotAgent.UI.Services;

namespace OpenAutomate.BotAgent.UI.Services
{
    /// <summary>
    /// Manages loading and saving of configuration settings to config.json
    /// </summary>
    public class ConfigurationManager
    {
        private const string CONFIG_FILENAME = "config.json";
        private readonly string _configPath;
        
        /// <summary>
        /// Initializes a new instance of the ConfigurationManager
        /// </summary>
        public ConfigurationManager()
        {
            // Get the configuration file path
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "OpenAutomate", "BotAgent");
                
            // Ensure directory exists
            Directory.CreateDirectory(appDataPath);
            
            _configPath = Path.Combine(appDataPath, CONFIG_FILENAME);
        }
        
        /// <summary>
        /// Loads configuration from config.json
        /// </summary>
        public async Task<ConfigurationModel> LoadConfigurationAsync()
        {
            try
            {
                if (!File.Exists(_configPath))
                {
                    // If config doesn't exist, return default configuration
                    LoggingService.Information("Configuration file not found at {ConfigPath}, creating default", _configPath);
                    return CreateDefaultConfiguration();
                }
                
                // Read and deserialize the configuration file
                LoggingService.Debug("Loading configuration from {ConfigPath}", _configPath);
                using var stream = File.OpenRead(_configPath);
                var config = await JsonSerializer.DeserializeAsync<ConfigurationModel>(stream);
                
                // If deserialization failed, return default configuration
                if (config == null)
                {
                    LoggingService.Warning("Failed to deserialize configuration, using default");
                    return CreateDefaultConfiguration();
                }
                
                LoggingService.Information("Configuration loaded successfully");
                return config;
            }
            catch (Exception ex)
            {
                LoggingService.Error(ex, "Error loading configuration from {ConfigPath}", _configPath);
                return CreateDefaultConfiguration();
            }
        }
        
        /// <summary>
        /// Saves configuration to config.json
        /// </summary>
        public async Task SaveConfigurationAsync(ConfigurationModel config)
        {
            try
            {
                // Ensure directory exists
                var directory = Path.GetDirectoryName(_configPath);
                if (!Directory.Exists(directory) && directory != null)
                {
                    Directory.CreateDirectory(directory);
                }
                
                // Serialize and write the configuration to file
                LoggingService.Debug("Saving configuration to {ConfigPath}", _configPath);
                using var stream = File.Create(_configPath);
                var options = new JsonSerializerOptions { WriteIndented = true };
                await JsonSerializer.SerializeAsync(stream, config, options);
                
                LoggingService.Information("Configuration saved successfully");
            }
            catch (Exception ex)
            {
                LoggingService.Error(ex, "Error saving configuration to {ConfigPath}", _configPath);
                throw;
            }
        }
        
        /// <summary>
        /// Creates a default configuration with reasonable values
        /// </summary>
        private ConfigurationModel CreateDefaultConfiguration()
        {
            return new ConfigurationModel
            {
                MachineName = Environment.MachineName,
                LoggingLevel = "INFO"
            };
        }
    }
} 