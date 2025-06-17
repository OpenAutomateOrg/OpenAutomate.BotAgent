using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace OpenAutomate.BotAgent.Executor.Services
{
    /// <summary>
    /// Handles uploading log files to the backend API
    /// </summary>
    public class LogUploader : IDisposable
    {
        private readonly ILogger<LogUploader> _logger;

        // Standardized log message templates
        private static class LogMessages
        {
            public const string UploadStarted = "Starting log upload for execution {ExecutionId} to {ApiUrl}";
            public const string UploadCompleted = "Log upload completed successfully for execution {ExecutionId}";
            public const string UploadFailed = "Log upload failed for execution {ExecutionId}";
            public const string AuthenticationFailed = "Authentication failed for log upload. Execution {ExecutionId}";
            public const string FileNotFound = "Log file not found for upload: {LogFilePath}";
            public const string HttpClientCreated = "HTTP client created for log upload";
        }

        public LogUploader(ILogger<LogUploader> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.LogDebug(LogMessages.HttpClientCreated);
        }

        /// <summary>
        /// Uploads a log file to the backend API
        /// </summary>
        /// <param name="apiBaseUrl">Base URL of the backend API (already includes tenant slug)</param>
        /// <param name="tenantSlug">Tenant slug for the API endpoint (for reference, but not used in URL construction)</param>
        /// <param name="executionId">Execution ID</param>
        /// <param name="logFilePath">Path to the log file to upload</param>
        /// <param name="machineKey">Machine key for authentication</param>
        /// <returns>True if upload succeeded, false otherwise</returns>
        public async Task<bool> UploadLogAsync(
            string apiBaseUrl,
            string tenantSlug,
            string executionId,
            string logFilePath,
            string machineKey)
        {
            try
            {
                // apiBaseUrl already includes the tenant slug, so we don't add it again
                var apiUrl = $"{apiBaseUrl.TrimEnd('/')}/api/executions/{executionId}/logs";
                _logger.LogInformation(LogMessages.UploadStarted, executionId, apiUrl);

                // Validate log file exists
                if (!File.Exists(logFilePath))
                {
                    _logger.LogError(LogMessages.FileNotFound, logFilePath);
                    return false;
                }

                // Create a fresh HttpClient for this request to avoid timeout setting issues
                using var httpClient = new HttpClient();
                
                // Set timeout for large file uploads (5 minutes)
                httpClient.Timeout = TimeSpan.FromMinutes(5);
                
                // Add authentication header
                httpClient.DefaultRequestHeaders.Add("X-Machine-Key", machineKey);

                // Prepare the multipart form data
                using var formData = new MultipartFormDataContent();
                using var fileStream = File.OpenRead(logFilePath);
                using var streamContent = new StreamContent(fileStream);
                
                streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
                formData.Add(streamContent, "logFile", Path.GetFileName(logFilePath));

                // Send the request
                var response = await httpClient.PostAsync(apiUrl, formData);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation(LogMessages.UploadCompleted, executionId);
                    return true;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        _logger.LogError(LogMessages.AuthenticationFailed, executionId);
                    }
                    else
                    {
                        _logger.LogError("Log upload failed for execution {ExecutionId}. Status: {StatusCode}, Response: {Response}", 
                            executionId, response.StatusCode, errorContent);
                    }
                    
                    return false;
                }
            }
            catch (HttpRequestException httpEx)
            {
                _logger.LogError(httpEx, "HTTP error during log upload for execution {ExecutionId}", executionId);
                return false;
            }
            catch (TaskCanceledException tcEx) when (tcEx.InnerException is TimeoutException)
            {
                _logger.LogError(tcEx, "Log upload timed out for execution {ExecutionId}", executionId);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, LogMessages.UploadFailed, executionId);
                return false;
            }
        }

        /// <summary>
        /// Uploads a log file with retry logic
        /// </summary>
        /// <param name="apiBaseUrl">Base URL of the backend API (already includes tenant slug)</param>
        /// <param name="tenantSlug">Tenant slug for the API endpoint (for reference, but not used in URL construction)</param>
        /// <param name="executionId">Execution ID</param>
        /// <param name="logFilePath">Path to the log file to upload</param>
        /// <param name="machineKey">Machine key for authentication</param>
        /// <param name="maxRetries">Maximum number of retry attempts</param>
        /// <returns>True if upload succeeded, false otherwise</returns>
        public async Task<bool> UploadLogWithRetryAsync(
            string apiBaseUrl,
            string tenantSlug,
            string executionId,
            string logFilePath,
            string machineKey,
            int maxRetries = 3)
        {
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                _logger.LogDebug("Log upload attempt {Attempt} of {MaxRetries} for execution {ExecutionId}", 
                    attempt, maxRetries, executionId);

                var success = await UploadLogAsync(apiBaseUrl, tenantSlug, executionId, logFilePath, machineKey);
                
                if (success)
                {
                    return true;
                }

                if (attempt < maxRetries)
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt)); // Exponential backoff
                    _logger.LogInformation("Log upload attempt {Attempt} failed, retrying in {Delay} seconds", 
                        attempt, delay.TotalSeconds);
                    await Task.Delay(delay);
                }
            }

            _logger.LogError("Log upload failed after {MaxRetries} attempts for execution {ExecutionId}", 
                maxRetries, executionId);
            return false;
        }

        /// <summary>
        /// Disposes the HTTP client
        /// </summary>
        public void Dispose()
        {
            // No longer need to dispose _httpClient since we create fresh instances
        }
    }
} 