# P1-09: Modification Tools

**Goal**: Implement tools for selecting, viewing, and modifying elements.

**Prerequisites**: P1-08 complete.

**Key Files to Create**:
- `src/RevitAI/Tools/ModifyTools/SelectElementsTool.cs`
- `src/RevitAI/Tools/ModifyTools/ZoomToElementTool.cs`
- `src/RevitAI/Tools/ModifyTools/ModifyElementParameterTool.cs`
- `src/RevitAI/Tools/ModifyTools/ChangeElementTypeTool.cs`
- `src/RevitAI/Tools/ModifyTools/PlaceWallTool.cs`
- `src/RevitAI/Tools/ModifyTools/PlaceColumnTool.cs`
- `src/RevitAI/Tools/ModifyTools/PlaceBeamTool.cs`
- `src/RevitAI/Tools/ModifyTools/PlaceFloorTool.cs`
- `src/RevitAI/Tools/ModifyTools/MoveElementTool.cs`
- `src/RevitAI/Tools/ModifyTools/DeleteElementsTool.cs`

---

## Implementation Details

### 1. select_elements

Set Revit selection.

```csharp
public class SelectElementsTool : IRevitTool
{
    public string Name => "select_elements";
    public string Description => "Sets the Revit selection to the specified element IDs";
    public bool RequiresTransaction => false;

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken ct)
    {
        var ids = input.GetProperty("element_ids").EnumerateArray()
            .Select(e => new ElementId(e.GetInt64()))
            .ToList();

        app.ActiveUIDocument.Selection.SetElementIds(ids);
        return Task.FromResult(ToolResult.Ok($"Selected {ids.Count} elements"));
    }
}
```

### 2. zoom_to_element

Adjust view to show element.

```csharp
// Use UIDocument.ShowElements() or set view bounding box
```

### 3. modify_element_parameter

```csharp
// Input: { "element_id": 12345, "parameter_name": "Mark", "value": "C-101" }
public Task<ToolResult> ExecuteAsync(...)
{
    var elem = doc.GetElement(new ElementId(elementId));
    var param = elem.LookupParameter(parameterName);
    // Handle different StorageTypes (String, Double, Integer, ElementId)
    param.Set(value);
    return Task.FromResult(ToolResult.Ok("Parameter updated"));
}
```

### 4. place_wall

```csharp
// Input: { "start": [0, 0], "end": [20, 0], "wall_type": "Basic Wall", "base_level": "Level 1", "height": 10 }
public Task<ToolResult> ExecuteAsync(...)
{
    var wallType = FindWallType(typeName);
    var level = FindLevel(levelName);
    var line = Line.CreateBound(
        new XYZ(start[0], start[1], 0),
        new XYZ(end[0], end[1], 0));

    var wall = Wall.Create(doc, line, wallType.Id, level.Id, height, 0, false, false);
    return Task.FromResult(ToolResult.Ok($"Created wall with ID {wall.Id.Value}"));
}
```

### 5. place_column

```csharp
// Input: { "location": [10, 10], "column_type": "W10x49", "base_level": "Level 1", "top_level": "Level 2" }
// Use doc.Create.NewFamilyInstance with StructuralType.Column
```

### 6. place_beam

```csharp
// Input: { "start": [0, 0, 10], "end": [20, 0, 10], "beam_type": "W12x26", "level": "Level 2" }
// Use doc.Create.NewFamilyInstance with StructuralType.Beam
```

### 7. place_floor

```csharp
// Input: { "boundary": [[0,0], [20,0], [20,20], [0,20]], "floor_type": "Generic", "level": "Level 1" }
// Create CurveArray from boundary, use doc.Create.NewFloor
```

### 8. move_element

```csharp
// Input: { "element_id": 12345, "translation": [5, 0, 0] }
ElementTransformUtils.MoveElement(doc, elementId, new XYZ(tx, ty, tz));
```

### 9. delete_elements

Mark as destructive (will need confirmation in P1-10).

```csharp
// Input: { "element_ids": [12345, 12346] }
doc.Delete(elementIds);
```

---

## Register All Tools

```csharp
registry.Register(new SelectElementsTool());
registry.Register(new ZoomToElementTool());
// ... all modify tools
```

---

## Verification (Manual)

1. Build and deploy
2. Open a Revit project
3. Ask Claude "Select all columns on Level 1"
4. Ask Claude "Place a wall from (0,0) to (20,0) on Level 1"
5. Ask Claude "Move the selected element 5 feet to the right"
6. Ask Claude "Change the wall type to 'Generic - 8"'"
7. Verify all operations work and can be undone with Ctrl+Z
