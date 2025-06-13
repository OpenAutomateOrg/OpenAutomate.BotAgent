using System;

namespace OpenAutomate.BotAgent.Executor.Models
{
    public class BotTask
    {
        public string TaskId { get; set; } = Guid.NewGuid().ToString();
        public string ScriptPath { get; set; } = string.Empty;
        public string PackageId { get; set; } = string.Empty;
        public string PackageName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Status { get; set; } = "Pending"; // Pending, Running, Completed, Failed
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public int? ProcessId { get; set; }
        public string? ErrorMessage { get; set; }
        public string? ExecutionId { get; set; } // Backend execution ID
    }
} 