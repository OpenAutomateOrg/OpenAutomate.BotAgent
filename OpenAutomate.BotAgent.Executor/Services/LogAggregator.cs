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

        // Log source constants
        private static class LogSources
        {
            public const string Executor = "Executor";
            public const string PythonBot = "PythonBot";
        }

        // Log level constants
        private static class LogLevels
        {
            public const string Warning = "WARNING";
            public const string Info = "INFO";
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
            if (!File.Exists(executorLogPath))
            {
                await AddExecutorLogNotFoundEntry(logEntries, executorLogPath);
                return;
            }

            _logger.LogDebug(LogMessages.ExecutorLogFound, executorLogPath);

            try
            {
                await ReadExecutorLogFile(executorLogPath, logEntries);
            }
            catch (IOException ioEx) when (ioEx.Message.Contains("being used by another process"))
            {
                await HandleLockedExecutorLogFile(executorLogPath, logEntries, ioEx);
            }
        }

        /// <summary>
        /// Reads the executor log file and adds entries to the collection
        /// </summary>
        private async Task ReadExecutorLogFile(string executorLogPath, List<LogEntry> logEntries)
        {
            using var fileStream = new FileStream(executorLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fileStream);

            string line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                var entry = ParseLogLine(line, LogSources.Executor);
                if (entry != null)
                {
                    logEntries.Add(entry);
                }
            }
        }

        /// <summary>
        /// Handles the case when the executor log file is locked by another process
        /// </summary>
        private async Task HandleLockedExecutorLogFile(string executorLogPath, List<LogEntry> logEntries, IOException ioEx)
        {
            _logger.LogWarning(ioEx, "Executor log file is locked, attempting to read with retry: {LogPath}", executorLogPath);

            // Wait a bit and try again with a more permissive file sharing mode
            await Task.Delay(1000);

            try
            {
                await ReadExecutorLogFileWithMaxSharing(executorLogPath, logEntries);
            }
            catch (Exception retryEx)
            {
                await AddExecutorLogReadErrorEntry(logEntries, executorLogPath, retryEx);
            }
        }

        /// <summary>
        /// Attempts to read the executor log file with maximum file sharing permissions
        /// </summary>
        private async Task ReadExecutorLogFileWithMaxSharing(string executorLogPath, List<LogEntry> logEntries)
        {
            using var fileStream = new FileStream(executorLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fileStream);

            string line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                var entry = ParseLogLine(line, LogSources.Executor);
                if (entry != null)
                {
                    logEntries.Add(entry);
                }
            }
        }

        /// <summary>
        /// Adds an entry indicating the executor log file was not found
        /// </summary>
        private async Task AddExecutorLogNotFoundEntry(List<LogEntry> logEntries, string executorLogPath)
        {
            _logger.LogWarning(LogMessages.ExecutorLogNotFound, executorLogPath);

            logEntries.Add(new LogEntry
            {
                Timestamp = DateTime.UtcNow,
                Level = LogLevels.Warning,
                Source = LogSources.Executor,
                Message = $"Executor log file not found: {executorLogPath}"
            });

            await Task.CompletedTask;
        }

        /// <summary>
        /// Adds an entry indicating an error occurred while reading the executor log file
        /// </summary>
        private async Task AddExecutorLogReadErrorEntry(List<LogEntry> logEntries, string executorLogPath, Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read executor log file after retry: {LogPath}", executorLogPath);

            logEntries.Add(new LogEntry
            {
                Timestamp = DateTime.UtcNow,
                Level = LogLevels.Warning,
                Source = LogSources.Executor,
                Message = $"Could not read executor log file (file locked): {executorLogPath}"
            });

            await Task.CompletedTask;
        }

        /// <summary>
        /// Collects logs from Python bot execution
        /// </summary>
        private async Task CollectPythonBotLogsAsync(string scriptPath, List<LogEntry> logEntries)
        {
            _logger.LogDebug(LogMessages.PythonLogSearching, scriptPath);

            if (!Directory.Exists(scriptPath))
            {
                AddPythonBotScriptPathNotFoundEntry(logEntries, scriptPath);
                return;
            }

            var foundLogs = await ProcessPythonLogPatterns(scriptPath, logEntries);

            if (!foundLogs)
            {
                AddPythonBotNoLogsFoundEntry(logEntries);
            }
        }

        /// <summary>
        /// Processes all Python log patterns and returns whether any logs were found
        /// </summary>
        private async Task<bool> ProcessPythonLogPatterns(string scriptPath, List<LogEntry> logEntries)
        {
            var logPatterns = new[] { "*.log", "logs/*.log", "*.txt" };
            var foundLogs = false;

            foreach (var pattern in logPatterns)
            {
                var patternFoundLogs = await ProcessSinglePythonLogPattern(scriptPath, pattern, logEntries);
                foundLogs = foundLogs || patternFoundLogs;
            }

            return foundLogs;
        }

        /// <summary>
        /// Processes a single Python log pattern and returns whether any logs were found
        /// </summary>
        private async Task<bool> ProcessSinglePythonLogPattern(string scriptPath, string pattern, List<LogEntry> logEntries)
        {
            var searchPath = Path.Combine(scriptPath, pattern);
            var directory = Path.GetDirectoryName(searchPath);
            var fileName = Path.GetFileName(searchPath);

            if (!Directory.Exists(directory))
                return false;

            var files = Directory.GetFiles(directory, fileName);
            var foundLogs = false;

            foreach (var file in files)
            {
                _logger.LogDebug(LogMessages.PythonLogFound, file);
                foundLogs = true;
                await ProcessPythonLogFile(file, logEntries);
            }

            return foundLogs;
        }

        /// <summary>
        /// Processes a single Python log file
        /// </summary>
        private async Task ProcessPythonLogFile(string file, List<LogEntry> logEntries)
        {
            try
            {
                await ReadPythonLogFile(file, logEntries);
            }
            catch (IOException ioEx) when (ioEx.Message.Contains("being used by another process"))
            {
                AddPythonBotFileLockedEntry(logEntries, file, ioEx);
            }
            catch (Exception ex)
            {
                AddPythonBotFileErrorEntry(logEntries, file, ex);
            }
        }

        /// <summary>
        /// Reads a Python log file and adds entries to the collection
        /// </summary>
        private async Task ReadPythonLogFile(string file, List<LogEntry> logEntries)
        {
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

        /// <summary>
        /// Adds an entry indicating the Python bot script path does not exist
        /// </summary>
        private void AddPythonBotScriptPathNotFoundEntry(List<LogEntry> logEntries, string scriptPath)
        {
            logEntries.Add(new LogEntry
            {
                Timestamp = DateTime.UtcNow,
                Level = LogLevels.Warning,
                Source = LogSources.PythonBot,
                Message = $"Script path does not exist: {scriptPath}"
            });
        }

        /// <summary>
        /// Adds an entry indicating no Python bot logs were found
        /// </summary>
        private void AddPythonBotNoLogsFoundEntry(List<LogEntry> logEntries)
        {
            logEntries.Add(new LogEntry
            {
                Timestamp = DateTime.UtcNow,
                Level = LogLevels.Info,
                Source = LogSources.PythonBot,
                Message = "No Python bot log files found in standard locations"
            });
        }

        /// <summary>
        /// Adds an entry indicating a Python bot log file was locked
        /// </summary>
        private void AddPythonBotFileLockedEntry(List<LogEntry> logEntries, string file, IOException ioEx)
        {
            _logger.LogWarning(ioEx, "Python log file is locked, skipping: {LogPath}", file);

            logEntries.Add(new LogEntry
            {
                Timestamp = DateTime.UtcNow,
                Level = LogLevels.Warning,
                Source = $"PythonBot({Path.GetFileName(file)})",
                Message = $"Could not read Python log file (file locked): {file}"
            });
        }

        /// <summary>
        /// Adds an entry indicating an error occurred while reading a Python bot log file
        /// </summary>
        private void AddPythonBotFileErrorEntry(List<LogEntry> logEntries, string file, Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read Python log file: {LogPath}", file);

            logEntries.Add(new LogEntry
            {
                Timestamp = DateTime.UtcNow,
                Level = LogLevels.Warning,
                Source = $"PythonBot({Path.GetFileName(file)})",
                Message = $"Error reading Python log file: {file} - {ex.Message}"
            });
        }

        /// <summary>
        /// Parses a log line into a structured log entry
        /// </summary>
        private LogEntry ParseLogLine(string line, string source)
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;

            var timestamp = DateTime.UtcNow;
            var level = "INFO";
            var message = line;

            try
            {
                var parseResult = TryParseLogLinePatterns(line);
                if (parseResult != null)
                {
                    timestamp = parseResult.Timestamp ?? timestamp;
                    level = parseResult.Level ?? level;
                    message = parseResult.Message ?? message;
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
        /// Attempts to parse log line using various patterns
        /// </summary>
        private LogParseResult TryParseLogLinePatterns(string line)
        {
            // Pattern 1: [2024-01-15 10:30:45] LEVEL: Message
            var bracketResult = TryParseBracketPattern(line);
            if (bracketResult != null)
                return bracketResult;

            // Pattern 2: YYYY-MM-DD HH:mm:ss - LEVEL - Message
            var dashResult = TryParseDashPattern(line);
            if (dashResult != null)
                return dashResult;

            // Pattern 3: LEVEL: Message
            var colonResult = TryParseColonPattern(line);
            if (colonResult != null)
                return colonResult;

            return null;
        }

        /// <summary>
        /// Tries to parse bracket pattern: [2024-01-15 10:30:45] LEVEL: Message
        /// </summary>
        private LogParseResult TryParseBracketPattern(string line)
        {
            if (!line.StartsWith("[") || !line.Contains("]"))
                return null;

            var closeBracket = line.IndexOf(']');
            if (closeBracket <= 0)
                return null;

            var timestampStr = line.Substring(1, closeBracket - 1);
            var timestamp = DateTime.TryParse(timestampStr, out var parsedTimestamp) ? parsedTimestamp : (DateTime?)null;

            var remainder = line.Substring(closeBracket + 1).Trim();
            var colonIndex = remainder.IndexOf(':');

            if (colonIndex > 0)
            {
                var level = remainder.Substring(0, colonIndex).Trim();
                var message = remainder.Substring(colonIndex + 1).Trim();
                return new LogParseResult { Timestamp = timestamp, Level = level, Message = message };
            }

            return new LogParseResult { Timestamp = timestamp, Message = remainder };
        }

        /// <summary>
        /// Tries to parse dash pattern: YYYY-MM-DD HH:mm:ss - LEVEL - Message
        /// </summary>
        private LogParseResult TryParseDashPattern(string line)
        {
            if (line.Length <= 19 || line[4] != '-' || line[7] != '-' || line[10] != ' ')
                return null;

            var timestampStr = line.Substring(0, 19);
            var timestamp = DateTime.TryParse(timestampStr, out var parsedTimestamp) ? parsedTimestamp : (DateTime?)null;

            var remainder = line.Substring(19).Trim();
            if (!remainder.StartsWith("- "))
                return new LogParseResult { Timestamp = timestamp, Message = remainder };

            remainder = remainder.Substring(2);
            var dashIndex = remainder.IndexOf(" - ");

            if (dashIndex > 0)
            {
                var level = remainder.Substring(0, dashIndex).Trim();
                var message = remainder.Substring(dashIndex + 3).Trim();
                return new LogParseResult { Timestamp = timestamp, Level = level, Message = message };
            }

            return new LogParseResult { Timestamp = timestamp, Message = remainder };
        }

        /// <summary>
        /// Tries to parse colon pattern: LEVEL: Message
        /// </summary>
        private LogParseResult TryParseColonPattern(string line)
        {
            if (!line.Contains(":"))
                return null;

            var colonIndex = line.IndexOf(':');
            var potentialLevel = line.Substring(0, colonIndex).Trim();

            if (IsValidLogLevel(potentialLevel))
            {
                var level = potentialLevel;
                var message = line.Substring(colonIndex + 1).Trim();
                return new LogParseResult { Level = level, Message = message };
            }

            return null;
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

    /// <summary>
    /// Represents the result of parsing a log line
    /// </summary>
    internal class LogParseResult
    {
        public DateTime? Timestamp { get; set; }
        public string Level { get; set; }
        public string Message { get; set; }
    }
} 