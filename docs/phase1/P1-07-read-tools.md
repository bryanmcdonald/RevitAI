# P1-07: Read-Only Tools

**Status**: ✅ Complete

**Goal**: Implement tools for querying model information without modification.

**Prerequisites**: P1-06 complete.

---

## Files Created

```
src/RevitAI/Tools/ReadTools/
├── Helpers/
│   └── CategoryHelper.cs           # Category name → BuiltInCategory mapping
├── GetLevelsTool.cs                # get_levels
├── GetGridsTool.cs                 # get_grids
├── GetProjectInfoTool.cs           # get_project_info
├── GetViewInfoTool.cs              # get_view_info
├── GetSelectedElementsTool.cs      # get_selected_elements
├── GetWarningsTool.cs              # get_warnings
├── GetAvailableTypesTool.cs        # get_available_types
├── GetElementsByCategoryTool.cs    # get_elements_by_category
├── GetElementPropertiesTool.cs     # get_element_properties
├── GetRoomInfoTool.cs              # get_room_info
└── GetElementQuantityTakeoffTool.cs # get_element_quantity_takeoff
```

**Modified**: `src/RevitAI/App.cs` - Added tool registrations in `RegisterTools()`

---

## Tool Summary

| Tool Name | Input | Output | Notes |
|-----------|-------|--------|-------|
| `get_levels` | None | Levels with elevations | Sorted by elevation |
| `get_grids` | None | Grids with coordinates | Includes curved flag |
| `get_project_info` | None | Project metadata | Name, number, client, workshared status |
| `get_view_info` | None | Active view details | Scale, level, phase, detail level |
| `get_selected_elements` | None | Selected elements (max 50) | ID, category, family/type, parameters |
| `get_warnings` | None | Model warnings (max 100) | Grouped summary included |
| `get_available_types` | `category` | Family types for category | Uses CategoryHelper |
| `get_elements_by_category` | `category`, `level?` | Elements (max 100) | Multi-parameter level filter |
| `get_element_properties` | `element_id`, `parameter_names?` | Full element details | Instance + type parameters |
| `get_room_info` | `room_id?`, `level?` | Room data | Area, volume, enclosed status |
| `get_element_quantity_takeoff` | `category?`, `level?` | Grouped counts | By category and type |

---

## Implementation Patterns

### CategoryHelper Utility

Shared utility for mapping user-friendly category names to `BuiltInCategory`:

```csharp
// Case-insensitive, singular/plural support
CategoryHelper.TryGetCategory("walls", out var category);  // OST_Walls
CategoryHelper.TryGetCategory("Wall", out var category);   // OST_Walls
CategoryHelper.TryGetCategory("structural framing", out var category); // OST_StructuralFraming

// Error messages list valid options
CategoryHelper.GetInvalidCategoryError("blurgh");
// "Unknown category: 'blurgh'. Valid categories include: Walls, Doors, Windows..."
```

### JSON Output

All tools use `JsonNamingPolicy.SnakeCaseLower` for consistent output:

```csharp
private static readonly JsonSerializerOptions _jsonOptions = new()
{
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};
```

### Truncation Pattern

Large result sets are truncated with clear messaging:

```csharp
var truncated = totalCount > MaxElements;
var result = new {
    elements = elements.Take(MaxElements),
    count = totalCount,
    truncated = truncated,
    truncated_message = truncated ? $"Showing {MaxElements} of {totalCount} elements." : null
};
```

---

## Lessons Learned / Implementation Notes

### Level Filtering for Structural Elements

**Issue**: `ElementLevelFilter` only checks the `LevelId` property, but structural framing (beams, columns) use "Reference Level" parameters instead.

**Solution**: Custom `IsElementOnLevel()` method that checks multiple level associations:

```csharp
private static bool IsElementOnLevel(Element elem, Level targetLevel, Document doc)
{
    var targetLevelId = targetLevel.Id;

    // Check 1: Direct LevelId property (walls, floors, ceilings)
    if (elem.LevelId == targetLevelId) return true;

    // Check 2: Reference Level parameter (structural framing, columns)
    var refLevelParam = elem.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM);
    if (refLevelParam?.AsElementId() == targetLevelId) return true;

    // Check 3-6: Other level parameters...
    // INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM, FAMILY_LEVEL_PARAM,
    // FAMILY_BASE_LEVEL_PARAM, LEVEL_PARAM
}
```

**Future consideration**: P2-04 (Smart Context) should use similar multi-parameter level detection.

### Element ID Validation

**Issue**: Zero and negative element IDs can return unexpected results.

**Solution**: Validate element IDs are positive before querying:

```csharp
if (elementIdValue <= 0)
{
    return ToolResult.Error($"Invalid element ID: {elementIdValue}. Element IDs must be positive integers.");
}
```

### Room Boundary Detection

Rooms without proper enclosure have `Area = 0`. The `is_enclosed` flag helps Claude understand room state:

```csharp
data.IsEnclosed = room.Area > 0;
```

---

## Verification Checklist

- [x] `get_levels` - Returns all levels sorted by elevation
- [x] `get_grids` - Returns grid names and coordinates
- [x] `get_project_info` - Returns project metadata
- [x] `get_view_info` - Returns active view details including phase
- [x] `get_selected_elements` - Returns selected elements (truncates at 50)
- [x] `get_warnings` - Returns warnings with summary grouping
- [x] `get_available_types` - Returns types for valid categories, errors for invalid
- [x] `get_elements_by_category` - Returns elements with level filter (including structural)
- [x] `get_element_properties` - Returns full parameters, rejects invalid IDs
- [x] `get_room_info` - Returns room data with enclosed status
- [x] `get_element_quantity_takeoff` - Returns grouped counts

---

## Future Enhancements (Deferred)

| Enhancement | Target Phase | Notes |
|-------------|--------------|-------|
| View-specific element filtering | P2-04 | Filter elements visible in current view |
| Parameter value search | P2-06 | Search elements by parameter value |
| Spatial queries | P3-04 | Elements within bounding box/region |
| Linked model support | P3-06 | Query elements in linked models |

---

## P1-06 Framework Reference

All tools implement `IRevitTool`:

```csharp
public interface IRevitTool
{
    string Name { get; }                    // snake_case identifier
    string Description { get; }             // For Claude's understanding
    JsonElement InputSchema { get; }        // JSON Schema for parameters
    bool RequiresTransaction { get; }       // false for all read-only tools
    Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken ct);
}
```

**Registration** in `App.RegisterTools()`:
```csharp
// Read-only tools (P1-07)
registry.Register(new GetLevelsTool());
registry.Register(new GetGridsTool());
// ... all 11 tools
```
