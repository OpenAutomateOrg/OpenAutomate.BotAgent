using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace OpenAutomate.BotAgent.UI.Models
{
    /// <summary>
    /// Model class for the Bot Agent configuration
    /// </summary>
    public class ConfigurationModel : INotifyPropertyChanged
    {
        private string _serverUrl = string.Empty;
        private string _machineKey = string.Empty;
        private string _machineName;
        private bool _isConnected;
        private string _loggingLevel;

        /// <summary>
        /// Server URL including tenant slug (e.g., http://open-bot.live/)
        /// </summary>
        public string ServerUrl
        {
            get => _serverUrl;
            set 
            { 
                if (_serverUrl != value)
                {
                    _serverUrl = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Machine key for authentication
        /// </summary>
        public string MachineKey
        {
            get => _machineKey;
            set
            {
                if (_machineKey != value)
                {
                    _machineKey = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Name of the machine
        /// </summary>
        public string MachineName
        {
            get => _machineName;
            set
            {
                if (_machineName != value)
                {
                    _machineName = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Indicates whether the bot is connected to the server
        /// </summary>
        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                if (_isConnected != value)
                {
                    _isConnected = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Logging level (Debug, Info, Warning, Error)
        /// </summary>
        public string LoggingLevel
        {
            get => _loggingLevel;
            set
            {
                if (_loggingLevel != value)
                {
                    _loggingLevel = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
} 