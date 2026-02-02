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
/// Screenshot resolution presets with corresponding pixel widths.
/// </summary>
public enum ScreenshotResolution
{
    /// <summary>800px width - Quick overview, layout verification (~500-700 tokens).</summary>
    Low,

    /// <summary>1280px width - General analysis, element identification (~800-1000 tokens).</summary>
    Medium,

    /// <summary>1920px width - Detail work, reading small text (~1500-2000 tokens).</summary>
    High,

    /// <summary>2560px width - Fine detail, dimension reading (~2500-3500 tokens).</summary>
    Max
}

/// <summary>
/// Controls how screenshots are captured and when Claude can request them.
/// </summary>
public enum ScreenshotToolState
{
    /// <summary>No screenshots. Claude cannot use capture_screenshot tool.</summary>
    Off,

    /// <summary>Auto-attach screenshot to each user message. Claude cannot request screenshots.</summary>
    OneTime,

    /// <summary>Claude can call capture_screenshot tool whenever it needs visual context.</summary>
    Always
}

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
