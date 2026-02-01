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
