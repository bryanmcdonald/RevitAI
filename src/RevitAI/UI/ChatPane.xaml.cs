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

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Autodesk.Revit.UI;
using RevitAI.Services;

namespace RevitAI.UI;

/// <summary>
/// Code-behind for the ChatPane WPF Page.
/// Implements IDockablePaneProvider for Revit integration.
/// </summary>
public partial class ChatPane : Page, IDockablePaneProvider
{
    private readonly ChatViewModel _viewModel;
    private ResourceDictionary? _currentThemeDictionary;

    public ChatPane()
    {
        InitializeComponent();

        _viewModel = new ChatViewModel();
        DataContext = _viewModel;

        // Subscribe to theme changes
        ThemeService.Instance.ThemeChanged += OnThemeChanged;

        // Apply initial theme
        ApplyTheme(ThemeService.Instance.IsDarkTheme);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>
    /// Sets up the dockable pane with default settings.
    /// Called by Revit when the pane is registered.
    /// </summary>
    public void SetupDockablePane(DockablePaneProviderData data)
    {
        data.FrameworkElement = this;
        data.InitialState = new DockablePaneState
        {
            DockPosition = DockPosition.Right,
            MinimumWidth = 350,
            MinimumHeight = 400
        };
        data.VisibleByDefault = false;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Focus the input textbox when the pane is shown
        InputTextBox.Focus();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ThemeService.Instance.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, bool isDarkTheme)
    {
        Dispatcher.Invoke(() => ApplyTheme(isDarkTheme));
    }

    private void ApplyTheme(bool isDarkTheme)
    {
        // Remove current theme dictionary if exists
        if (_currentThemeDictionary != null && Resources.MergedDictionaries.Contains(_currentThemeDictionary))
        {
            Resources.MergedDictionaries.Remove(_currentThemeDictionary);
        }

        // Load and apply new theme
        var themeUri = isDarkTheme
            ? new Uri("/RevitAI;component/UI/Themes/DarkTheme.xaml", UriKind.Relative)
            : new Uri("/RevitAI;component/UI/Themes/LightTheme.xaml", UriKind.Relative);

        try
        {
            _currentThemeDictionary = new ResourceDictionary { Source = themeUri };
            Resources.MergedDictionaries.Add(_currentThemeDictionary);
        }
        catch
        {
            // Fallback - theme resource not found
        }
    }

    private void InputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        _viewModel.HandleKeyDown(e);
    }

    /// <summary>
    /// Gets the ViewModel for external access (e.g., from commands).
    /// </summary>
    public ChatViewModel ViewModel => _viewModel;
}
