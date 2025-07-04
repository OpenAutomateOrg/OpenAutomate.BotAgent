using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using OpenAutomate.BotAgent.UI.Models;
using OpenAutomate.BotAgent.UI.Services;

namespace OpenAutomate.BotAgent.UI.ViewModels
{
    /// <summary>
    /// Main view model for the configuration window
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly IApiClient _apiClient;
        private readonly ConfigurationManager _configManager;
        private readonly ConnectionMonitor _connectionMonitor;
        private readonly Dispatcher _dispatcher;
        
        private ConfigurationModel _configModel;
        private bool _isBusy;
        private string _statusMessage;
        private bool _isDisposed;
        
        public MainViewModel()
        {
            _apiClient = new ApiClient();
            _configManager = new ConfigurationManager();
            _connectionMonitor = new ConnectionMonitor(_apiClient);
            _dispatcher = Dispatcher.CurrentDispatcher;
            
            _configModel = new ConfigurationModel 
            { 
                MachineName = Environment.MachineName,
                LoggingLevel = "INFO"
            };
            
            // Subscribe to configuration property changes to refresh command states
            _configModel.PropertyChanged += ConfigPropertyChanged;
            
            SaveCommand = new RelayCommand(async _ => await SaveConfigAsync(), _ => CanSave());
            ConnectCommand = new RelayCommand(async _ => await ConnectAsync(), _ => CanConnect());
            DisconnectCommand = new RelayCommand(async _ => await DisconnectAsync(), _ => IsConnected);
            
            // Subscribe to connection status changes
            _connectionMonitor.ConnectionStatusChanged += OnConnectionStatusChanged;
            
            LoggingService.Information("MainViewModel initialized");
            
            // Load configuration and start monitoring
            _ = InitializeAsync();
        }
        
        /// <summary>
        /// The configuration model
        /// </summary>
        public ConfigurationModel Config
        {
            get => _configModel;
            set
            {
                if (_configModel != null)
                {
                    // Unsubscribe from old config's property changes
                    _configModel.PropertyChanged -= ConfigPropertyChanged;
                }
                
                if (_configModel != value)
                {
                    _configModel = value;
                    
                    // Subscribe to new config's property changes
                    if (_configModel != null)
                    {
                        _configModel.PropertyChanged += ConfigPropertyChanged;
                    }
                    
                    OnPropertyChanged();
                    RefreshCommandStates();
                }
            }
        }
        
        /// <summary>
        /// Handler for configuration property changes to ensure UI updates
        /// </summary>
        private void ConfigPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Refresh command states when any config property changes
            RefreshCommandStates();
        }
        
        /// <summary>
        /// Indicates whether an operation is in progress
        /// </summary>
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (_isBusy != value)
                {
                    _isBusy = value;
                    OnPropertyChanged();
                    RefreshCommandStates();
                }
            }
        }
        
        /// <summary>
        /// Status message to display to the user
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                if (_statusMessage != value)
                {
                    _statusMessage = value;
                    OnPropertyChanged();
                }
            }
        }
        
        /// <summary>
        /// Command to save the configuration
        /// </summary>
        public ICommand SaveCommand { get; }
        
        /// <summary>
        /// Command to connect to the server
        /// </summary>
        public ICommand ConnectCommand { get; }
        
        /// <summary>
        /// Command to disconnect from the server
        /// </summary>
        public ICommand DisconnectCommand { get; }
        
        /// <summary>
        /// Gets whether the bot is connected to the server
        /// </summary>
        public bool IsConnected => Config.IsConnected;
        
        /// <summary>
        /// Initializes the view model by loading configuration and starting monitoring
        /// </summary>
        private async Task InitializeAsync()
        {
            IsBusy = true;
            StatusMessage = "Initializing...";
            LoggingService.Information("Initializing MainViewModel");
            
            try
            {
                // First try to load from config file
                var fileConfig = await _configManager.LoadConfigurationAsync();
                
                if (!string.IsNullOrEmpty(fileConfig.OrchestratorUrl) && !string.IsNullOrEmpty(fileConfig.MachineKey))
                {
                    // If we have basic config, use it
                    Config = fileConfig;
                    LoggingService.Information("Loaded configuration from file, OrchestratorUrl: {OrchestratorUrl}", fileConfig.OrchestratorUrl);
                    
                    // Now try to get the service config (for latest connection status)
                    try
                    {
                        var serviceConfig = await _apiClient.GetConfigAsync();
                        
                        // Update only the connection status
                        Config.IsConnected = serviceConfig.IsConnected;
                        LoggingService.Information("Updated connection status from service: {IsConnected}", serviceConfig.IsConnected);
                    }
                    catch (Exception ex)
                    {
                        LoggingService.Warning(ex.Message, "Failed to get configuration from service, continuing with file config");
                    }
                }
                else
                {
                    // No valid config file, try to get from service
                    LoggingService.Information("No valid configuration in file, trying to get from service");
                    try
                    {
                        var serviceConfig = await _apiClient.GetConfigAsync();
                        
                        if (!string.IsNullOrEmpty(serviceConfig.OrchestratorUrl) && !string.IsNullOrEmpty(serviceConfig.MachineKey))
                        {
                            Config = serviceConfig;
                            LoggingService.Information("Loaded configuration from service");
                        }
                        else
                        {
                            // Service returned empty config, use default with just machine name set
                            LoggingService.Information("Service returned empty configuration, using default with just machine name");
                            Config.MachineName = Environment.MachineName;
                            Config.OrchestratorUrl = string.Empty;
                            Config.MachineKey = string.Empty;
                            Config.IsConnected = false;
                            Config.LoggingLevel = "INFO";
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggingService.Warning(ex.Message, "Failed to get configuration from service, using default");
                        // Use default with empty fields to allow user to enter values
                        Config.MachineName = Environment.MachineName;
                        Config.OrchestratorUrl = string.Empty;
                        Config.MachineKey = string.Empty;
                        Config.IsConnected = false;
                        Config.LoggingLevel = "INFO";
                    }
                }
                
                // Always set the MachineName to the environment machine name
                Config.MachineName = Environment.MachineName;
                LoggingService.Information("Set MachineName to {MachineName}", Environment.MachineName);
                
                // Start monitoring connection status
                _connectionMonitor.StartMonitoring();
                
                StatusMessage = "Ready";
                LoggingService.Information("MainViewModel initialization complete");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Initialization error: {ex.Message}";
                LoggingService.Error(ex, "Error initializing MainViewModel");
            }
            finally
            {
                IsBusy = false;
                RefreshConnectionState();
                RefreshCommandStates(); // Make sure to refresh commands explicitly after initialization
            }
        }
        
        /// <summary>
        /// Saves the configuration to both the service and config file
        /// </summary>
        private async Task SaveConfigAsync()
        {
            IsBusy = true;
            StatusMessage = "Saving configuration...";
            LoggingService.Information("Saving configuration");
            
            try
            {
                // Ensure MachineName is correctly set before saving
                Config.MachineName = Environment.MachineName;
                
                // First save to the service
                var serviceSuccess = await _apiClient.UpdateConfigAsync(Config);
                
                if (serviceSuccess)
                {
                    // If service save succeeded, save to file
                    await _configManager.SaveConfigurationAsync(Config);
                    StatusMessage = "Configuration saved";
                    LoggingService.Information("Configuration saved successfully");
                }
                else
                {
                    StatusMessage = "Failed to save configuration to service";
                    LoggingService.Warning("Failed to save configuration to service");
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error saving configuration: {ex.Message}";
                LoggingService.Error(ex, "Error saving configuration");
            }
            finally
            {
                IsBusy = false;
            }
        }
        
        /// <summary>
        /// Connects to the server
        /// </summary>
        private async Task ConnectAsync()
        {
            IsBusy = true;
            StatusMessage = "Connecting to server...";
            LoggingService.Information("Connecting to orchestrator with URL: {OrchestratorUrl}", Config.OrchestratorUrl);
            
            try
            {
                // Ensure MachineName is correctly set
                Config.MachineName = Environment.MachineName;
                
                // First save the configuration to ensure it's up to date
                await _apiClient.UpdateConfigAsync(Config);
                
                // Reset connection status cache to ensure we get a fresh status
                _apiClient.ResetConnectionStatusCache();
                
                // Then attempt to connect
                var success = await _apiClient.ConnectAsync();
                
                if (success)
                {
                    LoggingService.Information("Connect API call returned success=true");
                    // Don't rely solely on the API response - verify the connection
                    await VerifyConnectionStatusAndUpdateUI();
                }
                else
                {
                    // Explicitly set disconnected when connect fails
                    LoggingService.Warning("Connect API call returned success=false");
                    Config.IsConnected = false;
                    StatusMessage = "Failed to connect to server";
                    RefreshConnectionState();
                }
            }
            catch (Exception ex)
            {
                // Ensure we set IsConnected to false when an exception occurs
                Config.IsConnected = false;
                StatusMessage = $"Error connecting to server: {ex.Message}";
                LoggingService.Error(ex, "Error connecting to server: {ErrorMessage}", ex.Message);
                RefreshConnectionState();
            }
            finally
            {
                IsBusy = false;
            }
        }
        
        /// <summary>
        /// Verifies the actual connection status and updates UI accordingly
        /// </summary>
        private async Task VerifyConnectionStatusAndUpdateUI()
        {
            try
            {
                // Force a delay to allow server state to propagate
                await Task.Delay(1000);
                
                // Force a fresh connection status check
                _apiClient.ResetConnectionStatusCache();
                var verifiedStatus = await _apiClient.GetConnectionStatusAsync();
                
                LoggingService.Information("Connection verification check: {Status}", 
                    verifiedStatus ? "Connected" : "Disconnected");
                
                // Always trust the verified status over what we think it should be
                Config.IsConnected = verifiedStatus;
                
                if (verifiedStatus)
                {
                    StatusMessage = "Connected to server";
                    LoggingService.Information("Connection verified: Connected");
                    
                    // Save the configuration with the updated connection status
                    await _configManager.SaveConfigurationAsync(Config);
                }
                else
                {
                    StatusMessage = "Failed to connect to server (verified)";
                    LoggingService.Warning("Connection verification failed: Server reports disconnected");
                }
                
                // Always refresh UI after verification
                RefreshConnectionState();
            }
            catch (Exception ex)
            {
                // If verification fails, assume disconnected
                Config.IsConnected = false;
                StatusMessage = $"Connection verification error: {ex.Message}";
                LoggingService.Error(ex, "Error verifying connection status: {ErrorMessage}", ex.Message);
                RefreshConnectionState();
            }
        }
        
        /// <summary>
        /// Disconnects from the server
        /// </summary>
        private async Task DisconnectAsync()
        {
            IsBusy = true;
            StatusMessage = "Disconnecting from server...";
            LoggingService.Information("Disconnecting from server");
            
            try
            {
                // Reset connection status cache before attempting to disconnect
                _apiClient.ResetConnectionStatusCache();
                
                var success = await _apiClient.DisconnectAsync();
                if (success)
                {
                    LoggingService.Information("Disconnect API call returned success=true");
                    // Verify the disconnection to ensure UI reflects actual state
                    await VerifyDisconnectionStatusAndUpdateUI();
                }
                else
                {
                    LoggingService.Warning("Disconnect API call returned success=false");
                    StatusMessage = "Failed to disconnect from server";
                    
                    // Don't assume connection state - verify it
                    await VerifyConnectionStatusAndUpdateUI();
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error disconnecting from server: {ex.Message}";
                LoggingService.Error(ex, "Error disconnecting from server: {ErrorMessage}", ex.Message);
                
                // Verify actual connection status on error
                await VerifyConnectionStatusAndUpdateUI();
            }
            finally
            {
                IsBusy = false;
            }
        }
        
        /// <summary>
        /// Verifies disconnection status and updates UI accordingly
        /// </summary>
        private async Task VerifyDisconnectionStatusAndUpdateUI()
        {
            try
            {
                // Force a delay to allow server state to propagate
                await Task.Delay(1000);
                
                // Force a fresh connection status check
                _apiClient.ResetConnectionStatusCache();
                var verifiedStatus = await _apiClient.GetConnectionStatusAsync();
                
                LoggingService.Information("Disconnection verification check: {Status}", 
                    verifiedStatus ? "Still connected" : "Disconnected");
                
                // Always trust the verified status
                Config.IsConnected = verifiedStatus;
                
                if (!verifiedStatus)
                {
                    StatusMessage = "Disconnected from server";
                    LoggingService.Information("Disconnection verified: Disconnected");
                    
                    // Save the configuration with the updated connection status
                    await _configManager.SaveConfigurationAsync(Config);
                }
                else
                {
                    StatusMessage = "Failed to disconnect completely (still connected)";
                    LoggingService.Warning("Disconnection verification failed: Server reports still connected");
                }
                
                // Always refresh UI after verification
                RefreshConnectionState();
            }
            catch (Exception ex)
            {
                // If verification fails, assume disconnected for safety
                Config.IsConnected = false;
                StatusMessage = $"Disconnection verification error: {ex.Message}";
                LoggingService.Error(ex, "Error verifying disconnection status: {ErrorMessage}", ex.Message);
                RefreshConnectionState();
            }
        }
        
        /// <summary>
        /// Handles connection status changes from the monitor
        /// </summary>
        private void OnConnectionStatusChanged(object sender, ConnectionStatusChangedEventArgs e)
        {
            // Dispatch to UI thread since this comes from a timer
            _dispatcher.Invoke(() =>
            {
                LoggingService.Information("Received connection status change notification: {IsConnected}",
                    e.IsConnected ? "Connected" : "Disconnected");
                
                var oldConnected = Config.IsConnected;
                
                // Always update to the latest status
                Config.IsConnected = e.IsConnected;
                
                if (oldConnected != e.IsConnected)
                {
                    LoggingService.Information("Connection status changed UI from {OldStatus} to {NewStatus}",
                        oldConnected ? "Connected" : "Disconnected",
                        e.IsConnected ? "Connected" : "Disconnected");
                    
                    StatusMessage = e.StatusMessage;
                    
                    // Save the updated connection status to the configuration file
                    // to ensure persistence between restarts
                    try
                    {
                        _configManager.SaveConfigurationAsync(Config).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        LoggingService.Error(ex, "Failed to save connection status to configuration");
                    }
                }
                else
                {
                    LoggingService.Debug("Connection status unchanged: {Status}",
                        e.IsConnected ? "Connected" : "Disconnected");
                }
                
                // Always refresh UI state to ensure consistency
                RefreshConnectionState();
            });
        }
        
        /// <summary>
        /// Refreshes the connection state properties
        /// </summary>
        private void RefreshConnectionState()
        {
            OnPropertyChanged(nameof(IsConnected));
            RefreshCommandStates();
        }
        
        /// <summary>
        /// Refreshes the command states
        /// </summary>
        private void RefreshCommandStates()
        {
            ((RelayCommand)SaveCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ConnectCommand).RaiseCanExecuteChanged();
            ((RelayCommand)DisconnectCommand).RaiseCanExecuteChanged();
        }
        
        /// <summary>
        /// Determines whether the configuration can be saved
        /// </summary>
        private bool CanSave()
        {
            return !IsBusy &&
                  !string.IsNullOrWhiteSpace(Config.OrchestratorUrl) &&
                  !string.IsNullOrWhiteSpace(Config.MachineKey) &&
                  !string.IsNullOrWhiteSpace(Config.MachineName);
        }
        
        /// <summary>
        /// Determines whether the bot can connect to the server
        /// </summary>
        private bool CanConnect()
        {
            var canConnect = !IsBusy &&
                  !IsConnected &&
                  !string.IsNullOrWhiteSpace(Config.OrchestratorUrl) &&
                  !string.IsNullOrWhiteSpace(Config.MachineKey) &&
                  !string.IsNullOrWhiteSpace(Config.MachineName);

            LoggingService.Debug($"CanConnect check: {canConnect}, OrchestratorUrl: {!string.IsNullOrWhiteSpace(Config.OrchestratorUrl)}, MachineKey: {!string.IsNullOrWhiteSpace(Config.MachineKey)}");
            return canConnect;
        }
        
        /// <summary>
        /// Clean up resources
        /// </summary>
        public void Dispose()
        {
            if (!_isDisposed)
            {
                LoggingService.Information("Disposing MainViewModel resources");
                _connectionMonitor.ConnectionStatusChanged -= OnConnectionStatusChanged;
                _connectionMonitor.StopMonitoring();
                _connectionMonitor.Dispose();
                
                if (_apiClient is IDisposable disposableClient)
                {
                    disposableClient.Dispose();
                }
                
                _isDisposed = true;
            }
        }
        
        public event PropertyChangedEventHandler PropertyChanged;
        
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    
    /// <summary>
    /// Command implementation
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Predicate<object> _canExecute;
        
        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }
        
        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute(parameter);
        }
        
        public void Execute(object parameter)
        {
            _execute(parameter);
        }
        
        public event EventHandler CanExecuteChanged;
        
        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
} 