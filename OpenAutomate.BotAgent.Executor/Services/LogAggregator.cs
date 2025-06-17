using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace OpenAutomate.BotAgent.Executor.Services
{
    /// <summary>
    /// Aggregates logs from multiple sources into a comprehensive log file
    /// </summary>
    public class LogAggregator
    {
        private readonly ILogger<LogAggregator> _logger;

        // Standardized log message templates
        private static class LogMessages
        {
            public const string AggregationStarted = "Starting log aggregation for execution {ExecutionId}";
            public const string AggregationCompleted = "Log aggregation completed for execution {ExecutionId}. Output file: {OutputPath}";
            public const string AggregationFailed = "Log aggregation failed for execution {ExecutionId}";
            public const string ExecutorLogFound = "Found executor log file: {LogPath}";
            public const string ExecutorLogNotFound = "Executor log file not found: {LogPath}";
            public const string PythonLogFound = "Found Python bot log file: {LogPath}";
            public const string PythonLogSearching = "Searching for Python bot logs in: {ScriptPath}";
        }

        public LogAggregator(ILogger<LogAggregator> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Aggregates executor and Python bot logs into a comprehensive log file
        /// </summary>
        /// <param name="executionId">Execution ID for log identification</param>
        /// <param name="executorLogPath">Path to the executor log file</param>
        /// <param name="scriptPath">Path to the Python bot script directory</param>
        /// <param name="packageName">Name of the executed package</param>
        /// <param name="version">Version of the executed package</param>
        /// <param name="startTime">Execution start time</param>
        /// <param name="endTime">Execution end time</param>
        /// <param name="finalStatus">Final execution status</param>
        /// <returns>Path to the comprehensive log file</returns>
        public async Task<string> AggregateLogsAsync(
            string executionId,
            string executorLogPath,
            string scriptPath,
            string packageName,
            string version,
            DateTime startTime,
            DateTime? endTime,
            string finalStatus)
        {
            try
            {
                _logger.LogInformation(LogMessages.AggregationStarted, executionId);

                var outputPath = Path.Combine(Path.GetTempPath(), $"comprehensive-execution-{executionId}.log");
                var logEntries = new List<LogEntry>();

                // Collect executor logs
                await CollectExecutorLogsAsync(executorLogPath, logEntries);

                // Collect Python bot logs
                await CollectPythonBotLogsAsync(scriptPath, logEntries);

                // Sort all log entries by timestamp
                logEntries.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

                // Write comprehensive log file
                await WriteComprehensiveLogFileAsync(
                    outputPath, 
                    logEntries, 
                    executionId, 
                    packageName, 
                    version, 
                    startTime, 
                    endTime, 
                    finalStatus);

                _logger.LogInformation(LogMessages.AggregationCompleted, executionId, outputPath);
                return outputPath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, LogMessages.AggregationFailed, executionId);
                throw;
            }
        }

        /// <summary>
        /// Collects logs from the executor log file
        /// </summary>
        private async Task CollectExecutorLogsAsync(string executorLogPath, List<LogEntry> logEntries)
        {
            if (File.Exists(executorLogPath))
            {
                _logger.LogDebug(LogMessages.ExecutorLogFound, executorLogPath);
                
                try
                {
                    // Use FileShare.ReadWrite to allow reading even if the file is still being written to
                    using var fileStream = new FileStream(executorLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(fileStream);
                    
                    string line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        var entry = ParseLogLine(line, "Executor");
                        if (entry != null)
                        {
                            logEntries.Add(entry);
                        }
                    }
                }
                catch (IOException ioEx) when (ioEx.Message.Contains("being used by another process"))
                {
                    _logger.LogWarning("Executor log file is locked, attempting to read with retry: {LogPath}", executorLogPath);
                    
                    // Wait a bit and try again with a more permissive file sharing mode
                    await Task.Delay(1000);
                    
                    try
                    {
                        // Try again with maximum file sharing permissions
                        using var fileStream = new FileStream(executorLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        using var reader = new StreamReader(fileStream);
                        
                        string line;
                        while ((line = await reader.ReadLineAsync()) != null)
                        {
                            var entry = ParseLogLine(line, "Executor");
                            if (entry != null)
                            {
                                logEntries.Add(entry);
                            }
                        }
                    }
                    catch (Exception retryEx)
                    {
                        _logger.LogWarning(retryEx, "Failed to read executor log file after retry: {LogPath}", executorLogPath);
                        
                        // Add a placeholder entry indicating the file couldn't be read
                        logEntries.Add(new LogEntry
                        {
                            Timestamp = DateTime.UtcNow,
                            Level = "WARNING",
                            Source = "Executor",
                            Message = $"Could not read executor log file (file locked): {executorLogPath}"
                        });
                    }
                }
            }
            else
            {
                _logger.LogWarning(LogMessages.ExecutorLogNotFound, executorLogPath);
                
                // Add a placeholder entry indicating executor log was not found
                logEntries.Add(new LogEntry
                {
                    Timestamp = DateTime.UtcNow,
                    Level = "WARNING",
                    Source = "Executor",
                    Message = $"Executor log file not found: {executorLogPath}"
                });
            }
        }

        /// <summary>
        /// Collects logs from Python bot execution
        /// </summary>
        private async Task CollectPythonBotLogsAsync(string scriptPath, List<LogEntry> logEntries)
        {
            _logger.LogDebug(LogMessages.PythonLogSearching, scriptPath);

            if (!Directory.Exists(scriptPath))
            {
                logEntries.Add(new LogEntry
                {
                    Timestamp = DateTime.UtcNow,
                    Level = "WARNING",
                    Source = "PythonBot",
                    Message = $"Script path does not exist: {scriptPath}"
                });
                return;
            }

            // Look for common Python log files
            var logPatterns = new[]
            {
                "*.log",
                "logs/*.log",
                "*.txt"
            };

            var foundLogs = false;

            foreach (var pattern in logPatterns)
            {
                var searchPath = Path.Combine(scriptPath, pattern);
                var directory = Path.GetDirectoryName(searchPath);
                var fileName = Path.GetFileName(searchPath);

                if (Directory.Exists(directory))
                {
                    var files = Directory.GetFiles(directory, fileName);
                    foreach (var file in files)
                    {
                        _logger.LogDebug(LogMessages.PythonLogFound, file);
                        foundLogs = true;

                        try
                        {
                            // Use FileShare.ReadWrite to allow reading even if the file is still being written to
                            using var fileStream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                            using var reader = new StreamReader(fileStream);
                            
                            string line;
                            while ((line = await reader.ReadLineAsync()) != null)
                            {
                                var entry = ParseLogLine(line, $"PythonBot({Path.GetFileName(file)})");
                                if (entry != null)
                                {
                                    logEntries.Add(entry);
                                }
                            }
                        }
                        catch (IOException ioEx) when (ioEx.Message.Contains("being used by another process"))
                        {
                            _logger.LogWarning("Python log file is locked, skipping: {LogPath}", file);
                            
                            // Add a placeholder entry indicating the file couldn't be read
                            logEntries.Add(new LogEntry
                            {
                                Timestamp = DateTime.UtcNow,
                                Level = "WARNING",
                                Source = $"PythonBot({Path.GetFileName(file)})",
                                Message = $"Could not read Python log file (file locked): {file}"
                            });
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to read Python log file: {LogPath}", file);
                            
                            // Add a placeholder entry indicating the file couldn't be read
                            logEntries.Add(new LogEntry
                            {
                                Timestamp = DateTime.UtcNow,
                                Level = "WARNING",
                                Source = $"PythonBot({Path.GetFileName(file)})",
                                Message = $"Error reading Python log file: {file} - {ex.Message}"
                            });
                        }
                    }
                }
            }

            if (!foundLogs)
            {
                logEntries.Add(new LogEntry
                {
                    Timestamp = DateTime.UtcNow,
                    Level = "INFO",
                    Source = "PythonBot",
                    Message = "No Python bot log files found in standard locations"
                });
            }
        }

        /// <summary>
        /// Parses a log line into a structured log entry
        /// </summary>
        private LogEntry ParseLogLine(string line, string source)
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;

            // Try to extract timestamp and level from common log formats
            var timestamp = DateTime.UtcNow;
            var level = "INFO";
            var message = line;

            // Common patterns:
            // [2024-01-15 10:30:45] INFO: Message
            // 2024-01-15 10:30:45 - INFO - Message
            // INFO: Message

            try
            {
                // Pattern 1: [2024-01-15 10:30:45] LEVEL: Message
                if (line.StartsWith("[") && line.Contains("]"))
                {
                    var closeBracket = line.IndexOf(']');
                    if (closeBracket > 0)
                    {
                        var timestampStr = line.Substring(1, closeBracket - 1);
                        if (DateTime.TryParse(timestampStr, out var parsedTimestamp))
                        {
                            timestamp = parsedTimestamp;
                        }

                        var remainder = line.Substring(closeBracket + 1).Trim();
                        var colonIndex = remainder.IndexOf(':');
                        if (colonIndex > 0)
                        {
                            level = remainder.Substring(0, colonIndex).Trim();
                            message = remainder.Substring(colonIndex + 1).Trim();
                        }
                        else
                        {
                            message = remainder;
                        }
                    }
                }
                // Pattern 2: YYYY-MM-DD HH:mm:ss - LEVEL - Message
                else if (line.Length > 19 && line[4] == '-' && line[7] == '-' && line[10] == ' ')
                {
                    var timestampStr = line.Substring(0, 19);
                    if (DateTime.TryParse(timestampStr, out var parsedTimestamp))
                    {
                        timestamp = parsedTimestamp;
                    }

                    var remainder = line.Substring(19).Trim();
                    if (remainder.StartsWith("- "))
                    {
                        remainder = remainder.Substring(2);
                        var dashIndex = remainder.IndexOf(" - ");
                        if (dashIndex > 0)
                        {
                            level = remainder.Substring(0, dashIndex).Trim();
                            message = remainder.Substring(dashIndex + 3).Trim();
                        }
                        else
                        {
                            message = remainder;
                        }
                    }
                }
                // Pattern 3: LEVEL: Message
                else if (line.Contains(":"))
                {
                    var colonIndex = line.IndexOf(':');
                    var potentialLevel = line.Substring(0, colonIndex).Trim();
                    if (IsValidLogLevel(potentialLevel))
                    {
                        level = potentialLevel;
                        message = line.Substring(colonIndex + 1).Trim();
                    }
                }
            }
            catch
            {
                // If parsing fails, use the original line as message
                message = line;
            }

            return new LogEntry
            {
                Timestamp = timestamp,
                Level = level.ToUpperInvariant(),
                Source = source,
                Message = message
            };
        }

        /// <summary>
        /// Checks if a string is a valid log level
        /// </summary>
        private bool IsValidLogLevel(string level)
        {
            var validLevels = new[] { "TRACE", "DEBUG", "INFO", "INFORMATION", "WARN", "WARNING", "ERROR", "FATAL", "CRITICAL" };
            return validLevels.Contains(level.ToUpperInvariant());
        }

        /// <summary>
        /// Writes the comprehensive log file with proper formatting
        /// </summary>
        private async Task WriteComprehensiveLogFileAsync(
            string outputPath,
            List<LogEntry> logEntries,
            string executionId,
            string packageName,
            string version,
            DateTime startTime,
            DateTime? endTime,
            string finalStatus)
        {
            var sb = new StringBuilder();

            // Header section
            sb.AppendLine("=".PadRight(80, '='));
            sb.AppendLine($"OPENAUTOMTE COMPREHENSIVE EXECUTION LOG");
            sb.AppendLine("=".PadRight(80, '='));
            sb.AppendLine($"Execution ID: {executionId}");
            sb.AppendLine($"Package: {packageName} v{version}");
            sb.AppendLine($"Start Time: {startTime:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"End Time: {endTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A"} UTC");
            sb.AppendLine($"Duration: {(endTime.HasValue ? (endTime.Value - startTime).ToString(@"hh\:mm\:ss") : "N/A")}");
            sb.AppendLine($"Final Status: {finalStatus}");
            sb.AppendLine($"Log Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine("=".PadRight(80, '='));
            sb.AppendLine();

            // Log entries section
            sb.AppendLine("EXECUTION LOG ENTRIES:");
            sb.AppendLine("-".PadRight(80, '-'));

            foreach (var entry in logEntries)
            {
                sb.AppendLine($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{entry.Level.PadRight(7)}] [{entry.Source.PadRight(15)}] {entry.Message}");
            }

            // Footer
            sb.AppendLine();
            sb.AppendLine("-".PadRight(80, '-'));
            sb.AppendLine($"END OF LOG - Total Entries: {logEntries.Count}");
            sb.AppendLine("=".PadRight(80, '='));

            await File.WriteAllTextAsync(outputPath, sb.ToString());
        }
    }

    /// <summary>
    /// Represents a structured log entry
    /// </summary>
    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Level { get; set; } = "INFO";
        public string Source { get; set; } = "Unknown";
        public string Message { get; set; } = string.Empty;
    }
} 