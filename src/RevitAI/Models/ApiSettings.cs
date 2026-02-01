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

namespace RevitAI.Models;

/// <summary>
/// Immutable settings for a single Claude API request.
/// Use with expressions to create modified copies for per-request overrides.
/// </summary>
/// <example>
/// var customSettings = ApiSettings.Default with { Temperature = 0.9 };
/// </example>
public sealed record ApiSettings
{
    /// <summary>
    /// The Claude model to use for the request.
    /// </summary>
    public string Model { get; init; } = "claude-sonnet-4-5-20250929";

    /// <summary>
    /// Temperature controls randomness. Lower values (0.0-0.3) are more deterministic,
    /// higher values (0.7-1.0) are more creative.
    /// </summary>
    public double Temperature { get; init; } = 0.7;

    /// <summary>
    /// Maximum number of tokens to generate in the response.
    /// </summary>
    public int MaxTokens { get; init; } = 4096;

    /// <summary>
    /// Gets the default API settings.
    /// </summary>
    public static ApiSettings Default => new();
}
