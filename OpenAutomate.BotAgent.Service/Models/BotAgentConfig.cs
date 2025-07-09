using System;

namespace OpenAutomate.BotAgent.Service.Models
{
    /// <summary>
    /// Configuration model for the Bot Agent service
    /// </summary>
    public class BotAgentConfig
    {
        /// <summary>
        /// Orchestrator URL including tenant slug (e.g., https://cloud.openautomate.me/acme-corp)
        /// This is the frontend URL that users configure. Used for initial discovery only.
        /// </summary>
        public string OrchestratorUrl { get; set; }

        /// <summary>
        /// Backend API URL discovered from the orchestrator (e.g., https://api.openautomate.me)
        /// This is cached after initial discovery and used for all API calls.
        /// </summary>
        public string BackendApiUrl { get; set; }

        /// <summary>
        /// DEPRECATED: Use OrchestratorUrl instead. Maintained for backward compatibility.
        /// This property is not serialized to avoid confusion in config files.
        /// </summary>
        [Obsolete("Use OrchestratorUrl instead. This property is maintained for backward compatibility.")]
        [System.Text.Json.Serialization.JsonIgnore]
        public string ServerUrl
        {
            get => OrchestratorUrl;
            set => OrchestratorUrl = value;
        }

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
        public int ApiPort { get; set; } = 8080;
    }
} 