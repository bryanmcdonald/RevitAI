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

    public SettingsDialog()
    {
        InitializeComponent();
        _configService = ConfigurationService.Instance;
        _usageTracker = UsageTracker.Instance;

        // Subscribe to usage tracker changes
        _usageTracker.PropertyChanged += UsageTracker_PropertyChanged;

        LoadSettings();
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

        // Load safety settings
        SkipConfirmationsCheckBox.IsChecked = _configService.SkipConfirmations;
        DryRunModeCheckBox.IsChecked = _configService.DryRunMode;
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

    private void ResetUsageButton_Click(object sender, RoutedEventArgs e)
    {
        _usageTracker.Reset();
        UpdateUsageDisplay();
    }
}
