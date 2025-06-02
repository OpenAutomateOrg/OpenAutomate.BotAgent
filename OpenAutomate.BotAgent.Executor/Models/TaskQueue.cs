using System;
using System.Collections.Generic;

namespace OpenAutomate.BotAgent.Executor.Models
{
    public class TaskQueue
    {
        public List<BotTask> Tasks { get; set; } = new();
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
} 