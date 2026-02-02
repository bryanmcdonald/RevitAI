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

using System.Collections.Concurrent;
using System.Text.Json;
using RevitAI.Models;
using RevitAI.Services;

namespace RevitAI.Tools;

/// <summary>
/// Singleton registry for all available Revit tools.
/// Provides thread-safe tool registration and lookup.
/// </summary>
public sealed class ToolRegistry
{
    private static readonly Lazy<ToolRegistry> _instance = new(() => new ToolRegistry());

    /// <summary>
    /// Gets the singleton instance of the tool registry.
    /// </summary>
    public static ToolRegistry Instance => _instance.Value;

    private readonly ConcurrentDictionary<string, IRevitTool> _tools = new(StringComparer.OrdinalIgnoreCase);

    private ToolRegistry() { }

    /// <summary>
    /// Registers a tool with the registry.
    /// </summary>
    /// <param name="tool">The tool to register.</param>
    /// <exception cref="ArgumentException">Thrown if a tool with the same name is already registered.</exception>
    public void Register(IRevitTool tool)
    {
        if (!_tools.TryAdd(tool.Name, tool))
        {
            throw new ArgumentException($"A tool with name '{tool.Name}' is already registered.", nameof(tool));
        }
    }

    /// <summary>
    /// Gets a tool by name (case-insensitive).
    /// </summary>
    /// <param name="name">The tool name to look up.</param>
    /// <returns>The tool if found, otherwise null.</returns>
    public IRevitTool? Get(string name)
    {
        _tools.TryGetValue(name, out var tool);
        return tool;
    }

    /// <summary>
    /// Gets all registered tools.
    /// </summary>
    /// <returns>An enumerable of all registered tools.</returns>
    public IEnumerable<IRevitTool> GetAll() => _tools.Values;

    /// <summary>
    /// Gets the tool definitions in the format expected by the Claude API.
    /// </summary>
    /// <returns>A list of tool definitions for the API request.</returns>
    public List<ToolDefinition> GetDefinitions()
    {
        return _tools.Values.Select(tool => new ToolDefinition
        {
            Name = tool.Name,
            Description = tool.Description,
            InputSchema = tool.InputSchema
        }).ToList();
    }

    /// <summary>
    /// Gets tool definitions filtered based on current configuration.
    /// Claude only gets the screenshot tool when in Always mode.
    /// </summary>
    /// <returns>A filtered list of tool definitions for the API request.</returns>
    public List<ToolDefinition> GetDefinitionsForRequest()
    {
        var configService = ConfigurationService.Instance;

        // Filter out capture_screenshot unless in Always mode
        // Off = no screenshots, OneTime = user-attached only (Claude can't request)
        var tools = _tools.Values.AsEnumerable();

        if (configService.ScreenshotToolEnabled != ScreenshotToolState.Always)
        {
            tools = tools.Where(t => t.Name != "capture_screenshot");
        }

        return tools.Select(tool => new ToolDefinition
        {
            Name = tool.Name,
            Description = tool.Description,
            InputSchema = tool.InputSchema
        }).ToList();
    }

    /// <summary>
    /// Gets a comma-separated list of all available tool names.
    /// Useful for error messages.
    /// </summary>
    public string GetAvailableToolNames()
    {
        return string.Join(", ", _tools.Keys.OrderBy(k => k));
    }

    /// <summary>
    /// Gets the number of registered tools.
    /// </summary>
    public int Count => _tools.Count;
}
