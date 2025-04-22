using System;

namespace OpenAutomate.BotAgent.UI.Models
{
    /// <summary>
    /// Model representing the Bot Agent configuration
    /// </summary>
    public class ConfigurationModel
    {
        /// <summary>
        /// The machine key used for authentication with the OpenAutomate server
        /// </summary>
        public string MachineKey { get; set; }
        
        /// <summary>
        /// The URL of the OpenAutomate server (including tenant slug)
        /// </summary>
        public string OrchestratorUrl { get; set; }
        
        /// <summary>
        /// The name of the machine (automatically populated from system)
        /// </summary>
        public string MachineName { get; set; }
        
        /// <summary>
        /// Connection status
        /// </summary>
        public bool IsConnected { get; set; }
        
        /// <summary>
        /// The date and time of the last successful connection
        /// </summary>
        public DateTime? LastConnected { get; set; }
    }
} 