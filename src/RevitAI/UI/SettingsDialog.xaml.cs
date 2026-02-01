using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RevitAI.Models;
using RevitAI.Services;

namespace RevitAI.UI;

/// <summary>
/// Settings dialog for RevitAI configuration.
/// Allows users to enter API key and adjust model parameters.
/// </summary>
public partial class SettingsDialog : Window
{
    private readonly ConfigurationService _configService;

    public SettingsDialog()
    {
        InitializeComponent();
        _configService = ConfigurationService.Instance;
        LoadSettings();
    }

    private void LoadSettings()
    {
        // Load API key (if set, show placeholder)
        if (_configService.HasApiKey)
        {
            // Don't show actual key, just indicate one is set
            ApiKeyBox.Password = _configService.ApiKey ?? string.Empty;
        }

        // Load model
        ModelComboBox.Text = _configService.Model;

        // Load temperature
        TemperatureSlider.Value = _configService.Temperature;

        // Load max tokens
        MaxTokensTextBox.Text = _configService.MaxTokens.ToString();

        // Load context verbosity
        VerbosityComboBox.SelectedIndex = _configService.ContextVerbosity;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        // Validate max tokens
        if (!int.TryParse(MaxTokensTextBox.Text, out var maxTokens) || maxTokens < 100 || maxTokens > 128000)
        {
            MessageBox.Show(
                "Max Tokens must be a number between 100 and 128000.",
                "Validation Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            MaxTokensTextBox.Focus();
            return;
        }

        // Validate model is not empty
        var model = ModelComboBox.Text?.Trim();
        if (string.IsNullOrEmpty(model))
        {
            MessageBox.Show(
                "Please select or enter a model name.",
                "Validation Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            ModelComboBox.Focus();
            return;
        }

        // Save API key only if changed (not empty and not the placeholder)
        var newApiKey = ApiKeyBox.Password;
        if (!string.IsNullOrEmpty(newApiKey))
        {
            _configService.ApiKey = newApiKey;
        }

        // Save other settings
        _configService.Model = model;
        _configService.Temperature = TemperatureSlider.Value;
        _configService.MaxTokens = maxTokens;
        _configService.ContextVerbosity = VerbosityComboBox.SelectedIndex;

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        var apiKey = ApiKeyBox.Password;
        if (string.IsNullOrEmpty(apiKey))
        {
            ShowConnectionStatus("Please enter an API key first.", isError: true);
            return;
        }

        TestConnectionButton.IsEnabled = false;
        ShowConnectionStatus("Testing connection...", isError: false);

        try
        {
            // Temporarily save the API key for testing
            var originalKey = _configService.ApiKey;
            _configService.ApiKey = apiKey;

            using var apiService = new ClaudeApiService(_configService);

            // Send a minimal test message
            var testMessages = new List<ClaudeMessage>
            {
                ClaudeMessage.User("Say 'ok' and nothing else.")
            };

            var testSettings = new ApiSettings
            {
                Model = ModelComboBox.Text ?? "claude-sonnet-4-5-20250929",
                MaxTokens = 10,
                Temperature = 0
            };

            var response = await apiService.SendMessageAsync(
                systemPrompt: null,
                messages: testMessages,
                tools: null,
                settingsOverride: testSettings);

            if (response != null)
            {
                ShowConnectionStatus("Connection successful!", isError: false);
            }
            else
            {
                // Restore original key if test failed
                _configService.ApiKey = originalKey;
                ShowConnectionStatus("Connection failed: No response received.", isError: true);
            }
        }
        catch (ClaudeApiException ex)
        {
            ShowConnectionStatus($"Connection failed: {ex.Message}", isError: true);
        }
        catch (Exception ex)
        {
            ShowConnectionStatus($"Connection failed: {ex.Message}", isError: true);
        }
        finally
        {
            TestConnectionButton.IsEnabled = true;
        }
    }

    private void ShowConnectionStatus(string message, bool isError)
    {
        ConnectionStatusText.Text = message;
        ConnectionStatusText.Foreground = isError
            ? new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F)) // Red
            : new SolidColorBrush(Color.FromRgb(0x38, 0x8E, 0x3C)); // Green
        ConnectionStatusText.Visibility = Visibility.Visible;
    }
}
