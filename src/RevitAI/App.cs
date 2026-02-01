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

using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitAI.Services;
using RevitAI.Threading;
using RevitAI.Tools;
using RevitAI.Tools.ModifyTools;
using RevitAI.Tools.ReadTools;
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

    /// <summary>
    /// The ExternalEvent used to marshal commands to the Revit main thread.
    /// </summary>
    public static ExternalEvent? RevitEvent { get; private set; }

    /// <summary>
    /// The command queue for pending Revit thread operations.
    /// </summary>
    public static CommandQueue? CommandQueue { get; private set; }

    public Result OnStartup(UIControlledApplication application)
    {
        try
        {
            // Initialize the threading infrastructure
            CommandQueue = new CommandQueue();
            var handler = new RevitEventHandler(CommandQueue);
            RevitEvent = ExternalEvent.Create(handler);

            // Initialize configuration service (loads settings from disk)
            _ = ConfigurationService.Instance;

            // Initialize the theme service
            ThemeService.Instance.Initialize(application);

            // Register available tools
            RegisterTools();

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
        // Cancel any pending commands and dispose the ExternalEvent
        CommandQueue?.CancelAll();
        RevitEvent?.Dispose();
        RevitEvent = null;
        CommandQueue = null;

        // Dispose the theme service
        ThemeService.Instance.Dispose();

        return Result.Succeeded;
    }

    /// <summary>
    /// Executes a function on the Revit main thread and returns the result.
    /// Call from background threads to safely access Revit API.
    /// </summary>
    /// <typeparam name="T">The return type of the function.</typeparam>
    /// <param name="func">The function to execute.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The result of the function.</returns>
    /// <exception cref="InvalidOperationException">Thrown if threading infrastructure is not initialized.</exception>
    public static async Task<T> ExecuteOnRevitThreadAsync<T>(
        Func<UIApplication, T> func,
        CancellationToken cancellationToken = default)
    {
        if (CommandQueue == null || RevitEvent == null)
        {
            throw new InvalidOperationException("RevitAI threading infrastructure is not initialized.");
        }

        var command = new FuncCommand<T>(func, cancellationToken);
        CommandQueue.Enqueue(command);

        var result = RevitEvent.Raise();
        // Accepted = event will fire, Pending = event already raised and will fire soon
        // Both are valid - command is queued and will execute
        if (result == ExternalEventRequest.Denied || result == ExternalEventRequest.TimedOut)
        {
            throw new InvalidOperationException(
                $"Failed to raise ExternalEvent. Request result: {result}. " +
                "This may occur if a modal dialog is open or Revit is busy.");
        }

        return await command.Task;
    }

    /// <summary>
    /// Executes an action on the Revit main thread.
    /// Call from background threads to safely access Revit API.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown if threading infrastructure is not initialized.</exception>
    public static async Task ExecuteOnRevitThreadAsync(
        Action<UIApplication> action,
        CancellationToken cancellationToken = default)
    {
        if (CommandQueue == null || RevitEvent == null)
        {
            throw new InvalidOperationException("RevitAI threading infrastructure is not initialized.");
        }

        var command = new ActionCommand(action, cancellationToken);
        CommandQueue.Enqueue(command);

        var result = RevitEvent.Raise();
        // Accepted = event will fire, Pending = event already raised and will fire soon
        // Both are valid - command is queued and will execute
        if (result == ExternalEventRequest.Denied || result == ExternalEventRequest.TimedOut)
        {
            throw new InvalidOperationException(
                $"Failed to raise ExternalEvent. Request result: {result}. " +
                "This may occur if a modal dialog is open or Revit is busy.");
        }

        await command.Task;
    }

    /// <summary>
    /// Registers all available tools with the tool registry.
    /// </summary>
    private static void RegisterTools()
    {
        var registry = ToolRegistry.Instance;

        // Test tool
        registry.Register(new EchoTool());

        // Read-only tools (P1-07)
        registry.Register(new GetLevelsTool());
        registry.Register(new GetGridsTool());
        registry.Register(new GetProjectInfoTool());
        registry.Register(new GetViewInfoTool());
        registry.Register(new GetSelectedElementsTool());
        registry.Register(new GetWarningsTool());
        registry.Register(new GetAvailableTypesTool());
        registry.Register(new GetElementsByCategoryTool());
        registry.Register(new GetElementPropertiesTool());
        registry.Register(new GetRoomInfoTool());
        registry.Register(new GetElementQuantityTakeoffTool());

        // Modification tools (P1-09) - Non-transaction
        registry.Register(new SelectElementsTool());
        registry.Register(new ZoomToElementTool());

        // Modification tools (P1-09) - Transaction-required
        registry.Register(new MoveElementTool());
        registry.Register(new DeleteElementsTool());
        registry.Register(new ModifyElementParameterTool());
        registry.Register(new ChangeElementTypeTool());
        registry.Register(new PlaceWallTool());
        registry.Register(new PlaceColumnTool());
        registry.Register(new PlaceBeamTool());
        registry.Register(new PlaceFloorTool());
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

#if DEBUG
        // Add Test Threading button in DEBUG builds only
        panel.AddSeparator();

        var testThreadingButtonData = new PushButtonData(
            "TestThreading",
            "Test\nThreading",
            assemblyPath,
            "RevitAI.Commands.TestThreadingCommand")
        {
            ToolTip = "Test threading infrastructure",
            LongDescription = "Runs tests to verify the ExternalEvent threading infrastructure is working correctly.",
        };

        panel.AddItem(testThreadingButtonData);

        // Add Test Transactions button in DEBUG builds only
        var testTransactionButtonData = new PushButtonData(
            "TestTransactions",
            "Test\nTransactions",
            assemblyPath,
            "RevitAI.Commands.TestTransactionCommand")
        {
            ToolTip = "Test transaction infrastructure",
            LongDescription = "Runs tests to verify the TransactionManager is working correctly.",
        };

        panel.AddItem(testTransactionButtonData);
#endif
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
