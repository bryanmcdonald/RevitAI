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

using RevitAI.Models;

namespace RevitAI.Services;

/// <summary>
/// Abstraction for AI provider services (Claude, Gemini, etc.).
/// All providers translate to/from the canonical internal types
/// (ClaudeMessage, StreamEvent, ToolDefinition, etc.).
/// </summary>
public interface IAiProvider : IDisposable
{
    /// <summary>
    /// Gets the display name of this provider (e.g., "Claude", "Gemini").
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Event raised when a streaming response completes, providing usage statistics.
    /// </summary>
    event EventHandler<Usage>? StreamCompleted;

    /// <summary>
    /// Sends a message and returns the complete response.
    /// </summary>
    Task<ClaudeResponse> SendMessageAsync(
        string? systemPrompt,
        List<ClaudeMessage> messages,
        List<ToolDefinition>? tools = null,
        ApiSettings? settingsOverride = null,
        CancellationToken ct = default);

    /// <summary>
    /// Sends a message and streams the response.
    /// </summary>
    IAsyncEnumerable<StreamEvent> SendMessageStreamingAsync(
        string? systemPrompt,
        List<ClaudeMessage> messages,
        List<ToolDefinition>? tools = null,
        ApiSettings? settingsOverride = null,
        CancellationToken ct = default);

    /// <summary>
    /// Cancels the current request if one is in progress.
    /// </summary>
    void CancelCurrentRequest();
}
