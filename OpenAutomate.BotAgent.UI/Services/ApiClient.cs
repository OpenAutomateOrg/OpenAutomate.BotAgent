using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using OpenAutomate.BotAgent.UI.Models;

namespace OpenAutomate.BotAgent.UI.Services
{
    /// <summary>
    /// Client for communicating with the OpenAutomate server API
    /// </summary>
    public class ApiClient
    {
        private readonly HttpClient _httpClient;
        private string _baseUrl;
        private string _machineKey;
        private string _tenantSlug;
        
        public ApiClient()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        }
        
        /// <summary>
        /// Configures the API client with the server URL and machine key
        /// </summary>
        public void Configure(string serverUrl, string machineKey)
        {
            _baseUrl = serverUrl.TrimEnd('/');
            _machineKey = machineKey;
            
            // Extract tenant slug from URL if present
            var uri = new Uri(_baseUrl);
            var segments = uri.Segments;
            
            if (segments.Length > 1)
            {
                _tenantSlug = segments[segments.Length - 1].TrimEnd('/');
            }
        }
        
        /// <summary>
        /// Connects the Bot Agent with the server
        /// </summary>
        public async Task<bool> ConnectAsync(string machineName)
        {
            if (string.IsNullOrEmpty(_baseUrl) || string.IsNullOrEmpty(_machineKey))
            {
                throw new InvalidOperationException("API client must be configured before connecting");
            }

            try
            {
                var request = new
                {
                    MachineKey = _machineKey,
                    MachineName = machineName
                };
                
                var response = await _httpClient.PostAsJsonAsync(
                    $"{_baseUrl}/{_tenantSlug}/api/botAgent/connect", request);
                    
                response.EnsureSuccessStatusCode();
                return true;
            }
            catch (Exception ex)
            {
                // Log the error
                Console.WriteLine($"Connection error: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Disconnects the Bot Agent from the server
        /// </summary>
        public async Task<bool> DisconnectAsync()
        {
            if (string.IsNullOrEmpty(_baseUrl) || string.IsNullOrEmpty(_machineKey))
            {
                return false;
            }

            try
            {
                var request = new
                {
                    MachineKey = _machineKey,
                    Status = "Offline"
                };
                
                var response = await _httpClient.PostAsJsonAsync(
                    $"{_baseUrl}/{_tenantSlug}/api/botAgent/status", request);
                    
                response.EnsureSuccessStatusCode();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
} 