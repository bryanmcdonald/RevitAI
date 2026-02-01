# P1-07: Read-Only Tools

**Goal**: Implement tools for querying model information without modification.

**Prerequisites**: P1-06 complete.

**Key Files to Create**:
- `src/RevitAI/Tools/ReadTools/GetSelectedElementsTool.cs`
- `src/RevitAI/Tools/ReadTools/GetElementPropertiesTool.cs`
- `src/RevitAI/Tools/ReadTools/GetElementsByCategoryTool.cs`
- `src/RevitAI/Tools/ReadTools/GetViewInfoTool.cs`
- `src/RevitAI/Tools/ReadTools/GetLevelsTool.cs`
- `src/RevitAI/Tools/ReadTools/GetGridsTool.cs`
- `src/RevitAI/Tools/ReadTools/GetAvailableTypesTool.cs`
- `src/RevitAI/Tools/ReadTools/GetProjectInfoTool.cs`
- `src/RevitAI/Tools/ReadTools/GetElementQuantityTakeoffTool.cs`
- `src/RevitAI/Tools/ReadTools/GetRoomInfoTool.cs`
- `src/RevitAI/Tools/ReadTools/GetWarningsTool.cs`

---

## Implementation Details

### 1. get_selected_elements

```csharp
public class GetSelectedElementsTool : IRevitTool
{
    public string Name => "get_selected_elements";
    public string Description => "Returns details of currently selected elements including IDs, categories, types, and key parameters";

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken ct)
    {
        var uidoc = app.ActiveUIDocument;
        var doc = uidoc.Document;
        var selection = uidoc.Selection.GetElementIds();

        var elements = selection.Select(id =>
        {
            var elem = doc.GetElement(id);
            return new {
                Id = id.Value,
                Category = elem.Category?.Name,
                TypeName = elem.Name,
                // Key parameters
            };
        }).ToList();

        return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(elements)));
    }
}
```

### 2. get_elements_by_category

Filter by category, optional level/view filter.

```csharp
// Input: { "category": "Walls", "level": "Level 1" }
// Uses FilteredElementCollector with category filter
```

### 3. get_view_info

Active view details.

### 4. get_levels

All levels with names and elevations.

### 5. get_grids

All grids with geometry.

### 6. get_available_types

Loaded family types for a category.

### 7. get_project_info

Project name, number, location.

### 8. get_element_quantity_takeoff

Count/summarize by category.

### 9. get_room_info

Room boundaries, areas.

### 10. get_warnings

Current Revit warnings.

---

## Register All Tools in App.OnStartup

```csharp
var registry = new ToolRegistry();
registry.Register(new GetSelectedElementsTool());
registry.Register(new GetElementPropertiesTool());
// ... register all
```

---

## Verification (Manual)

1. Build and deploy
2. Open a Revit project with various elements
3. Select some walls, ask Claude "What walls do I have selected?"
4. Ask "How many columns are on Level 1?"
5. Ask "What levels exist in this project?"
6. Ask "Are there any warnings in the model?"
7. Verify Claude uses tools and returns accurate information
