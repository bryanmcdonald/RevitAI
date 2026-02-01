using System.Text.Json;
using Autodesk.Revit.UI;

namespace RevitAI.Tools;

/// <summary>
/// Interface for tools that Claude can invoke to interact with Revit.
/// </summary>
public interface IRevitTool
{
    /// <summary>
    /// Gets the unique name of the tool (snake_case identifier).
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the description of what the tool does (for Claude's understanding).
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Gets the JSON Schema that defines the expected input parameters.
    /// </summary>
    JsonElement InputSchema { get; }

    /// <summary>
    /// Gets whether this tool requires a Revit transaction to execute.
    /// Tools that modify the model should return true.
    /// </summary>
    bool RequiresTransaction { get; }

    /// <summary>
    /// Executes the tool with the given input parameters.
    /// </summary>
    /// <param name="input">The input parameters as a JSON element.</param>
    /// <param name="app">The Revit UIApplication for API access.</param>
    /// <param name="cancellationToken">Token for cancellation.</param>
    /// <returns>The result of the tool execution.</returns>
    Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken);
}
