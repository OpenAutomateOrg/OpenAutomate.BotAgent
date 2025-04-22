using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenAutomate.BotAgent.UI.Models
{
    /// <summary>
    /// Manages secure storage and retrieval of agent settings
    /// </summary>
    public class AgentSettings
    {
        private const string SettingsFileName = "agent_settings.json";
        private const string EntropyValue = "OpenAutomate_Settings_Security";

        private readonly string _settingsFilePath;
        
        public AgentSettings()
        {
            // Store settings in the AppData folder
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenAutomate",
                "BotAgent");
            
            // Create directory if it doesn't exist
            if (!Directory.Exists(appDataPath))
            {
                Directory.CreateDirectory(appDataPath);
            }
            
            _settingsFilePath = Path.Combine(appDataPath, SettingsFileName);
        }
        
        /// <summary>
        /// Saves the configuration settings to a secure file
        /// </summary>
        /// <param name="config">The configuration to save</param>
        public void SaveSettings(ConfigurationModel config)
        {
            try
            {
                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                
                // Encrypt the settings before saving
                byte[] protectedData = ProtectData(Encoding.UTF8.GetBytes(json));
                
                File.WriteAllBytes(_settingsFilePath, protectedData);
            }
            catch (Exception ex)
            {
                // Log the error but don't throw it to the UI
                Console.WriteLine($"Error saving settings: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Loads the configuration settings from the secure file
        /// </summary>
        /// <returns>The loaded configuration or null if not found or error</returns>
        public ConfigurationModel LoadSettings()
        {
            try
            {
                if (!File.Exists(_settingsFilePath))
                {
                    return null;
                }
                
                byte[] protectedData = File.ReadAllBytes(_settingsFilePath);
                byte[] unprotectedData = UnprotectData(protectedData);
                
                string json = Encoding.UTF8.GetString(unprotectedData);
                
                return JsonSerializer.Deserialize<ConfigurationModel>(json);
            }
            catch (Exception ex)
            {
                // Log the error but don't throw it to the UI
                Console.WriteLine($"Error loading settings: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// Encrypts the data using DPAPI
        /// </summary>
        private byte[] ProtectData(byte[] data)
        {
            byte[] entropy = Encoding.UTF8.GetBytes(EntropyValue);
            return ProtectedData.Protect(data, entropy, DataProtectionScope.CurrentUser);
        }
        
        /// <summary>
        /// Decrypts the data using DPAPI
        /// </summary>
        private byte[] UnprotectData(byte[] protectedData)
        {
            byte[] entropy = Encoding.UTF8.GetBytes(EntropyValue);
            return ProtectedData.Unprotect(protectedData, entropy, DataProtectionScope.CurrentUser);
        }
    }
} 