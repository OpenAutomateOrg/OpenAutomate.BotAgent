using System.Windows;
using System.Windows.Input;
using OpenAutomate.BotAgent.UI.Services;
using OpenAutomate.BotAgent.UI.ViewModels;

namespace OpenAutomate.BotAgent.UI;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        
        LoggingService.Information("MainWindow initializing");
        
        // Handle window closing to dispose resources
        Closing += MainWindow_Closing;
        
        LoggingService.Information("MainWindow initialized");
    }
    
    /// <summary>
    /// Allow dragging the window when the user clicks on the title bar
    /// </summary>
    private void DragWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            LoggingService.Debug("Window drag started");
            this.DragMove();
        }
    }
    
    /// <summary>
    /// Minimize the window when the minimize button is clicked
    /// </summary>
    private void MinimizeWindow(object sender, RoutedEventArgs e)
    {
        LoggingService.Debug("Window minimized");
        this.WindowState = WindowState.Minimized;
    }
    
    /// <summary>
    /// Close the window when the close button is clicked
    /// </summary>
    private void CloseWindow(object sender, RoutedEventArgs e)
    {
        LoggingService.Information("Window close requested by user");
        this.Close();
    }
    
    /// <summary>
    /// Handle window closing to dispose of resources
    /// </summary>
    private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        LoggingService.Information("Window closing, disposing resources");
        
        // Dispose the view model when the window is closed
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.Dispose();
        }
        
        LoggingService.Information("Window closed");
    }
}