using System;
using System.Text.Json;

namespace OpenAutomate.BotAgent.Service.Models
{
    /// <summary>
    /// Asset information DTO
    /// </summary>
    public class AssetInfo
    {
        /// <summary>
        /// The asset ID
        /// </summary>
        public Guid Id { get; set; }
        
        /// <summary>
        /// The asset key
        /// </summary>
        public string Key { get; set; }
        
        /// <summary>
        /// The asset description
        /// </summary>
        public string Description { get; set; }
        
        /// <summary>
        /// The asset type - can be a number (enum) or string
        /// </summary>
        public JsonElement Type { get; set; }
        
        /// <summary>
        /// Whether the asset is encrypted
        /// </summary>
        public bool IsEncrypted { get; set; }
        
        /// <summary>
        /// When the asset was created
        /// </summary>
        public DateTime CreatedAt { get; set; }
        
        /// <summary>
        /// When the asset was last modified
        /// </summary>
        public DateTime? LastModifiedAt { get; set; }
    }
} 