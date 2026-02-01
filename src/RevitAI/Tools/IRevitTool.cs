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
