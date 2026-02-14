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
using Autodesk.Revit.DB.Events;
using Autodesk.Revit.UI;
using RevitAI.Services;
using RevitAI.Threading;
using RevitAI.Tools;
using RevitAI.Tools.ModifyTools;
using RevitAI.Tools.ReadTools;
using RevitAI.Tools.ViewTools;
using RevitAI.Tools.DraftingTools;
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

            // Subscribe to document events for conversation memory
            application.ControlledApplication.DocumentOpened += OnDocumentOpened;
            application.ControlledApplication.DocumentClosing += OnDocumentClosing;

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
        // Unsubscribe from document events
        application.ControlledApplication.DocumentOpened -= OnDocumentOpened;
        application.ControlledApplication.DocumentClosing -= OnDocumentClosing;

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
    /// Handles document opened event: loads project-keyed conversation if available.
    /// </summary>
    private void OnDocumentOpened(object? sender, DocumentOpenedEventArgs e)
    {
        var projectKey = ConversationPersistenceService.GetProjectKey(e.Document);
        if (projectKey == null)
            return;

        var viewModel = _chatPane?.ViewModel;
        if (viewModel == null)
            return;

        // Clear change tracker for fresh session
        ChangeTracker.Instance.Clear();

        // Fire-and-forget: load conversation on background thread to avoid blocking Revit
        _ = Task.Run(async () =>
        {
            try
            {
                var restored = await viewModel.LoadProjectConversationAsync(projectKey);

                // Show the chat pane if a conversation was restored
                if (restored)
                {
                    await ExecuteOnRevitThreadAsync(app =>
                    {
                        var pane = app.GetDockablePane(ChatPaneId);
                        if (pane != null && !pane.IsShown())
                            pane.Show();
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"RevitAI: Failed to load conversation for {projectKey}: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Handles document closing event: saves current conversation with project key.
    /// </summary>
    private void OnDocumentClosing(object? sender, DocumentClosingEventArgs e)
    {
        var projectKey = ConversationPersistenceService.GetProjectKey(e.Document);
        if (projectKey == null)
            return;

        _chatPane?.ViewModel?.SaveCurrentConversation(projectKey);
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
        registry.Register(new ResolveGridIntersectionTool());

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

        // Advanced placement tools (P2-01)
        registry.Register(new PlaceLevelTool());
        registry.Register(new PlaceGridTool());
        registry.Register(new PlaceDetailLineTool());
        registry.Register(new PlaceTextNoteTool());
        registry.Register(new CreateSheetTool());
        registry.Register(new PlaceTagTool());
        registry.Register(new PlaceDimensionTool());

        // Element manipulation tools (P2-02)
        registry.Register(new CopyElementTool());
        registry.Register(new MirrorElementTool());
        registry.Register(new RotateElementTool());
        registry.Register(new ArrayElementsTool());
        registry.Register(new AlignElementsTool());
        registry.Register(new CreateGroupTool());
        registry.Register(new CreateAssemblyTool());

        // Parameter & schedule tools (P2-06)
        registry.Register(new ReadScheduleDataTool());
        registry.Register(new ExportElementDataTool());
        registry.Register(new BulkModifyParametersTool());

        // View tools (P1.5)
        registry.Register(new CaptureScreenshotTool());

        // View management tools (P1.5-02)
        registry.Register(new GetViewListTool());
        registry.Register(new SwitchViewTool());
        registry.Register(new OpenViewTool());
        registry.Register(new CreateFloorPlanViewTool());
        registry.Register(new CreateCeilingPlanViewTool());
        registry.Register(new Create3DViewTool());
        registry.Register(new CreateSectionViewTool());
        registry.Register(new CreateElevationViewTool());
        registry.Register(new CreateScheduleViewTool());
        registry.Register(new CreateDraftingViewTool());
        registry.Register(new DuplicateViewTool());
        registry.Register(new RenameViewTool());
        registry.Register(new DeleteViewTool());

        // Camera control tools (P1.5-03)
        registry.Register(new ZoomToFitTool());
        registry.Register(new ZoomToElementsTool());
        registry.Register(new ZoomToBoundsTool());
        registry.Register(new ZoomByPercentTool());
        registry.Register(new PanViewTool());
        registry.Register(new OrbitViewTool());
        registry.Register(new SetViewOrientationTool());

        // Visual isolation tools (P1.5-04)
        registry.Register(new IsolateElementsTool());
        registry.Register(new HideElementsTool());
        registry.Register(new ResetVisibilityTool());
        registry.Register(new Set3DSectionBoxTool());
        registry.Register(new ClearSectionBoxTool());
        registry.Register(new SetDisplayStyleTool());

        // Drafting & documentation tools (P2-08.1) - Discovery
        registry.Register(new GetFillPatternsTool());
        registry.Register(new GetLineStylesTool());
        registry.Register(new GetDetailComponentsTool());
        registry.Register(new GetRevisionListTool());
        registry.Register(new GetSheetListTool());
        registry.Register(new GetViewportInfoTool());

        // Drafting & documentation tools (P2-08.2) - Linework & Shapes
        registry.Register(new PlaceDetailArcTool());
        registry.Register(new PlaceDetailCurveTool());
        registry.Register(new PlaceDetailPolylineTool());
        registry.Register(new PlaceDetailCircleTool());
        registry.Register(new PlaceDetailRectangleTool());
        registry.Register(new PlaceDetailEllipseTool());
        registry.Register(new ModifyDetailCurveStyleTool());

        // Drafting & documentation tools (P2-08.3) - Regions & Components
        registry.Register(new PlaceFilledRegionTool());
        registry.Register(new PlaceMaskingRegionTool());
        registry.Register(new CreateFilledRegionTypeTool());
        registry.Register(new PlaceDetailComponentTool());
        registry.Register(new PlaceDetailGroupTool());

        // Drafting & documentation tools (P2-08.4) - Sheets & Viewports
        registry.Register(new PlaceViewportTool());
        registry.Register(new AutoArrangeViewportsTool());
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
