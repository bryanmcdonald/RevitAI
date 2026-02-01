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

using Autodesk.Revit.UI;

namespace RevitAI.Services;

/// <summary>
/// Detects and tracks Revit's current UI theme (Light/Dark).
/// Provides events for theme changes to update WPF styling.
/// </summary>
public sealed class ThemeService : IDisposable
{
    private static ThemeService? _instance;
    private static readonly object _lock = new();

    private UIControlledApplication? _application;
    private bool _disposed;
    private System.Timers.Timer? _themePollingTimer;
    private UITheme _lastKnownTheme;

    /// <summary>
    /// Gets the singleton instance of the ThemeService.
    /// </summary>
    public static ThemeService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new ThemeService();
                }
            }
            return _instance;
        }
    }

    /// <summary>
    /// Event raised when Revit's theme changes.
    /// </summary>
    public event EventHandler<bool>? ThemeChanged;

    /// <summary>
    /// Gets whether dark theme is currently active.
    /// </summary>
    public bool IsDarkTheme { get; private set; }

    private ThemeService() { }

    /// <summary>
    /// Initializes the theme service with the Revit application.
    /// Must be called during OnStartup.
    /// </summary>
    public void Initialize(UIControlledApplication application)
    {
        _application = application;

        // Detect initial theme
        UpdateThemeState();
        _lastKnownTheme = GetCurrentTheme();

        // Try to subscribe to theme changes (Revit 2024+)
        // Fall back to polling for older versions or if event is not available
        try
        {
            // Note: UIThemeManager.CurrentThemeChanged may not exist in all Revit versions
            // Using polling as a reliable cross-version approach
            StartThemePolling();
        }
        catch
        {
            StartThemePolling();
        }
    }

    private void StartThemePolling()
    {
        // Poll for theme changes every 2 seconds
        _themePollingTimer = new System.Timers.Timer(2000);
        _themePollingTimer.Elapsed += (s, e) => CheckForThemeChange();
        _themePollingTimer.AutoReset = true;
        _themePollingTimer.Start();
    }

    private void CheckForThemeChange()
    {
        try
        {
            var currentTheme = GetCurrentTheme();
            if (currentTheme != _lastKnownTheme)
            {
                _lastKnownTheme = currentTheme;
                var wasDark = IsDarkTheme;
                UpdateThemeState();

                if (wasDark != IsDarkTheme)
                {
                    ThemeChanged?.Invoke(this, IsDarkTheme);
                }
            }
        }
        catch
        {
            // Ignore errors during polling
        }
    }

    private UITheme GetCurrentTheme()
    {
        try
        {
            return UIThemeManager.CurrentTheme;
        }
        catch
        {
            return UITheme.Light;
        }
    }

    private void UpdateThemeState()
    {
        try
        {
            var currentTheme = UIThemeManager.CurrentTheme;
            IsDarkTheme = currentTheme == UITheme.Dark;
        }
        catch
        {
            // Default to light theme if detection fails
            IsDarkTheme = false;
        }
    }

    /// <summary>
    /// Gets the appropriate resource dictionary URI for the current theme.
    /// </summary>
    public Uri GetThemeResourceUri()
    {
        var themeName = IsDarkTheme ? "DarkTheme" : "LightTheme";
        return new Uri($"pack://application:,,,/RevitAI;component/UI/Themes/{themeName}.xaml", UriKind.Absolute);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _themePollingTimer?.Stop();
        _themePollingTimer?.Dispose();

        _disposed = true;
    }
}
