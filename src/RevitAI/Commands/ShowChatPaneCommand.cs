using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitAI.Commands;

/// <summary>
/// Command to toggle visibility of the RevitAI chat pane.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class ShowChatPaneCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            var paneId = App.ChatPaneId;
            var pane = commandData.Application.GetDockablePane(paneId);

            if (pane != null)
            {
                if (pane.IsShown())
                {
                    pane.Hide();
                }
                else
                {
                    pane.Show();
                }
            }

            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return Result.Failed;
        }
    }
}
