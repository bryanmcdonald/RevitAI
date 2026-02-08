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

using System.IO;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitAI.Models;

namespace RevitAI.Services;

/// <summary>
/// Gathers Revit model context and builds system prompts for Claude API calls.
/// Context is gathered fresh before each API request to ensure accuracy.
/// </summary>
public sealed class ContextEngine
{
    /// <summary>
    /// Maximum number of elements to include detailed info for.
    /// </summary>
    private const int MaxDetailedElements = 20;

    /// <summary>
    /// Maximum number of parameters to include per element.
    /// Set high to avoid missing important data - token usage is acceptable for modern models.
    /// </summary>
    private const int MaxParametersPerElement = 200;

    /// <summary>
    /// Maximum number of types to list per category.
    /// </summary>
    private const int MaxTypesPerCategory = 20;

    /// <summary>
    /// Categories to gather available types for.
    /// </summary>
    private static readonly BuiltInCategory[] TypeCategories =
    {
        BuiltInCategory.OST_Walls,
        BuiltInCategory.OST_Doors,
        BuiltInCategory.OST_Windows,
        BuiltInCategory.OST_StructuralColumns,
        BuiltInCategory.OST_StructuralFraming,
        BuiltInCategory.OST_Floors,
        BuiltInCategory.OST_Roofs,
        BuiltInCategory.OST_Ceilings,
        BuiltInCategory.OST_GenericModel
    };

    /// <summary>
    /// Internal parameter names to skip when extracting parameters.
    /// </summary>
    private static readonly HashSet<string> InternalParameterNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ELEM_FAMILY_PARAM",
        "ELEM_TYPE_PARAM",
        "SYMBOL_ID_PARAM",
        "ELEM_FAMILY_AND_TYPE_PARAM"
    };

    /// <summary>
    /// Gathers context from the current Revit state.
    /// Must be called on the Revit main thread.
    /// </summary>
    /// <param name="app">The UIApplication instance.</param>
    /// <param name="verbosity">Context verbosity level (0=minimal, 1=standard, 2=detailed).</param>
    /// <returns>The gathered context.</returns>
    public RevitContext GatherContext(UIApplication app, int verbosity)
    {
        var context = new RevitContext();

        try
        {
            var uiDoc = app.ActiveUIDocument;
            var doc = uiDoc?.Document;

            if (doc == null)
            {
                context.HasActiveDocument = false;
                context.ErrorMessage = "No active document is open.";
                return context;
            }

            context.HasActiveDocument = true;

            // Always gather view info
            context.ActiveView = ExtractViewInfo(uiDoc);

            // Always gather active level
            context.ActiveLevel = ExtractActiveLevel(doc, uiDoc.ActiveView);

            // Always gather selected elements (IDs are needed for tool use at all verbosity levels)
            context.SelectedElements = ExtractSelectedElements(uiDoc, doc, verbosity);

            // Verbose mode: add project info, available types, and grid layout
            if (verbosity >= 2)
            {
                context.Project = ExtractRevitProjectInfo(doc);
                context.AvailableTypes = ExtractAvailableTypes(doc);
                context.GridInfo = ExtractGridInfo(doc);
            }
        }
        catch (Exception ex)
        {
            context.ErrorMessage = $"Error gathering context: {ex.Message}";
        }

        return context;
    }

    /// <summary>
    /// Builds a system prompt from the gathered context.
    /// </summary>
    /// <param name="context">The gathered Revit context.</param>
    /// <param name="verbosity">Context verbosity level (0=minimal, 1=standard, 2=detailed).</param>
    /// <returns>The system prompt string.</returns>
    public string BuildSystemPrompt(RevitContext context, int verbosity)
    {
        var sb = new StringBuilder();

        // Base system prompt
        sb.AppendLine("You are RevitAI, an AI assistant embedded in Autodesk Revit. You help users with their Revit models through natural language conversation.");
        sb.AppendLine();
        sb.AppendLine("Your capabilities include:");
        sb.AppendLine("- Answering questions about Revit and BIM workflows");
        sb.AppendLine("- Helping users understand their model structure");
        sb.AppendLine("- Providing guidance on Revit best practices");
        sb.AppendLine("- Querying and analyzing model elements");
        sb.AppendLine();
        sb.AppendLine("Be concise and helpful. When discussing Revit elements, use correct terminology and reference element IDs when relevant.");
        sb.AppendLine();
        sb.AppendLine("**IMPORTANT - Do NOT output your reasoning process.** When you need to use tools, call them directly without narrating your plan or thought process. Do not output text like 'Let me try...' or 'I will now...' before tool calls. Just call the tools, then present the results to the user concisely.");
        sb.AppendLine();
        sb.AppendLine("**CRITICAL - Selection Context:** The 'Current Revit Context' section below is refreshed with EVERY message. When the user says 'this element', 'the selection', 'selected elements', or similar references, ALWAYS use the element IDs from the CURRENT context below - NOT element IDs from earlier in the conversation. The user may have changed their selection between messages. Use these current IDs directly with tools like move_element, delete_elements, modify_element_parameter, etc.");
        sb.AppendLine();
        sb.AppendLine("**Tool usage notes:**");
        sb.AppendLine("- move_element: Moves ONE element at a time. For multiple elements, call it once per element.");
        sb.AppendLine("- Units are in feet. Convert if user provides inches (divide by 12) or meters (multiply by 3.28084).");
        sb.AppendLine("- Directions: +X is typically East, +Y is typically North, +Z is Up. 'South' means -Y, 'West' means -X.");
        sb.AppendLine();
        sb.AppendLine("**View & Camera Control:**");
        sb.AppendLine("- If you need to see something from a different angle, use the camera tools (zoom, pan, orbit, set_view_orientation).");
        sb.AppendLine("- Before working on elements, consider using zoom_to_elements to get a better view of them.");
        sb.AppendLine("- In 3D views, use orbit_view or set_view_orientation to see the model from the optimal angle for the task.");
        sb.AppendLine("- After placing or modifying elements, consider zooming to them to verify the result visually.");
        sb.AppendLine("- Use zoom_to_fit to reset the view if you've zoomed in too far.");
        sb.AppendLine();
        sb.AppendLine("**Element Manipulation:**");
        sb.AppendLine("- copy_element: Copies elements with a translation offset. Returns new element IDs.");
        sb.AppendLine("- mirror_element: Mirrors about a vertical plane defined by two [x,y] points. Default creates copies; set copy=false to move in place.");
        sb.AppendLine("- rotate_element: Rotates around a center point. Positive angle = counterclockwise from above. If no center given, uses bounding box center.");
        sb.AppendLine("- array_elements: Creates linear (spacing vector) or radial (center point + angle) arrays. Count is number of additional copies.");
        sb.AppendLine("- align_elements: Aligns elements to a reference element by bounding box edge (left/right/top/bottom) or center.");
        sb.AppendLine("- create_group: Groups elements into a Model Group (min 2 elements). Optionally name the group type.");
        sb.AppendLine("- create_assembly: Creates an Assembly from elements. Auto-detects naming category from most common element category.");
        sb.AppendLine();
        sb.AppendLine("**Wall type changes:** Revit keeps the wall's 'Location Line' fixed during type changes. The change_element_type tool returns:");
        sb.AppendLine("- location_line_setting: Which reference is fixed ('Finish Face: Exterior', 'Finish Face: Interior', 'Wall Centerline', etc.)");
        sb.AppendLine("- If location_line_setting matches the face the user wants to keep fixed, NO MOVE is needed.");
        sb.AppendLine("- If the user wants a DIFFERENT face fixed, calculate move based on width_difference and perpendicular_to_exterior direction.");
        sb.AppendLine("- Example: Location line is 'Finish Face: Exterior' but user wants interior fixed -> move by full width_difference toward exterior.");
        sb.AppendLine();
        sb.AppendLine("**CRITICAL - Tool Use Budget:** You have a MAXIMUM of 10 tool calls per response. You MUST plan your tool usage strategically:");
        sb.AppendLine("- Before calling ANY tools, mentally plan the optimal sequence of calls needed to fulfill the request.");
        sb.AppendLine("- Prioritize: gather the information you need first, then act. Don't waste calls on speculative or exploratory queries.");
        sb.AppendLine("- Combine related work efficiently (e.g., if you need info on 5 elements, use get_elements_by_category once rather than get_element_properties 5 times).");
        sb.AppendLine("- If a task will clearly require more than 10 tool calls, break it into phases: complete what you can, explain your progress, and tell the user you'll continue in the next response.");
        sb.AppendLine("- Reserve at least 1-2 tool calls for verification (e.g., screenshots or re-querying to confirm results) when doing modifications.");
        sb.AppendLine();
        sb.AppendLine("**Ambiguity - Ask Before Acting:** If the user's request has ANY ambiguity that could lead to incorrect or unintended results, you MUST ask a clarifying question BEFORE using tools. Getting it right the first time is far more valuable than acting fast and getting it wrong. Examples of when to ask:");
        sb.AppendLine("- **Scope ambiguity:** 'Do a beam takeoff for this plan view' - If the plan shows multiple floor levels, should the takeoff include ALL visible beams or only those on the plan's primary level?");
        sb.AppendLine("- **Type/size ambiguity:** 'Add columns here' - Which column family/type? What size?");
        sb.AppendLine("- **Parameter ambiguity:** 'Make it bigger' - Which dimension should change? By how much?");
        sb.AppendLine("- **Selection ambiguity:** 'Delete these walls' when walls span multiple levels - All segments, or just the visible portion?");
        sb.AppendLine("- **Intent ambiguity:** 'Clean up the framing' - Does this mean realign to grids, fix connections, remove duplicates, or something else?");
        sb.AppendLine("One good clarifying question upfront saves the user from having to undo incorrect work. However, do NOT over-ask when intent is clear and reasonable defaults exist - in those cases, proceed and state your assumptions.");
        sb.AppendLine();
        sb.AppendLine("**Smart Placement:**");
        sb.AppendLine("- Placement tools (place_column, place_beam, place_wall) accept `grid_intersection` to place at grid crossings (e.g., {\"grid1\": \"A\", \"grid2\": \"1\"}).");
        sb.AppendLine("- Use `relative_to` to place relative to existing elements (e.g., {\"element_id\": 12345, \"direction\": \"east\", \"distance\": 3.0}).");
        sb.AppendLine("- Level parameters are optional - they default to the active plan view's level when omitted.");
        sb.AppendLine("- Type names support fuzzy matching (e.g., 'W10x49' matches 'W-Wide Flange-Column: W10x49').");
        sb.AppendLine("- Use `resolve_grid_intersection` to get coordinates for grid crossings (useful for floor boundaries).");
        sb.AppendLine();
        sb.AppendLine("**Multi-Step Operations:**");
        sb.AppendLine("- For complex operations requiring multiple tool rounds (query -> plan -> execute), query tools first to gather information (get_levels, get_available_types, get_grids), then execute modifications.");
        sb.AppendLine("- Within each tool round, multiple modification tools are batched into a single undo operation. If any tool in a batch fails, the entire batch is rolled back.");
        sb.AppendLine("- Prefer issuing all related modifications in the SAME tool round when possible, so they group into one Ctrl+Z. For example, place all columns in one round rather than one per round.");
        sb.AppendLine();

        // Screenshot QC guidance (only when screenshots are enabled)
        var configService = ConfigurationService.Instance;
        if (configService.ScreenshotToolEnabled == ScreenshotToolState.Always)
        {
            sb.AppendLine("**CRITICAL: Screenshot-based Quality Control**");
            sb.AppendLine();
            sb.AppendLine("You MUST use the `capture_screenshot` tool to rigorously verify your work. The primary value of an AI assistant in Revit is producing CLEAN, ACCURATE, PRODUCTION-READY work. A superficial 'looks good' check is NOT acceptable.");
            sb.AppendLine();
            sb.AppendLine("AFTER EVERY modification, take a screenshot and critically examine for ALL of these issues:");
            sb.AppendLine();
            sb.AppendLine("**Structural/Framing QC:**");
            sb.AppendLine("- Are beam/column connections correct? (Should frame INTO supporting members, not extend to centerlines)");
            sb.AppendLine("- Are there visual clashes? (Elements passing through each other)");
            sb.AppendLine("- Do framing members align with grids appropriately?");
            sb.AppendLine("- Are structural elements at the correct elevation/level?");
            sb.AppendLine();
            sb.AppendLine("**Annotation/Documentation QC:**");
            sb.AppendLine("- Are NEW elements missing tags? (Beam tags, column tags, door/window tags)");
            sb.AppendLine("- Do existing annotation styles match? (New elements should have same tag style as similar elements)");
            sb.AppendLine("- Are dimensions still valid or do they need updating?");
            sb.AppendLine("- Are there any orphaned annotations pointing to nothing?");
            sb.AppendLine();
            sb.AppendLine("**Visual/Drafting QC:**");
            sb.AppendLine("- Does the linework look correct? (No gaps, overlaps, or artifacts)");
            sb.AppendLine("- Are elements displaying with correct line weights/styles?");
            sb.AppendLine("- Is the view still readable and professional-looking?");
            sb.AppendLine("- Would this view be acceptable on a construction document?");
            sb.AppendLine();
            sb.AppendLine("**Reporting Issues:**");
            sb.AppendLine("Even if you cannot fix an issue (no tool available yet), you MUST report it to the user:");
            sb.AppendLine("- 'I notice the new beams are missing tags - you may want to add beam tags'");
            sb.AppendLine("- 'The beam appears to extend to the column centerline which may cause a clash - consider adjusting the framing connection'");
            sb.AppendLine("- 'The view may need dimension updates to reflect the new elements'");
            sb.AppendLine();
            sb.AppendLine("Your goal is to leave the model in BETTER condition than you found it, or at minimum, clearly communicate what additional work the user needs to do.");
            sb.AppendLine();
        }

        // Context section
        sb.AppendLine("## Current Revit Context");
        sb.AppendLine();

        if (!context.HasActiveDocument)
        {
            sb.AppendLine("**No document is currently open.** Ask the user to open a Revit project.");
            return sb.ToString();
        }

        if (!string.IsNullOrEmpty(context.ErrorMessage))
        {
            sb.AppendLine($"**Warning:** {context.ErrorMessage}");
            sb.AppendLine();
        }

        // View info
        if (context.ActiveView != null)
        {
            var view = context.ActiveView;
            var viewDesc = $"**Active View:** {view.Name} ({view.ViewType})";
            if (!string.IsNullOrEmpty(view.ScaleFormatted))
            {
                viewDesc += $" - Scale: {view.ScaleFormatted}";
            }
            sb.AppendLine(viewDesc);
        }

        // Selection count
        var selectionCount = context.SelectedElements.Count;
        sb.AppendLine($"**Selected Elements:** {selectionCount}");

        // Active level
        if (context.ActiveLevel != null)
        {
            sb.AppendLine($"**Active Level:** {context.ActiveLevel.Name} (Elevation: {context.ActiveLevel.ElevationFormatted})");
        }

        sb.AppendLine();

        // Selected elements - always show IDs so Claude can reference them with tools
        if (selectionCount > 0)
        {
            sb.AppendLine("### Selected Elements");
            sb.AppendLine();

            var displayCount = Math.Min(selectionCount, MaxDetailedElements);
            for (int i = 0; i < displayCount; i++)
            {
                var elem = context.SelectedElements[i];

                sb.AppendLine($"- **[{elem.Id}]** {elem.Category}: {elem.FullTypeName}");

                // Show level and parameters only at verbosity 1+
                if (verbosity >= 1)
                {
                    if (!string.IsNullOrEmpty(elem.LevelName))
                    {
                        sb.AppendLine($"  - Level: {elem.LevelName}");
                    }

                    // Show all parameters at verbosity 1+
                    foreach (var param in elem.Parameters)
                    {
                        if (!string.IsNullOrEmpty(param.Value))
                        {
                            sb.AppendLine($"  - {param.Name}: {param.Value}");
                        }
                    }
                }
            }

            if (selectionCount > MaxDetailedElements)
            {
                sb.AppendLine($"  ... and {selectionCount - MaxDetailedElements} more elements");
            }

            sb.AppendLine();
        }

        // Project info (verbosity 2)
        if (verbosity >= 2 && context.Project != null)
        {
            sb.AppendLine("### Project Information");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(context.Project.Name))
                sb.AppendLine($"- **Name:** {context.Project.Name}");
            if (!string.IsNullOrEmpty(context.Project.Number))
                sb.AppendLine($"- **Number:** {context.Project.Number}");
            if (!string.IsNullOrEmpty(context.Project.Client))
                sb.AppendLine($"- **Client:** {context.Project.Client}");
            if (!string.IsNullOrEmpty(context.Project.Address))
                sb.AppendLine($"- **Address:** {context.Project.Address}");

            if (context.Project.Levels.Count > 0)
            {
                sb.AppendLine($"- **Levels:** {context.Project.Levels.Count}");
                foreach (var level in context.Project.Levels.OrderBy(l => l.Elevation))
                {
                    sb.AppendLine($"  - {level.Name} ({level.ElevationFormatted})");
                }
            }

            sb.AppendLine();
        }

        // Available types (verbosity 2)
        if (verbosity >= 2 && context.AvailableTypes.Count > 0)
        {
            sb.AppendLine("### Available Family Types");
            sb.AppendLine();

            foreach (var kvp in context.AvailableTypes.OrderBy(k => k.Key))
            {
                var typeNames = kvp.Value.Select(t => t.FullName).Take(MaxTypesPerCategory);
                sb.AppendLine($"- **{kvp.Key}:** {string.Join(", ", typeNames)}");

                if (kvp.Value.Count > MaxTypesPerCategory)
                {
                    sb.Append($" ... and {kvp.Value.Count - MaxTypesPerCategory} more");
                }
            }

            sb.AppendLine();
        }

        // Grid layout (verbosity 2)
        if (verbosity >= 2 && context.GridInfo != null && context.GridInfo.TotalCount > 0)
        {
            sb.AppendLine("### Grid Layout");
            sb.AppendLine();

            if (context.GridInfo.HorizontalGrids.Count > 0)
                sb.AppendLine($"- **Horizontal (E-W):** {string.Join(", ", context.GridInfo.HorizontalGrids)}");
            if (context.GridInfo.VerticalGrids.Count > 0)
                sb.AppendLine($"- **Vertical (N-S):** {string.Join(", ", context.GridInfo.VerticalGrids)}");

            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Builds a fallback system prompt when context is unavailable.
    /// </summary>
    /// <returns>A static fallback prompt.</returns>
    public static string BuildFallbackSystemPrompt()
    {
        return @"You are RevitAI, an AI assistant embedded in Autodesk Revit. You help users with their Revit models through natural language conversation.

Your capabilities include:
- Answering questions about Revit and BIM workflows
- Helping users understand their model structure
- Providing guidance on Revit best practices

Be concise and helpful. When discussing Revit elements, use correct terminology.

**Ambiguity - Ask Before Acting:** If the user's request has any ambiguity that could lead to incorrect results, ask a clarifying question before proceeding. Getting it right the first time is more valuable than acting fast and getting it wrong. However, don't over-ask when intent is clear.

**Note:** Unable to gather current Revit context. Some features may be limited until the context can be refreshed.";
    }

    /// <summary>
    /// Captures the active view as a PNG image.
    /// Must be called on the Revit main thread.
    /// </summary>
    /// <param name="app">The UIApplication instance.</param>
    /// <returns>The image bytes, or null if capture fails.</returns>
    public byte[]? CaptureActiveView(UIApplication app)
    {
        try
        {
            var doc = app.ActiveUIDocument?.Document;
            var view = app.ActiveUIDocument?.ActiveView;

            if (doc == null || view == null)
                return null;

            // Skip non-exportable views
            if (view.ViewType == ViewType.Schedule ||
                view.ViewType == ViewType.DrawingSheet ||
                view.ViewType == ViewType.ProjectBrowser ||
                view.ViewType == ViewType.SystemBrowser)
            {
                return null;
            }

            var tempPath = Path.Combine(Path.GetTempPath(), $"RevitAI_capture_{Guid.NewGuid()}.png");

            try
            {
                var options = new ImageExportOptions
                {
                    FilePath = tempPath,
                    ExportRange = ExportRange.CurrentView,
                    ZoomType = ZoomFitType.FitToPage,
                    ImageResolution = ImageResolution.DPI_150,
                    PixelSize = 1920,
                    HLRandWFViewsFileType = ImageFileType.PNG
                };

                doc.ExportImage(options);

                // The exported file may have a suffix added by Revit
                var actualPath = tempPath;
                if (!File.Exists(actualPath))
                {
                    // Try with common suffixes Revit might add
                    var directory = Path.GetDirectoryName(tempPath)!;
                    var baseName = Path.GetFileNameWithoutExtension(tempPath);
                    var possibleFiles = Directory.GetFiles(directory, $"{baseName}*.png");

                    if (possibleFiles.Length > 0)
                    {
                        actualPath = possibleFiles[0];
                    }
                    else
                    {
                        return null;
                    }
                }

                return File.ReadAllBytes(actualPath);
            }
            finally
            {
                // Cleanup temp files
                try
                {
                    var directory = Path.GetDirectoryName(tempPath)!;
                    var baseName = Path.GetFileNameWithoutExtension(tempPath);
                    foreach (var file in Directory.GetFiles(directory, $"{baseName}*.png"))
                    {
                        File.Delete(file);
                    }
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
        catch
        {
            return null;
        }
    }

    #region Private Helper Methods

    private ViewInfo ExtractViewInfo(UIDocument uiDoc)
    {
        var view = uiDoc.ActiveView;
        var viewInfo = new ViewInfo
        {
            Name = view.Name,
            ViewType = view.ViewType.ToString(),
            Scale = view.Scale
        };

        // Format scale for common values
        viewInfo.ScaleFormatted = view.Scale switch
        {
            1 => "1:1 (Full)",
            12 => "1\" = 1'-0\"",
            24 => "1/2\" = 1'-0\"",
            48 => "1/4\" = 1'-0\"",
            96 => "1/8\" = 1'-0\"",
            192 => "1/16\" = 1'-0\"",
            _ => $"1:{view.Scale}"
        };

        // Get associated level for plan views
        if (view is ViewPlan viewPlan && viewPlan.GenLevel != null)
        {
            viewInfo.AssociatedLevel = viewPlan.GenLevel.Name;
        }

        return viewInfo;
    }

    private LevelInfo? ExtractActiveLevel(Document doc, View activeView)
    {
        Level? level = null;

        // Try to get level from ViewPlan
        if (activeView is ViewPlan viewPlan && viewPlan.GenLevel != null)
        {
            level = viewPlan.GenLevel;
        }
        else
        {
            // Get the first level as fallback
            level = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .FirstOrDefault();
        }

        if (level == null)
            return null;

        return new LevelInfo
        {
            Id = level.Id.Value,
            Name = level.Name,
            Elevation = level.Elevation,
            ElevationFormatted = FormatLength(doc, level.Elevation)
        };
    }

    private List<ElementInfo> ExtractSelectedElements(UIDocument uiDoc, Document doc, int verbosity)
    {
        var elements = new List<ElementInfo>();
        var selection = uiDoc.Selection.GetElementIds();

        var count = 0;
        foreach (var id in selection)
        {
            if (count >= MaxDetailedElements)
                break;

            try
            {
                var elem = doc.GetElement(id);
                if (elem == null)
                    continue;

                var elementInfo = ExtractElementInfo(elem, doc, verbosity);
                if (elementInfo != null)
                {
                    elements.Add(elementInfo);
                    count++;
                }
            }
            catch
            {
                // Skip elements that throw exceptions (e.g., deleted elements)
            }
        }

        return elements;
    }

    private ElementInfo? ExtractElementInfo(Element elem, Document doc, int verbosity)
    {
        var info = new ElementInfo
        {
            Id = elem.Id.Value,
            Category = elem.Category?.Name ?? "Unknown"
        };

        // Get family and type names
        if (elem is FamilyInstance familyInstance)
        {
            info.FamilyName = familyInstance.Symbol?.Family?.Name;
            info.TypeName = familyInstance.Symbol?.Name;
            info.FullTypeName = $"{info.FamilyName}: {info.TypeName}";
        }
        else if (elem.GetTypeId() != ElementId.InvalidElementId)
        {
            var elemType = doc.GetElement(elem.GetTypeId());
            if (elemType != null)
            {
                info.TypeName = elemType.Name;

                // Try to get family name from type
                var familyNameParam = elemType.get_Parameter(BuiltInParameter.ALL_MODEL_FAMILY_NAME);
                info.FamilyName = familyNameParam?.AsString();

                info.FullTypeName = string.IsNullOrEmpty(info.FamilyName)
                    ? info.TypeName
                    : $"{info.FamilyName}: {info.TypeName}";
            }
        }

        if (string.IsNullOrEmpty(info.FullTypeName))
        {
            info.FullTypeName = elem.Name ?? "Unknown";
        }

        // Get level
        var levelId = elem.LevelId;
        if (levelId != ElementId.InvalidElementId)
        {
            var level = doc.GetElement(levelId) as Level;
            info.LevelName = level?.Name;
        }
        else
        {
            // Try to get level from parameter
            var levelParam = elem.get_Parameter(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM)
                          ?? elem.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM)
                          ?? elem.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM);

            if (levelParam != null && levelParam.HasValue)
            {
                var level = doc.GetElement(levelParam.AsElementId()) as Level;
                info.LevelName = level?.Name;
            }
        }

        // Get location
        info.Location = ExtractLocation(elem, doc);

        // Get parameters (verbosity 1+)
        if (verbosity >= 1)
        {
            info.Parameters = ExtractParameters(elem, doc, verbosity);
        }

        return info;
    }

    private string? ExtractLocation(Element elem, Document doc)
    {
        var location = elem.Location;

        if (location is LocationPoint locationPoint)
        {
            var pt = locationPoint.Point;
            return $"({FormatLength(doc, pt.X)}, {FormatLength(doc, pt.Y)}, {FormatLength(doc, pt.Z)})";
        }
        else if (location is LocationCurve locationCurve)
        {
            var curve = locationCurve.Curve;
            var start = curve.GetEndPoint(0);
            var end = curve.GetEndPoint(1);
            return $"Line from ({FormatLength(doc, start.X)}, {FormatLength(doc, start.Y)}) to ({FormatLength(doc, end.X)}, {FormatLength(doc, end.Y)})";
        }

        return null;
    }

    private List<ParameterInfo> ExtractParameters(Element elem, Document doc, int verbosity)
    {
        var parameters = new List<ParameterInfo>();

        // Get commonly useful parameters first
        var priorityParams = new[]
        {
            BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS,
            BuiltInParameter.ALL_MODEL_MARK,
            BuiltInParameter.CURVE_ELEM_LENGTH,
            BuiltInParameter.WALL_USER_HEIGHT_PARAM,
            BuiltInParameter.INSTANCE_LENGTH_PARAM,
            BuiltInParameter.FAMILY_WIDTH_PARAM,
            BuiltInParameter.FAMILY_HEIGHT_PARAM,
            BuiltInParameter.HOST_AREA_COMPUTED,
            BuiltInParameter.HOST_VOLUME_COMPUTED
        };

        foreach (var builtIn in priorityParams)
        {
            var param = elem.get_Parameter(builtIn);
            if (param != null && param.HasValue)
            {
                var paramInfo = CreateParameterInfo(param, doc);
                if (paramInfo != null && !string.IsNullOrEmpty(paramInfo.Value))
                {
                    parameters.Add(paramInfo);
                }
            }
        }

        // Add other instance parameters if we have room (verbosity 1+)
        if (verbosity >= 1 && parameters.Count < MaxParametersPerElement)
        {
            foreach (Parameter param in elem.Parameters)
            {
                if (parameters.Count >= MaxParametersPerElement)
                    break;

                if (!param.HasValue)
                    continue;

                // Skip internal and already-added parameters
                if (InternalParameterNames.Contains(param.Definition.Name))
                    continue;

                if (parameters.Any(p => p.Name == param.Definition.Name))
                    continue;

                // Only instance parameters
                if (!param.IsShared && param.Definition is InternalDefinition internalDef)
                {
                    // Skip type parameters
                    if (internalDef.VariesAcrossGroups == false)
                        continue;
                }

                var paramInfo = CreateParameterInfo(param, doc);
                if (paramInfo != null && !string.IsNullOrEmpty(paramInfo.Value))
                {
                    parameters.Add(paramInfo);
                }
            }
        }

        return parameters;
    }

    private ParameterInfo? CreateParameterInfo(Parameter param, Document doc)
    {
        if (param == null || !param.HasValue)
            return null;

        var info = new ParameterInfo
        {
            Name = param.Definition.Name,
            StorageType = param.StorageType.ToString(),
            IsReadOnly = param.IsReadOnly,
            IsInstance = true // We're only extracting instance params
        };

        // Format value based on storage type
        info.Value = param.StorageType switch
        {
            StorageType.String => param.AsString() ?? string.Empty,
            StorageType.Integer => param.AsInteger().ToString(),
            StorageType.Double => FormatParameterDouble(param, doc),
            StorageType.ElementId => FormatElementIdParameter(param, doc),
            _ => param.AsValueString() ?? string.Empty
        };

        return info;
    }

    private string FormatParameterDouble(Parameter param, Document doc)
    {
        // Use value string if available (includes units)
        var valueString = param.AsValueString();
        if (!string.IsNullOrEmpty(valueString))
            return valueString;

        // Otherwise format the raw value
        return param.AsDouble().ToString("F3");
    }

    private string FormatElementIdParameter(Parameter param, Document doc)
    {
        var elemId = param.AsElementId();
        if (elemId == ElementId.InvalidElementId)
            return string.Empty;

        var elem = doc.GetElement(elemId);
        return elem?.Name ?? $"Element {elemId.Value}";
    }

    private RevitProjectInfo ExtractRevitProjectInfo(Document doc)
    {
        var info = new RevitProjectInfo();

        var projectInfo = doc.ProjectInformation;
        if (projectInfo != null)
        {
            info.Name = projectInfo.Name;
            info.Number = projectInfo.Number;
            info.Client = projectInfo.ClientName;
            info.Address = projectInfo.Address;
        }

        info.FilePath = doc.PathName;

        // Get all levels
        info.Levels = new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .Select(l => new LevelInfo
            {
                Id = l.Id.Value,
                Name = l.Name,
                Elevation = l.Elevation,
                ElevationFormatted = FormatLength(doc, l.Elevation)
            })
            .OrderBy(l => l.Elevation)
            .ToList();

        return info;
    }

    private GridSummary? ExtractGridInfo(Document doc)
    {
        try
        {
            var (horizontal, vertical) = GeometryResolver.GetGridNamesByOrientation(doc);
            var totalCount = horizontal.Count + vertical.Count;

            if (totalCount == 0)
                return null;

            return new GridSummary
            {
                HorizontalGrids = horizontal,
                VerticalGrids = vertical,
                TotalCount = totalCount
            };
        }
        catch
        {
            return null;
        }
    }

    private Dictionary<string, List<TypeInfo>> ExtractAvailableTypes(Document doc)
    {
        var types = new Dictionary<string, List<TypeInfo>>();

        foreach (var category in TypeCategories)
        {
            try
            {
                var collector = new FilteredElementCollector(doc)
                    .OfCategory(category)
                    .WhereElementIsElementType();

                var typeList = new List<TypeInfo>();

                foreach (var elemType in collector.Take(MaxTypesPerCategory * 2)) // Get extra to filter
                {
                    if (typeList.Count >= MaxTypesPerCategory)
                        break;

                    var typeInfo = new TypeInfo
                    {
                        Id = elemType.Id.Value,
                        TypeName = elemType.Name
                    };

                    // Get family name
                    var familyParam = elemType.get_Parameter(BuiltInParameter.ALL_MODEL_FAMILY_NAME);
                    typeInfo.FamilyName = familyParam?.AsString() ?? string.Empty;

                    typeInfo.FullName = string.IsNullOrEmpty(typeInfo.FamilyName)
                        ? typeInfo.TypeName
                        : $"{typeInfo.FamilyName}: {typeInfo.TypeName}";

                    typeList.Add(typeInfo);
                }

                if (typeList.Count > 0)
                {
                    var categoryName = GetCategoryDisplayName(category);
                    types[categoryName] = typeList;
                }
            }
            catch
            {
                // Skip categories that throw exceptions
            }
        }

        return types;
    }

    private static string GetCategoryDisplayName(BuiltInCategory category)
    {
        return category switch
        {
            BuiltInCategory.OST_Walls => "Walls",
            BuiltInCategory.OST_Doors => "Doors",
            BuiltInCategory.OST_Windows => "Windows",
            BuiltInCategory.OST_StructuralColumns => "Columns",
            BuiltInCategory.OST_StructuralFraming => "Framing",
            BuiltInCategory.OST_Floors => "Floors",
            BuiltInCategory.OST_Roofs => "Roofs",
            BuiltInCategory.OST_Ceilings => "Ceilings",
            BuiltInCategory.OST_GenericModel => "Generic Models",
            _ => category.ToString().Replace("OST_", "")
        };
    }

    private string FormatLength(Document doc, double feet)
    {
        // Simple imperial formatting
        var totalInches = feet * 12;
        var wholeFeet = (int)Math.Floor(Math.Abs(feet));
        var inches = Math.Abs(totalInches) - (wholeFeet * 12);

        var sign = feet < 0 ? "-" : "";

        if (wholeFeet == 0)
        {
            return $"{sign}{inches:F2}\"";
        }
        else if (Math.Abs(inches) < 0.01)
        {
            return $"{sign}{wholeFeet}'-0\"";
        }
        else
        {
            return $"{sign}{wholeFeet}'-{inches:F2}\"";
        }
    }

    #endregion
}
