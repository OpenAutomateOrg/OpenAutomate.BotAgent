using System;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using OpenAutomate.BotAgent.UI.Models;
using Timer = System.Timers.Timer;

namespace OpenAutomate.BotAgent.UI.Services
{
    /// <summary>
    /// Connection status change event args
    /// </summary>
    public class ConnectionStatusChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Gets whether the service is connected
        /// </summary>
        public bool IsConnected { get; }
        
        /// <summary>
        /// Gets additional status information
        /// </summary>
        public string StatusMessage { get; }
        
        /// <summary>
        /// Initializes a new instance of the ConnectionStatusChangedEventArgs class
        /// </summary>
        public ConnectionStatusChangedEventArgs(bool isConnected, string statusMessage)
        {
            IsConnected = isConnected;
            StatusMessage = statusMessage;
        }
    }

    /// <summary>
    /// Monitors the connection status of the BotAgent service
    /// </summary>
    public class ConnectionMonitor : IDisposable
    {
        private readonly IApiClient _apiClient;
        private readonly Timer _statusCheckTimer;
        private bool _isConnected;
        private bool _disposed;
        
        /// <summary>
        /// Event raised when the connection status changes
        /// </summary>
        public event EventHandler<ConnectionStatusChangedEventArgs> ConnectionStatusChanged;
        
        /// <summary>
        /// Initializes a new instance of the ConnectionMonitor class
        /// </summary>
        public ConnectionMonitor(IApiClient apiClient)
        {
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            
            // Set up a timer to check connection status periodically
            _statusCheckTimer = new Timer(30000);  // Check every 30 seconds (changed from 5 seconds)
            _statusCheckTimer.Elapsed += OnStatusCheckTimerElapsed;
            _statusCheckTimer.AutoReset = true;
            
            LoggingService.Debug("ConnectionMonitor initialized");
        }
        
        /// <summary>
        /// Gets whether the service is currently connected
        /// </summary>
        public bool IsConnected => _isConnected;
        
        /// <summary>
        /// Starts monitoring the connection status
        /// </summary>
        public void StartMonitoring()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ConnectionMonitor));
            
            LoggingService.Information("Starting connection monitoring");
            
            // Ensure we have a fresh status check at start
            _isConnected = false; // Initially assume disconnected until proven otherwise
            
            // Start the timer to check status periodically
            _statusCheckTimer.Start();
            
            // Perform an initial check immediately and don't throttle it
            _ = Task.Run(async () => {
                try {
                    // Force a cache reset to ensure fresh check
                    if (_apiClient is ApiClient apiClient) {
                        apiClient.ResetConnectionStatusCache();
                    }
                    
                    await CheckConnectionStatusAsync(forceCheck: true);
                }
                catch (Exception ex) {
                    LoggingService.Error(ex, "Error performing initial connection check");
                }
            });
        }
        
        /// <summary>
        /// Stops monitoring the connection status
        /// </summary>
        public void StopMonitoring()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ConnectionMonitor));
                
            _statusCheckTimer.Stop();
            LoggingService.Information("Connection monitoring stopped");
        }
        
        /// <summary>
        /// Handles the timer elapsed event
        /// </summary>
        private void OnStatusCheckTimerElapsed(object sender, ElapsedEventArgs e)
        {
            _ = CheckConnectionStatusAsync();
        }
        
        /// <summary>
        /// Checks the service connection status
        /// </summary>
        private async Task CheckConnectionStatusAsync(bool forceCheck = false)
        {
            try
            {
                LoggingService.Debug("Checking connection status...");
                
                // Force cache reset if requested
                if (forceCheck && _apiClient is ApiClient apiClient) {
                    apiClient.ResetConnectionStatusCache();
                }
                
                var isConnected = await _apiClient.GetConnectionStatusAsync();
                
                // Only raise event if status has changed
                if (isConnected != _isConnected)
                {
                    _isConnected = isConnected;
                    
                    var statusMessage = isConnected 
                        ? "Connected to BotAgent service" 
                        : "Disconnected from BotAgent service";
                    
                    LoggingService.Information("Connection status changed: {IsConnected}, {Status}", 
                        isConnected ? "Connected" : "Disconnected", statusMessage);
                    
                    OnConnectionStatusChanged(new ConnectionStatusChangedEventArgs(isConnected, statusMessage));
                }
                else
                {
                    LoggingService.Debug("Connection status unchanged: {IsConnected}", 
                        isConnected ? "Connected" : "Disconnected");
                }
            }
            catch (Exception ex)
            {
                // Connection failure, consider disconnected
                LoggingService.Warning("Error checking connection status: {ErrorMessage}", ex.Message);
                
                if (_isConnected)
                {
                    _isConnected = false;
                    var statusMessage = $"Connection error: {ex.Message}";
                    LoggingService.Warning("Connection lost to service: {ErrorMessage}", ex.Message);
                    OnConnectionStatusChanged(new ConnectionStatusChangedEventArgs(false, statusMessage));
                }
            }
        }
        
        /// <summary>
        /// Raises the ConnectionStatusChanged event
        /// </summary>
        protected virtual void OnConnectionStatusChanged(ConnectionStatusChangedEventArgs e)
        {
            ConnectionStatusChanged?.Invoke(this, e);
        }
        
        /// <summary>
        /// Disposes resources
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                LoggingService.Debug("Disposing ConnectionMonitor");
                _statusCheckTimer.Stop();
                _statusCheckTimer.Elapsed -= OnStatusCheckTimerElapsed;
                _statusCheckTimer.Dispose();
                _disposed = true;
            }
        }
    }
} 