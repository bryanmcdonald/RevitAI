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

namespace RevitAI.Services;

/// <summary>
/// Factory for creating AI provider instances based on configuration.
/// </summary>
public static class AiProviderFactory
{
    /// <summary>
    /// Creates an AI provider based on the current configuration.
    /// </summary>
    /// <param name="configService">The configuration service.</param>
    /// <returns>An IAiProvider instance for the configured provider.</returns>
    public static IAiProvider Create(ConfigurationService configService)
    {
        return configService.AiProvider switch
        {
            "Gemini" => new GeminiApiService(configService),
            _ => new ClaudeApiService(configService)
        };
    }
}
