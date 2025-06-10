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
                
                var downloadUrl = await GetDownloadUrlAsync(packageId, version, config.MachineKey);
                
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

        private async Task<string> GetDownloadUrlAsync(string packageId, string version, string machineKey)
        {
            try
            {
                var config = _configService.GetConfiguration();
                var url = $"{config.ServerUrl}/api/packages/{packageId}/versions/{version}/agent-download?machineKey={machineKey}";
                
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
} 