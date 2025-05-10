namespace OpenAutomate.BotAgent.Service.Models
{
    /// <summary>
    /// Configuration model for the Bot Agent service
    /// </summary>
    public class BotAgentConfig
    {
        /// <summary>
        /// Server URL including tenant slug (e.g., https://openautomateapp.com/acme-corp)
        /// </summary>
        public string ServerUrl { get; set; }
        
        /// <summary>
        /// Machine key for authentication with the orchestrator
        /// </summary>
        public string MachineKey { get; set; }
        
        /// <summary>
        /// Whether the Bot Agent should auto-start connection on service start
        /// </summary>
        public bool AutoStart { get; set; } = true;
        
        /// <summary>
        /// Log level for the service
        /// </summary>
        public string LogLevel { get; set; } = "Information";
        
        /// <summary>
        /// Port for the local API server
        /// </summary>
        public int ApiPort { get; set; } = 8081;
    }
} 