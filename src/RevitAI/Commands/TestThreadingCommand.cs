using System.Diagnostics;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitAI.Commands;

/// <summary>
/// Test command for verifying the ExternalEvent threading infrastructure.
/// Runs 5 tests to validate command execution, cancellation, and exception handling.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class TestThreadingCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        // Run tests asynchronously and show results
        _ = RunTestsAsync(commandData.Application);
        return Result.Succeeded;
    }

    private static async Task RunTestsAsync(UIApplication uiApp)
    {
        var results = new StringBuilder();
        results.AppendLine("Threading Infrastructure Tests");
        results.AppendLine("==============================");
        results.AppendLine();

        var allPassed = true;

        // Test 1: FuncCommand - Get document title from background thread
        try
        {
            results.Append("Test 1: FuncCommand (get document title)... ");
            var title = await App.ExecuteOnRevitThreadAsync(app =>
            {
                var doc = app.ActiveUIDocument?.Document;
                return doc?.Title ?? "No document open";
            });
            results.AppendLine($"PASSED - Got: \"{title}\"");
        }
        catch (Exception ex)
        {
            results.AppendLine($"FAILED - {ex.Message}");
            allPassed = false;
        }

        // Test 2: ActionCommand - Execute void action
        try
        {
            results.Append("Test 2: ActionCommand (void action)... ");
            var actionExecuted = false;
            await App.ExecuteOnRevitThreadAsync(app =>
            {
                // Simple action that sets a flag
                actionExecuted = true;
                Debug.WriteLine("ActionCommand executed on Revit thread");
            });

            if (actionExecuted)
            {
                results.AppendLine("PASSED");
            }
            else
            {
                results.AppendLine("FAILED - Action was not executed");
                allPassed = false;
            }
        }
        catch (Exception ex)
        {
            results.AppendLine($"FAILED - {ex.Message}");
            allPassed = false;
        }

        // Test 3: Cancellation - Verify cancelled commands don't execute
        try
        {
            results.Append("Test 3: Cancellation support... ");
            var cts = new CancellationTokenSource();
            cts.Cancel(); // Cancel immediately

            var commandExecuted = false;
            try
            {
                await App.ExecuteOnRevitThreadAsync(app =>
                {
                    commandExecuted = true;
                    return "Should not get here";
                }, cts.Token);

                results.AppendLine("FAILED - Command should have been cancelled");
                allPassed = false;
            }
            catch (TaskCanceledException)
            {
                if (!commandExecuted)
                {
                    results.AppendLine("PASSED - Command was cancelled before execution");
                }
                else
                {
                    results.AppendLine("FAILED - Command executed despite cancellation");
                    allPassed = false;
                }
            }
        }
        catch (Exception ex)
        {
            results.AppendLine($"FAILED - {ex.Message}");
            allPassed = false;
        }

        // Test 4: Exception handling - Verify exceptions propagate
        try
        {
            results.Append("Test 4: Exception propagation... ");
            try
            {
                await App.ExecuteOnRevitThreadAsync<string>(app =>
                {
                    throw new InvalidOperationException("Test exception");
                });

                results.AppendLine("FAILED - Exception should have been thrown");
                allPassed = false;
            }
            catch (InvalidOperationException ex) when (ex.Message == "Test exception")
            {
                results.AppendLine("PASSED - Exception propagated correctly");
            }
        }
        catch (Exception ex)
        {
            results.AppendLine($"FAILED - Wrong exception type: {ex.GetType().Name}");
            allPassed = false;
        }

        // Test 5: Multiple commands - Verify queue processes correctly
        try
        {
            results.Append("Test 5: Multiple commands in sequence... ");

            // Queue multiple commands and verify they all complete
            var task1 = App.ExecuteOnRevitThreadAsync(app => 1);
            var task2 = App.ExecuteOnRevitThreadAsync(app => 2);
            var task3 = App.ExecuteOnRevitThreadAsync(app => 3);

            var results123 = await Task.WhenAll(task1, task2, task3);

            if (results123[0] == 1 && results123[1] == 2 && results123[2] == 3)
            {
                results.AppendLine("PASSED - All commands executed correctly");
            }
            else
            {
                results.AppendLine($"FAILED - Got [{string.Join(", ", results123)}] instead of [1, 2, 3]");
                allPassed = false;
            }
        }
        catch (Exception ex)
        {
            results.AppendLine($"FAILED - {ex.Message}");
            allPassed = false;
        }

        results.AppendLine();
        results.AppendLine("==============================");
        results.AppendLine(allPassed ? "All tests PASSED!" : "Some tests FAILED!");

        // Show results in a TaskDialog (must be on Revit thread)
        await App.ExecuteOnRevitThreadAsync(app =>
        {
            TaskDialog.Show("RevitAI Threading Tests", results.ToString());
        });
    }
}
