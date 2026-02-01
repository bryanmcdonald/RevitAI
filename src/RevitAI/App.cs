using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitAI.Services;
using RevitAI.UI;

namespace RevitAI;

/// <summary>
/// Main entry point for the RevitAI plugin.
/// Implements IExternalApplication to integrate with Revit's startup/shutdown lifecycle.
/// </summary>
public class App : IExternalApplication
{
    /// <summary>
    /// Unique identifier for the chat dockable pane.
    /// </summary>
    public static readonly DockablePaneId ChatPaneId = new(new Guid("8A9B0C1D-2E3F-4A5B-6C7D-8E9F0A1B2C3D"));

    /// <summary>
    /// Reference to the chat pane instance.
    /// </summary>
    private static ChatPane? _chatPane;

    /// <summary>
    /// Gets the chat pane instance.
    /// </summary>
    public static ChatPane? ChatPane => _chatPane;

    public Result OnStartup(UIControlledApplication application)
    {
        try
        {
            // Initialize the theme service
            ThemeService.Instance.Initialize(application);

            // Register the dockable chat pane
            RegisterDockablePane(application);

            // Create the ribbon UI
            CreateRibbonUI(application);

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            TaskDialog.Show("RevitAI Error", $"Failed to initialize RevitAI: {ex.Message}");
            return Result.Failed;
        }
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        // Dispose the theme service
        ThemeService.Instance.Dispose();

        return Result.Succeeded;
    }

    /// <summary>
    /// Registers the dockable chat pane with Revit.
    /// </summary>
    private void RegisterDockablePane(UIControlledApplication application)
    {
        _chatPane = new ChatPane();
        application.RegisterDockablePane(ChatPaneId, "RevitAI Chat", _chatPane);
    }

    /// <summary>
    /// Creates the RevitAI ribbon tab and panel.
    /// </summary>
    private void CreateRibbonUI(UIControlledApplication application)
    {
        // Create a new ribbon tab
        const string tabName = "RevitAI";
        application.CreateRibbonTab(tabName);

        // Create a ribbon panel
        var panel = application.CreateRibbonPanel(tabName, "Chat");

        // Get the assembly path for command references
        var assemblyPath = Assembly.GetExecutingAssembly().Location;

        // Create the Show Chat button
        var showChatButtonData = new PushButtonData(
            "ShowChatPane",
            "Show\nChat",
            assemblyPath,
            "RevitAI.Commands.ShowChatPaneCommand")
        {
            ToolTip = "Toggle the RevitAI chat pane",
            LongDescription = "Opens or closes the RevitAI chat pane where you can interact with the AI assistant.",
        };

        // Try to load button images
        try
        {
            var largeImage = LoadEmbeddedImage("RevitAI.Resources.ChatIcon32.png");
            var smallImage = LoadEmbeddedImage("RevitAI.Resources.ChatIcon16.png");

            if (largeImage != null)
                showChatButtonData.LargeImage = largeImage;
            if (smallImage != null)
                showChatButtonData.Image = smallImage;
        }
        catch
        {
            // Images not found - button will display without icons
        }

        panel.AddItem(showChatButtonData);
    }

    /// <summary>
    /// Loads an embedded resource image as a BitmapImage.
    /// </summary>
    private static BitmapImage? LoadEmbeddedImage(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName);

        if (stream == null)
            return null;

        var image = new BitmapImage();
        image.BeginInit();
        image.StreamSource = stream;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();

        return image;
    }
}
