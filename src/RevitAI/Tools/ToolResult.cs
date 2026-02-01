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

namespace RevitAI.Tools;

/// <summary>
/// Represents the result of a tool execution.
/// Immutable wrapper with success/error states.
/// </summary>
public sealed class ToolResult
{
    /// <summary>
    /// Gets whether the tool execution was successful.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Gets whether the tool execution resulted in an error.
    /// </summary>
    public bool IsError => !Success;

    /// <summary>
    /// Gets the result content or error message.
    /// </summary>
    public string Content { get; }

    private ToolResult(bool success, string content)
    {
        Success = success;
        Content = content;
    }

    /// <summary>
    /// Creates a successful result with the given content.
    /// </summary>
    /// <param name="content">The result content.</param>
    public static ToolResult Ok(string content) => new(true, content);

    /// <summary>
    /// Creates an error result with the given message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public static ToolResult Error(string message) => new(false, message);

    /// <summary>
    /// Creates an error result from an exception.
    /// </summary>
    /// <param name="exception">The exception that occurred.</param>
    public static ToolResult FromException(Exception exception)
    {
        var message = exception switch
        {
            OperationCanceledException => "Tool execution was cancelled.",
            InvalidOperationException ex => ex.Message,
            _ => $"Tool execution failed: {exception.Message}"
        };
        return new ToolResult(false, message);
    }
}
