// RevitAI - AI-powered assistant for Autodesk Revit
// Copyright (C) 2025 Bryan McDonald
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.ComponentModel;
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
    private readonly UsageTracker _usageTracker;
    private bool _isLoading;

    public SettingsDialog()
    {
        InitializeComponent();
        _configService = ConfigurationService.Instance;
        _usageTracker = UsageTracker.Instance;

        // Subscribe to usage tracker changes
        _usageTracker.PropertyChanged += UsageTracker_PropertyChanged;

        _isLoading = true;
        LoadSettings();
        _isLoading = false;

        UpdateUsageDisplay();
    }

    protected override void OnClosed(EventArgs e)
    {
        // Unsubscribe from usage tracker
        _usageTracker.PropertyChanged -= UsageTracker_PropertyChanged;
        base.OnClosed(e);
    }

    private void UsageTracker_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Update UI on the dispatcher thread
        Dispatcher.InvokeAsync(UpdateUsageDisplay);
    }

    private void UpdateUsageDisplay()
    {
        InputTokensText.Text = _usageTracker.InputTokens.ToString("N0");
        OutputTokensText.Text = _usageTracker.OutputTokens.ToString("N0");
        TotalTokensText.Text = _usageTracker.TotalTokens.ToString("N0");
        EstimatedCostText.Text = _usageTracker.FormattedCost;
    }

    private void LoadSettings()
    {
        // Load provider selection
        var provider = _configService.AiProvider;
        ProviderComboBox.SelectedIndex = provider == "Gemini" ? 1 : 0;

        // Load Claude API key
        if (_configService.HasApiKey)
        {
            ApiKeyBox.Password = _configService.ApiKey ?? string.Empty;
        }

        // Load Gemini API key
        if (_configService.HasGeminiApiKey)
        {
            GeminiApiKeyBox.Password = _configService.GeminiApiKey ?? string.Empty;
        }

        // Load model (provider-specific)
        UpdateModelListForProvider(provider);
        ModelComboBox.Text = provider == "Gemini" ? _configService.GeminiModel : _configService.Model;

        // Load temperature
        TemperatureSlider.Value = _configService.Temperature;

        // Load max tokens
        MaxTokensTextBox.Text = _configService.MaxTokens.ToString();

        // Load context verbosity
        VerbosityComboBox.SelectedIndex = _configService.ContextVerbosity;

        // Load safety settings
        SkipConfirmationsCheckBox.IsChecked = _configService.SkipConfirmations;
        DryRunModeCheckBox.IsChecked = _configService.DryRunMode;

        // Update UI visibility
        UpdateProviderVisibility(provider);
    }

    private void ProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;

        var selectedItem = ProviderComboBox.SelectedItem as ComboBoxItem;
        var provider = selectedItem?.Tag?.ToString() ?? "Claude";

        UpdateProviderVisibility(provider);
        UpdateModelListForProvider(provider);
    }

    private void UpdateProviderVisibility(string provider)
    {
        var isGemini = provider == "Gemini";

        if (ClaudeApiKeySection != null)
            ClaudeApiKeySection.Visibility = isGemini ? Visibility.Collapsed : Visibility.Visible;
        if (GeminiApiKeySection != null)
            GeminiApiKeySection.Visibility = isGemini ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateModelListForProvider(string provider)
    {
        if (ModelComboBox == null) return;

        ModelComboBox.Items.Clear();

        if (provider == "Gemini")
        {
            ModelComboBox.Items.Add(new ComboBoxItem { Content = "gemini-3-pro-preview" });
            ModelComboBox.Text = _configService.GeminiModel;
            if (ModelDescriptionText != null)
                ModelDescriptionText.Text = "The Gemini model to use. Gemini 3 Pro is recommended.";
        }
        else
        {
            ModelComboBox.Items.Add(new ComboBoxItem { Content = "claude-sonnet-4-5-20250929" });
            ModelComboBox.Items.Add(new ComboBoxItem { Content = "claude-opus-4-5-20251101" });
            ModelComboBox.Items.Add(new ComboBoxItem { Content = "claude-haiku-4-5-20251001" });
            ModelComboBox.Text = _configService.Model;
            if (ModelDescriptionText != null)
                ModelDescriptionText.Text = "The Claude model to use. Sonnet is recommended for most tasks.";
        }
    }

    private string GetSelectedProvider()
    {
        var selectedItem = ProviderComboBox.SelectedItem as ComboBoxItem;
        return selectedItem?.Tag?.ToString() ?? "Claude";
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

        var provider = GetSelectedProvider();

        // Save provider
        _configService.AiProvider = provider;

        // Save Claude API key if changed
        var newClaudeKey = ApiKeyBox.Password;
        if (!string.IsNullOrEmpty(newClaudeKey))
        {
            _configService.ApiKey = newClaudeKey;
        }

        // Save Gemini API key if changed
        var newGeminiKey = GeminiApiKeyBox.Password;
        if (!string.IsNullOrEmpty(newGeminiKey))
        {
            _configService.GeminiApiKey = newGeminiKey;
        }

        // Save model for the selected provider
        if (provider == "Gemini")
        {
            _configService.GeminiModel = model;
        }
        else
        {
            _configService.Model = model;
        }

        // Save shared settings
        _configService.Temperature = TemperatureSlider.Value;
        _configService.MaxTokens = maxTokens;
        _configService.ContextVerbosity = VerbosityComboBox.SelectedIndex;

        // Save safety settings
        _configService.SkipConfirmations = SkipConfirmationsCheckBox.IsChecked == true;
        _configService.DryRunMode = DryRunModeCheckBox.IsChecked == true;

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
        var provider = GetSelectedProvider();
        var apiKey = provider == "Gemini" ? GeminiApiKeyBox.Password : ApiKeyBox.Password;

        if (string.IsNullOrEmpty(apiKey))
        {
            ShowConnectionStatus($"Please enter a {provider} API key first.", isError: true);
            return;
        }

        TestConnectionButton.IsEnabled = false;
        ShowConnectionStatus("Testing connection...", isError: false);

        try
        {
            // Temporarily save the API key for testing
            if (provider == "Gemini")
            {
                var originalKey = _configService.GeminiApiKey;
                _configService.GeminiApiKey = apiKey;

                // Temporarily set provider for the factory
                var originalProvider = _configService.AiProvider;
                _configService.AiProvider = "Gemini";

                try
                {
                    using var aiProvider = AiProviderFactory.Create(_configService);
                    await TestProviderConnection(aiProvider, provider);
                }
                finally
                {
                    _configService.AiProvider = originalProvider;
                }
            }
            else
            {
                var originalKey = _configService.ApiKey;
                _configService.ApiKey = apiKey;

                using var aiProvider = new ClaudeApiService(_configService);
                await TestProviderConnection(aiProvider, provider);

                // Note: key stays saved if test succeeded (same as before)
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

    private async Task TestProviderConnection(IAiProvider aiProvider, string provider)
    {
        var testMessages = new List<ClaudeMessage>
        {
            ClaudeMessage.User("Say 'ok' and nothing else.")
        };

        var defaultModel = provider == "Gemini"
            ? (ModelComboBox.Text ?? "gemini-3-pro-preview")
            : (ModelComboBox.Text ?? "claude-sonnet-4-5-20250929");

        var testSettings = new ApiSettings
        {
            Model = defaultModel,
            MaxTokens = 10,
            Temperature = 0
        };

        var response = await aiProvider.SendMessageAsync(
            systemPrompt: null,
            messages: testMessages,
            tools: null,
            settingsOverride: testSettings);

        if (response != null)
        {
            ShowConnectionStatus($"{provider} connection successful!", isError: false);
        }
        else
        {
            ShowConnectionStatus("Connection failed: No response received.", isError: true);
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

    private void ResetUsageButton_Click(object sender, RoutedEventArgs e)
    {
        _usageTracker.Reset();
        UpdateUsageDisplay();
    }
}
