using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitAI;

/// <summary>
/// Main entry point for the RevitAI plugin.
/// Implements IExternalApplication to integrate with Revit's startup/shutdown lifecycle.
/// </summary>
public class App : IExternalApplication
{
    public Result OnStartup(UIControlledApplication application)
    {
        TaskDialog.Show("RevitAI", "RevitAI plugin loaded successfully!");
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        return Result.Succeeded;
    }
}
