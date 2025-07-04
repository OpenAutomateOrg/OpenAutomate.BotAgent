using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenAutomate.BotAgent.Service.Core;

namespace OpenAutomate.BotAgent.Service.Services
{
    /// <summary>
    /// Interface for package download service
    /// </summary>
    public interface IPackageDownloadService
    {
        Task<string> DownloadPackageAsync(string packageId, string version);
        Task<string> DownloadPackageAsync(string packageId, string version, string packageName, string tenantSlug);
    }

    /// <summary>
    /// Service for downloading and extracting automation packages from the backend
    /// </summary>
    public class PackageDownloadService : IPackageDownloadService
    {
        private readonly ILogger<PackageDownloadService> _logger;
        private readonly IConfigurationService _configService;
        private readonly HttpClient _httpClient;
        private readonly string _botScriptsPath = @"C:\ProgramData\OpenAutomate\BotScripts";

        public PackageDownloadService(
            ILogger<PackageDownloadService> logger,
            IConfigurationService configService,
            HttpClient httpClient)
        {
            _logger = logger;
            _configService = configService;
            _httpClient = httpClient;
        }

        public async Task<string> DownloadPackageAsync(string packageId, string version)
        {
            // Default implementation for backward compatibility
            return await DownloadPackageAsync(packageId, version, "unknown", "unknown");
        }

        public async Task<string> DownloadPackageAsync(string packageId, string version, string packageName, string tenantSlug)
        {
            try
            {
                _logger.LogInformation("Starting package download for {PackageId} v{Version} (Tenant: {TenantSlug}, Package: {PackageName})", 
                    packageId, version, tenantSlug, packageName);
                
                var config = _configService.GetConfiguration();
                _logger.LogInformation("Server URL: {ServerUrl}", config.ServerUrl);
                
                var downloadUrl = await GetDownloadUrlAsync(packageId, version, tenantSlug, config.MachineKey);
                
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    _logger.LogError("Failed to get download URL for package {PackageId} v{Version}", packageId, version);
                    return null;
                }

                // Create meaningful folder name: tenantslug_packagename_version_guid
                var sanitizedTenant = SanitizeForPath(tenantSlug);
                var sanitizedPackage = SanitizeForPath(packageName);
                var sanitizedVersion = SanitizeForPath(version);
                var guid = Guid.NewGuid().ToString("N");
                var folderName = $"{sanitizedTenant}_{sanitizedPackage}_{sanitizedVersion}_{guid}";
                var extractPath = Path.Combine(_botScriptsPath, folderName);
                
                _logger.LogInformation("Creating extract directory: {ExtractPath}", extractPath);
                Directory.CreateDirectory(extractPath);

                // Download package
                _logger.LogInformation("Downloading package from {DownloadUrl}", downloadUrl);
                var response = await _httpClient.GetAsync(downloadUrl);
                response.EnsureSuccessStatusCode();

                // Save to temp file with meaningful name
                var tempFileName = $"{sanitizedTenant}_{sanitizedPackage}_{sanitizedVersion}.zip";
                var tempZipPath = Path.Combine(Path.GetTempPath(), tempFileName);
                _logger.LogInformation("Saving package to temp file: {TempZipPath}", tempZipPath);
                await using (var fileStream = File.Create(tempZipPath))
                {
                    await response.Content.CopyToAsync(fileStream);
                }

                // Extract package
                _logger.LogInformation("Extracting package to: {ExtractPath}", extractPath);
                ZipFile.ExtractToDirectory(tempZipPath, extractPath);
                
                // Cleanup temp file
                File.Delete(tempZipPath);
                _logger.LogInformation("Cleaned up temp file: {TempZipPath}", tempZipPath);

                _logger.LogInformation("Package extracted successfully to {ExtractPath}", extractPath);
                return extractPath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading package {PackageId} v{Version}", packageId, version);
                return null;
            }
        }

        private async Task<string> GetDownloadUrlAsync(string packageId, string version, string tenantSlug, string machineKey)
        {
            try
            {
                var config = _configService.GetConfiguration();

                // Use cached backend API URL, or discover if not available
                var apiUrl = config.BackendApiUrl;
                if (string.IsNullOrEmpty(apiUrl))
                {
                    _logger.LogInformation("Backend API URL not cached, discovering from {OrchestratorUrl}", config.OrchestratorUrl);
                    apiUrl = await DiscoverApiUrlAsync(config.OrchestratorUrl);
                    if (string.IsNullOrEmpty(apiUrl))
                    {
                        _logger.LogError("Failed to discover backend API URL from {OrchestratorUrl}", config.OrchestratorUrl);
                        return null;
                    }

                    // Cache the discovered backend API URL
                    config.BackendApiUrl = apiUrl;
                    _configService.SaveConfiguration(config);
                    _logger.LogInformation("Cached backend API URL: {ApiUrl}", apiUrl);
                }
                else
                {
                    _logger.LogDebug("Using cached backend API URL: {ApiUrl}", apiUrl);
                }

                var url = $"{apiUrl.TrimEnd('/')}/{tenantSlug}/api/packages/{packageId}/versions/{version}/agent-download?machineKey={machineKey}";

                _logger.LogInformation("Requesting download URL from: {Url}", url);
                _logger.LogInformation("Using machine key: {MachineKey}", machineKey.Substring(0, Math.Min(8, machineKey.Length)) + "...");

                var response = await _httpClient.GetAsync(url);
                
                _logger.LogInformation("Response status: {StatusCode}", response.StatusCode);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to get download URL. Status: {StatusCode}, Response: {ErrorContent}", 
                        response.StatusCode, errorContent);
                    return null;
                }
                
                var content = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Response content: {Content}", content);
                
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                
                var result = JsonSerializer.Deserialize<DownloadUrlResponse>(content, options);
                
                _logger.LogInformation("Deserialized result: DownloadUrl={DownloadUrl}, PackageId={PackageId}, Version={Version}", 
                    result?.DownloadUrl, result?.PackageId, result?.Version);
                
                if (result == null || string.IsNullOrEmpty(result.DownloadUrl))
                {
                    _logger.LogError("Invalid response format or empty download URL. Result: {Result}", 
                        result == null ? "null" : $"DownloadUrl='{result.DownloadUrl}'");
                    return null;
                }
                
                _logger.LogInformation("Successfully obtained download URL");
                return result.DownloadUrl;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting download URL for package {PackageId} v{Version}", packageId, version);
                return null;
            }
        }

        /// <summary>
        /// Discovers the backend API URL from the frontend discovery endpoint
        /// </summary>
        /// <param name="orchestratorUrl">The orchestrator URL (e.g., https://cloud.openautomate.me/tenant-name)</param>
        /// <returns>The backend API URL</returns>
        private async Task<string> DiscoverApiUrlAsync(string orchestratorUrl)
        {
            const int maxRetries = 3;
            const int baseDelayMs = 1000;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    // Parse the orchestrator URL to get base domain
                    var (baseDomain, _) = ParseOrchestratorUrl(orchestratorUrl);
                    var discoveryUrl = $"{baseDomain}/api/connection-info";

                    _logger.LogDebug("Attempting to discover API URL from {DiscoveryUrl} (attempt {Attempt}/{MaxRetries})",
                        discoveryUrl, attempt, maxRetries);

                    using var response = await _httpClient.GetAsync(discoveryUrl);
                    response.EnsureSuccessStatusCode();

                    var content = await response.Content.ReadAsStringAsync();
                    var discoveryResponse = JsonSerializer.Deserialize<DiscoveryResponse>(content, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (string.IsNullOrEmpty(discoveryResponse?.ApiUrl))
                    {
                        throw new InvalidOperationException("Discovery response did not contain a valid API URL");
                    }

                    _logger.LogInformation("Successfully discovered backend API URL: {ApiUrl}", discoveryResponse.ApiUrl);
                    return discoveryResponse.ApiUrl;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to discover API URL (attempt {Attempt}/{MaxRetries})", attempt, maxRetries);

                    if (attempt == maxRetries)
                    {
                        _logger.LogError("Failed to discover API URL after {MaxRetries} attempts", maxRetries);
                        throw;
                    }

                    // Exponential backoff with jitter
                    var delay = baseDelayMs * (int)Math.Pow(2, attempt - 1);
                    var jitter = new Random().Next(0, delay / 4);
                    await Task.Delay(delay + jitter);
                }
            }

            return null; // This should never be reached due to the throw above
        }

        /// <summary>
        /// Parses the orchestrator URL to extract base domain and tenant slug
        /// </summary>
        /// <param name="orchestratorUrl">The full orchestrator URL (e.g., https://cloud.openautomate.me/acme-corp)</param>
        /// <returns>A tuple containing the base domain and tenant slug</returns>
        private (string baseDomain, string tenantSlug) ParseOrchestratorUrl(string orchestratorUrl)
        {
            try
            {
                var uri = new Uri(orchestratorUrl.TrimEnd('/'));
                var baseDomain = $"{uri.Scheme}://{uri.Host}";
                if (!uri.IsDefaultPort)
                {
                    baseDomain += $":{uri.Port}";
                }

                var tenantSlug = uri.AbsolutePath.Trim('/');
                if (string.IsNullOrEmpty(tenantSlug))
                {
                    throw new ArgumentException("Orchestrator URL must include a tenant slug (e.g., https://cloud.openautomate.me/tenant-name)");
                }

                _logger.LogDebug("Parsed orchestrator URL: BaseDomain={BaseDomain}, TenantSlug={TenantSlug}", baseDomain, tenantSlug);
                return (baseDomain, tenantSlug);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse orchestrator URL: {OrchestratorUrl}", orchestratorUrl);
                throw;
            }
        }

        private static string SanitizeForPath(string input)
        {
            if (string.IsNullOrEmpty(input))
                return "unknown";
                
            // Replace spaces and special characters with hyphens, convert to lowercase
            var sanitized = input.ToLowerInvariant()
                .Replace(" ", "-")
                .Replace("_", "-")
                .Replace(".", "-");
                
            // Remove any characters that aren't alphanumeric or hyphens
            sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"[^a-z0-9\-]", "");
            
            // Remove multiple consecutive hyphens and trim
            sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"-+", "-").Trim('-');
            
            return string.IsNullOrEmpty(sanitized) ? "unknown" : sanitized;
        }
    }

    /// <summary>
    /// Response model for download URL requests
    /// </summary>
    public class DownloadUrlResponse
    {
        [JsonPropertyName("downloadUrl")]
        public string DownloadUrl { get; set; } = string.Empty;

        [JsonPropertyName("packageId")]
        public string PackageId { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("expiresAt")]
        public DateTime ExpiresAt { get; set; }
    }

    /// <summary>
    /// Response model for the discovery endpoint
    /// </summary>
    public class DiscoveryResponse
    {
        [JsonPropertyName("apiUrl")]
        public string ApiUrl { get; set; } = string.Empty;
    }
} 