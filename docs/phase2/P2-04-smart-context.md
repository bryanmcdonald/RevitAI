# P2-04: Smart Context Awareness

**Goal**: Add intelligent interpretation of relative positions, grid references, and type inference.

**Prerequisites**: P2-03 complete.

**Key Files to Create/Modify**:
- `src/RevitAI/Services/ContextEngine.cs` (enhanced)
- `src/RevitAI/Services/GeometryResolver.cs`
- `src/RevitAI/Services/TypeResolver.cs`

---

## Implementation Details

### 1. Grid Intersection Resolver

```csharp
public class GeometryResolver
{
    public XYZ? ResolveGridIntersection(Document doc, string grid1Name, string grid2Name)
    {
        var grid1 = FindGrid(doc, grid1Name);
        var grid2 = FindGrid(doc, grid2Name);

        if (grid1 == null || grid2 == null) return null;

        var curve1 = grid1.Curve;
        var curve2 = grid2.Curve;

        // Find intersection point
        var result = curve1.Intersect(curve2, out var resultArray);
        if (result == SetComparisonResult.Overlap && resultArray.Size > 0)
        {
            return resultArray.get_Item(0).XYZPoint;
        }
        return null;
    }

    public List<XYZ> GetAllGridIntersections(Document doc)
    {
        // Return all intersection points for batch operations
    }
}
```

### 2. Relative Position Resolver

```csharp
public XYZ ResolveRelativePosition(Document doc, ElementId referenceId, string direction, double distance)
{
    var elem = doc.GetElement(referenceId);
    var location = GetElementLocation(elem);

    var offset = direction.ToLower() switch
    {
        "right" or "east" => new XYZ(distance, 0, 0),
        "left" or "west" => new XYZ(-distance, 0, 0),
        "up" or "north" => new XYZ(0, distance, 0),
        "down" or "south" => new XYZ(0, -distance, 0),
        "above" => new XYZ(0, 0, distance),
        "below" => new XYZ(0, 0, -distance),
        _ => throw new ArgumentException($"Unknown direction: {direction}")
    };

    return location + offset;
}
```

### 3. Type Inference

```csharp
public class TypeResolver
{
    public FamilySymbol? FindBestMatch(Document doc, BuiltInCategory category, string userInput)
    {
        var types = new FilteredElementCollector(doc)
            .OfClass(typeof(FamilySymbol))
            .OfCategory(category)
            .Cast<FamilySymbol>()
            .ToList();

        // Exact match first
        var exact = types.FirstOrDefault(t =>
            t.Name.Equals(userInput, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        // Partial match (e.g., "W10x49" matches "W10x49 - Structural Column")
        var partial = types.FirstOrDefault(t =>
            t.Name.Contains(userInput, StringComparison.OrdinalIgnoreCase));
        if (partial != null) return partial;

        // Fuzzy match
        return types.OrderBy(t => LevenshteinDistance(t.Name, userInput)).FirstOrDefault();
    }
}
```

### 4. Level Inference from Active View

```csharp
public Level? InferLevelFromContext(UIDocument uidoc)
{
    var view = uidoc.ActiveView;
    if (view is ViewPlan plan)
    {
        return plan.GenLevel;
    }
    // Fall back to lowest level or ask user
    return null;
}
```

### 5. Update Tool Input Processing

```csharp
// In tool execution, resolve references before acting
if (input.TryGetProperty("grid_intersection", out var gridRef))
{
    var point = _geometryResolver.ResolveGridIntersection(
        doc,
        gridRef.GetProperty("grid1").GetString(),
        gridRef.GetProperty("grid2").GetString());
    // Use point for placement
}
```

---

## Verification (Manual)

1. Ask Claude "Place a column at grid A-1"
2. Ask Claude "Add a beam 3 feet to the right of the selected column"
3. Ask Claude "Use a W10x49 column" (should find the matching type)
4. In a Level 2 floor plan, ask Claude to place an element (should default to Level 2)
