using System;
using System.Windows;
using System.Windows.Controls;

namespace OpenAutomate.BotAgent.UI.Controls
{
    /// <summary>
    /// Interaction logic for CredentialTextBox.xaml
    /// A custom control that displays credentials with masking and allows editing
    /// </summary>
    public partial class CredentialTextBox : UserControl
    {
        private bool _isEditing = false;
        private bool _isInternalUpdate = false;

        public CredentialTextBox()
        {
            InitializeComponent();
            UpdateDisplayText();
        }

        #region Dependency Properties

        /// <summary>
        /// The actual credential value
        /// </summary>
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(string), typeof(CredentialTextBox),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

        public string Value
        {
            get => (string)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        /// <summary>
        /// The text displayed in the TextBox (either masked or actual)
        /// </summary>
        public static readonly DependencyProperty DisplayTextProperty =
            DependencyProperty.Register(nameof(DisplayText), typeof(string), typeof(CredentialTextBox),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public string DisplayText
        {
            get => (string)GetValue(DisplayTextProperty);
            set => SetValue(DisplayTextProperty, value);
        }

        #endregion

        #region Event Handlers

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CredentialTextBox control)
            {
                control.UpdateDisplayText();
            }
        }

        private void MainTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            // Switch to PasswordBox for editing
            _isEditing = true;

            // Set the PasswordBox value to current Value
            PasswordTextBox.Password = Value ?? string.Empty;

            // Hide TextBox and show PasswordBox
            MainTextBox.Visibility = Visibility.Collapsed;
            PasswordTextBox.Visibility = Visibility.Visible;

            // Focus the PasswordBox
            PasswordTextBox.Focus();
            PasswordTextBox.SelectAll();
        }

        private void PasswordTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            // Switch back to TextBox for masked display
            _isEditing = false;

            // Update the Value from PasswordBox
            Value = PasswordTextBox.Password;

            // Show TextBox and hide PasswordBox
            PasswordTextBox.Visibility = Visibility.Collapsed;
            MainTextBox.Visibility = Visibility.Visible;

            // Update display
            UpdateDisplayText();
        }

        private void PasswordTextBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_isInternalUpdate) return;

            // Update the Value as user types
            if (_isEditing)
            {
                Value = PasswordTextBox.Password;
            }
        }

        #endregion

        #region Private Methods

        private void UpdateDisplayText()
        {
            _isInternalUpdate = true;

            try
            {
                // Always show masked text in the TextBox
                // PasswordBox handles the credential input separately
                DisplayText = GetMaskedText(Value);
            }
            finally
            {
                _isInternalUpdate = false;
            }
        }

        private static string GetMaskedText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            if (text.Length <= 8)
                return new string('*', text.Length);

            // Show first 6 characters, then asterisks, then last 6 characters
            var start = text.Substring(0, 6);
            var end = text.Substring(text.Length - 6);
            var middle = new string('*', Math.Max(4, text.Length - 12));

            return $"{start}{middle}{end}";
        }

        #endregion
    }
}
