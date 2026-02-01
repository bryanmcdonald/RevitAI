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

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RevitAI.Models;

namespace RevitAI.Services;

/// <summary>
/// Manages persistent configuration for RevitAI.
/// Settings are stored in %APPDATA%\RevitAI\config.json.
/// API key is encrypted using Windows DPAPI.
/// </summary>
public sealed class ConfigurationService
{
    private static ConfigurationService? _instance;
    private static readonly object _lock = new();

    private readonly string _configFilePath;
    private ConfigData _config;

    /// <summary>
    /// Gets the singleton instance.
    /// </summary>
    public static ConfigurationService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new ConfigurationService();
                }
            }
            return _instance;
        }
    }

    /// <summary>
    /// Gets or sets the API key (stored encrypted).
    /// </summary>
    public string? ApiKey
    {
        get => SecureStorage.Decrypt(_config.EncryptedApiKey);
        set
        {
            _config.EncryptedApiKey = SecureStorage.Encrypt(value);
            Save();
        }
    }

    /// <summary>
    /// Gets a value indicating whether an API key is configured.
    /// </summary>
    public bool HasApiKey => !string.IsNullOrEmpty(ApiKey);

    /// <summary>
    /// Gets or sets the Claude model to use.
    /// </summary>
    public string Model
    {
        get => _config.Model;
        set
        {
            if (_config.Model != value)
            {
                _config.Model = value;
                Save();
            }
        }
    }

    /// <summary>
    /// Gets or sets the temperature (0.0 to 1.0).
    /// </summary>
    public double Temperature
    {
        get => _config.Temperature;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_config.Temperature - clamped) > 0.001)
            {
                _config.Temperature = clamped;
                Save();
            }
        }
    }

    /// <summary>
    /// Gets or sets the maximum tokens for responses.
    /// </summary>
    public int MaxTokens
    {
        get => _config.MaxTokens;
        set
        {
            var clamped = Math.Clamp(value, 100, 128000);
            if (_config.MaxTokens != clamped)
            {
                _config.MaxTokens = clamped;
                Save();
            }
        }
    }

    /// <summary>
    /// Gets or sets the context verbosity level (0=minimal, 1=standard, 2=verbose).
    /// </summary>
    public int ContextVerbosity
    {
        get => _config.ContextVerbosity;
        set
        {
            var clamped = Math.Clamp(value, 0, 2);
            if (_config.ContextVerbosity != clamped)
            {
                _config.ContextVerbosity = clamped;
                Save();
            }
        }
    }

    /// <summary>
    /// Gets or sets whether to skip confirmation dialogs for modifications.
    /// </summary>
    public bool SkipConfirmations
    {
        get => _config.SkipConfirmations;
        set
        {
            if (_config.SkipConfirmations != value)
            {
                _config.SkipConfirmations = value;
                Save();
            }
        }
    }

    /// <summary>
    /// Gets or sets whether dry run mode is enabled (tools report what they would do without modifying).
    /// </summary>
    public bool DryRunMode
    {
        get => _config.DryRunMode;
        set
        {
            if (_config.DryRunMode != value)
            {
                _config.DryRunMode = value;
                Save();
            }
        }
    }

    /// <summary>
    /// Gets the default API settings based on current configuration.
    /// </summary>
    public ApiSettings DefaultApiSettings => new()
    {
        Model = Model,
        Temperature = Temperature,
        MaxTokens = MaxTokens
    };

    private ConfigurationService()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var revitAiFolder = Path.Combine(appDataPath, "RevitAI");
        Directory.CreateDirectory(revitAiFolder);

        _configFilePath = Path.Combine(revitAiFolder, "config.json");
        _config = Load();
    }

    /// <summary>
    /// Resets all settings to defaults (except API key).
    /// </summary>
    public void ResetToDefaults()
    {
        var apiKey = _config.EncryptedApiKey;
        _config = new ConfigData { EncryptedApiKey = apiKey };
        Save();
    }

    private ConfigData Load()
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                var json = File.ReadAllText(_configFilePath);
                return JsonSerializer.Deserialize<ConfigData>(json) ?? new ConfigData();
            }
        }
        catch (Exception)
        {
            // If loading fails, start with defaults
        }

        return new ConfigData();
    }

    private void Save()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_config, options);
            File.WriteAllText(_configFilePath, json);
        }
        catch (Exception)
        {
            // Silently fail if saving fails (e.g., permissions issue)
        }
    }

    /// <summary>
    /// Internal data class for JSON serialization.
    /// </summary>
    private sealed class ConfigData
    {
        [JsonPropertyName("encryptedApiKey")]
        public string? EncryptedApiKey { get; set; }

        [JsonPropertyName("model")]
        public string Model { get; set; } = "claude-sonnet-4-5-20250929";

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; } = 0.7;

        [JsonPropertyName("maxTokens")]
        public int MaxTokens { get; set; } = 4096;

        [JsonPropertyName("contextVerbosity")]
        public int ContextVerbosity { get; set; } = 1;

        [JsonPropertyName("skipConfirmations")]
        public bool SkipConfirmations { get; set; }

        [JsonPropertyName("dryRunMode")]
        public bool DryRunMode { get; set; }
    }
}
