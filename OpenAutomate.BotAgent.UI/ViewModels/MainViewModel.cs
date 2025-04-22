using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using OpenAutomate.BotAgent.UI.Models;
using OpenAutomate.BotAgent.UI.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OpenAutomate.BotAgent.UI.ViewModels
{
    /// <summary>
    /// Main ViewModel for the Bot Agent UI
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly ApiClient _apiClient;
        private readonly AgentSettings _agentSettings;
        private string _machineKey;
        private string _orchestratorUrl;
        private string _machineName;
        private bool _isConnected;
        private bool _isConnecting;
        private string _statusMessage;
        private DateTime? _lastConnected;

        public MainViewModel()
        {
            _apiClient = new ApiClient();
            _agentSettings = new AgentSettings();
            _machineName = Environment.MachineName;
            
            ConnectCommand = new RelayCommand<object>(async _ => await ConnectAsync(), _ => CanConnect);
            DisconnectCommand = new RelayCommand<object>(async _ => await DisconnectAsync(), _ => IsConnected);
        }

        /// <summary>
        /// The machine key used for authentication
        /// </summary>
        public string MachineKey
        {
            get => _machineKey;
            set
            {
                if (SetProperty(ref _machineKey, value))
                {
                    ((RelayCommand<object>)ConnectCommand).RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// The URL of the OpenAutomate server
        /// </summary>
        public string OrchestratorUrl
        {
            get => _orchestratorUrl;
            set
            {
                if (SetProperty(ref _orchestratorUrl, value))
                {
                    ((RelayCommand<object>)ConnectCommand).RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// The name of the machine (auto-populated, not editable)
        /// </summary>
        public string MachineName
        {
            get => _machineName;
            private set => SetProperty(ref _machineName, value);
        }

        /// <summary>
        /// Indicates whether the agent is connected to the server
        /// </summary>
        public bool IsConnected
        {
            get => _isConnected;
            private set
            {
                if (SetProperty(ref _isConnected, value))
                {
                    ((RelayCommand<object>)ConnectCommand).RaiseCanExecuteChanged();
                    ((RelayCommand<object>)DisconnectCommand).RaiseCanExecuteChanged();
                    OnPropertyChanged(nameof(ConnectionStatus));
                }
            }
        }

        /// <summary>
        /// Indicates whether a connection attempt is in progress
        /// </summary>
        public bool IsConnecting
        {
            get => _isConnecting;
            private set
            {
                if (SetProperty(ref _isConnecting, value))
                {
                    ((RelayCommand<object>)ConnectCommand).RaiseCanExecuteChanged();
                    ((RelayCommand<object>)DisconnectCommand).RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// Status message to display in the UI
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        /// <summary>
        /// Last connected time
        /// </summary>
        public DateTime? LastConnected
        {
            get => _lastConnected;
            set => SetProperty(ref _lastConnected, value);
        }

        /// <summary>
        /// Connection status text for display
        /// </summary>
        public string ConnectionStatus => IsConnected ? "Connected" : "Disconnected";

        /// <summary>
        /// Status text for display in the UI
        /// </summary>
        public string StatusText => IsConnected ? "Status:" : "Ready to connect";

        /// <summary>
        /// Command to connect to the OpenAutomate server
        /// </summary>
        public ICommand ConnectCommand { get; }

        /// <summary>
        /// Command to disconnect from the OpenAutomate server
        /// </summary>
        public ICommand DisconnectCommand { get; }

        /// <summary>
        /// Determines if the connect command can execute
        /// </summary>
        private bool CanConnect => !string.IsNullOrWhiteSpace(MachineKey) && 
                                 !string.IsNullOrWhiteSpace(OrchestratorUrl) && 
                                 !IsConnecting &&
                                 !IsConnected;

        /// <summary>
        /// Connects to the OpenAutomate server
        /// </summary>
        private async Task ConnectAsync()
        {
            try
            {
                IsConnecting = true;
                StatusMessage = "Connecting...";

                _apiClient.Configure(OrchestratorUrl, MachineKey);
                bool success = await _apiClient.ConnectAsync(MachineName);

                if (success)
                {
                    IsConnected = true;
                    StatusMessage = "Connected successfully";
                    SaveSettings();
                    LastConnected = DateTime.Now;
                }
                else
                {
                    StatusMessage = "Failed to connect. Please check your settings.";
                    MessageBox.Show("Failed to connect to the OpenAutomate server. Please verify your settings and try again.",
                        "Connection Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                MessageBox.Show($"An error occurred: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsConnecting = false;
            }
        }

        /// <summary>
        /// Disconnects from the OpenAutomate server
        /// </summary>
        private async Task DisconnectAsync()
        {
            try
            {
                StatusMessage = "Disconnecting...";
                await _apiClient.DisconnectAsync();
                IsConnected = false;
                StatusMessage = "Disconnected";
                
                // Update the settings to reflect disconnected state
                SaveSettings();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error disconnecting: {ex.Message}";
            }
        }

        /// <summary>
        /// Saves the current settings
        /// </summary>
        private void SaveSettings()
        {
            var config = new ConfigurationModel
            {
                MachineKey = MachineKey,
                OrchestratorUrl = OrchestratorUrl,
                MachineName = MachineName,
                IsConnected = IsConnected,
                LastConnected = LastConnected
            };
            
            _agentSettings.SaveSettings(config);
        }

        /// <summary>
        /// Loads the saved settings if available
        /// </summary>
        public void LoadSettings()
        {
            var config = _agentSettings.LoadSettings();
            if (config != null)
            {
                MachineKey = config.MachineKey;
                OrchestratorUrl = config.OrchestratorUrl;
                MachineName = config.MachineName ?? Environment.MachineName;
                LastConnected = config.LastConnected;
                
                // Don't restore connected state on startup
                // The user should explicitly connect
                IsConnected = false;
                
                StatusMessage = "Settings loaded";
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Sets the value of a property and raises PropertyChanged if the value has changed
        /// </summary>
        /// <typeparam name="T">The type of the property</typeparam>
        /// <param name="storage">Reference to the backing field</param>
        /// <param name="value">The new value</param>
        /// <param name="propertyName">Name of the property (auto-populated by CallerMemberName)</param>
        /// <returns>True if the value was changed, false otherwise</returns>
        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(storage, value))
                return false;

            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
} 