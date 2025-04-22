using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Media.Animation;
using OpenAutomate.BotAgent.UI.ViewModels;

namespace OpenAutomate.BotAgent.UI;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly Storyboard _connectingAnimation;
    
    public MainWindow()
    {
        InitializeComponent();
        
        _viewModel = DataContext as MainViewModel;
        _connectingAnimation = FindResource("ConnectingAnimation") as Storyboard;
        
        // Load saved settings if available
        if (_viewModel != null)
        {
            _viewModel.LoadSettings();
            
            // Subscribe to the IsConnecting property to start/stop animation
            _viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.IsConnecting))
                {
                    if (_viewModel.IsConnecting && _connectingAnimation != null)
                    {
                        _connectingAnimation.Begin();
                    }
                    else if (!_viewModel.IsConnecting && _connectingAnimation != null)
                    {
                        _connectingAnimation.Stop();
                    }
                }
            };
        }
        
        // Add application closing handler
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        // Check if we're connected and ask user if they want to disconnect before closing
        if (_viewModel != null && _viewModel.IsConnected)
        {
            var result = MessageBox.Show(
                "You are currently connected to the OpenAutomate server. Do you want to disconnect before closing?",
                "Confirm Disconnect",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);
            
            switch (result)
            {
                case MessageBoxResult.Yes:
                    // Execute the disconnect command
                    if (_viewModel.DisconnectCommand.CanExecute(null))
                    {
                        _viewModel.DisconnectCommand.Execute(null);
                    }
                    break;
                case MessageBoxResult.Cancel:
                    // Cancel the closing operation
                    e.Cancel = true;
                    break;
                // For No, just let the window close
            }
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}