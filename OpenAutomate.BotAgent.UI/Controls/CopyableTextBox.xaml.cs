using System;
using System.Windows;
using System.Windows.Controls;

namespace OpenAutomate.BotAgent.UI.Controls
{
    /// <summary>
    /// Interaction logic for CopyableTextBox.xaml
    /// A read-only text box with a copy button
    /// </summary>
    public partial class CopyableTextBox : UserControl
    {
        public CopyableTextBox()
        {
            InitializeComponent();
        }

        #region Dependency Properties

        /// <summary>
        /// The text to display and copy
        /// </summary>
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(nameof(Text), typeof(string), typeof(CopyableTextBox),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        #endregion

        #region Event Handlers

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(Text))
                {
                    Clipboard.SetText(Text);
                    
                    // Visual feedback - briefly change the button appearance
                    ShowCopyFeedback();
                }
            }
            catch (Exception ex)
            {
                // Handle clipboard access issues gracefully
                MessageBox.Show($"Failed to copy to clipboard: {ex.Message}", "Copy Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        #endregion

        #region Private Methods

        private async void ShowCopyFeedback()
        {
            // Change the icon color to indicate successful copy
            var originalBrush = ((System.Windows.Shapes.Path)CopyButton.Content).Fill;
            ((System.Windows.Shapes.Path)CopyButton.Content).Fill = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(34, 197, 94)); // Green color
            
            // Reset after a short delay
            await System.Threading.Tasks.Task.Delay(500);
            
            ((System.Windows.Shapes.Path)CopyButton.Content).Fill = originalBrush;
        }

        #endregion
    }
}
