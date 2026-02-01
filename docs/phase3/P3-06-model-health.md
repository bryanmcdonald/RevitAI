# P3-06: Model Health Tools

**Goal**: Add model quality and hygiene analysis.

**Prerequisites**: P3-05 complete.

**Scope**:
- `get_all_warnings` - Comprehensive warning report
- `find_overlapping_elements` - Geometric clash detection
- `find_unhosted_elements` - Elements not properly hosted
- `find_elements_without_level` - Elements not associated with any level
- `find_duplicate_instances` - Duplicate element detection
- `analyze_model_size` - Element count by category

---

## Key Revit API Areas

- Document.GetWarnings()
- BoundingBoxIntersectsFilter
- FilteredElementCollector analysis
- Level parameter checking

---

## Key Files to Create

- `src/RevitAI/Tools/ModelHealthTools/GetAllWarningsTool.cs`
- `src/RevitAI/Tools/ModelHealthTools/FindOverlappingElementsTool.cs`
- `src/RevitAI/Tools/ModelHealthTools/FindUnhostedElementsTool.cs`
- `src/RevitAI/Tools/ModelHealthTools/FindElementsWithoutLevelTool.cs`
- `src/RevitAI/Tools/ModelHealthTools/FindDuplicateInstancesTool.cs`
- `src/RevitAI/Tools/ModelHealthTools/AnalyzeModelSizeTool.cs`

---

## Implementation Details

> *This is a preliminary outline. Detailed implementation will be added during the chunk planning session.*

### 1. get_all_warnings

```csharp
public Task<ToolResult> ExecuteAsync(...)
{
    var warnings = doc.GetWarnings();
    var result = warnings.Select(w => new {
        Description = w.GetDescriptionText(),
        Severity = w.GetSeverity().ToString(),
        ElementIds = w.GetFailingElements().Select(id => id.Value)
    }).ToList();
    return ToolResult.Ok(JsonSerializer.Serialize(result));
}
```

### 2. find_overlapping_elements

```csharp
// Input: { "category": "Walls" }
public Task<ToolResult> ExecuteAsync(...)
{
    var elements = new FilteredElementCollector(doc)
        .OfCategory(category)
        .WhereElementIsNotElementType()
        .ToElements();

    var overlaps = new List<(ElementId, ElementId)>();
    foreach (var elem1 in elements)
    {
        var bb = elem1.get_BoundingBox(null);
        if (bb == null) continue;

        var filter = new BoundingBoxIntersectsFilter(new Outline(bb.Min, bb.Max));
        var intersecting = new FilteredElementCollector(doc)
            .OfCategory(category)
            .WherePasses(filter)
            .Where(e => e.Id != elem1.Id);

        foreach (var elem2 in intersecting)
            overlaps.Add((elem1.Id, elem2.Id));
    }
    return ToolResult.Ok($"Found {overlaps.Count} overlapping pairs");
}
```

### 3. find_elements_without_level

```csharp
// Input: { "categories": ["Walls", "Columns", "Floors"] }
public Task<ToolResult> ExecuteAsync(...)
{
    var unassigned = new List<ElementInfo>();

    foreach (var category in categories)
    {
        var elements = new FilteredElementCollector(doc)
            .OfCategory(GetBuiltInCategory(category))
            .WhereElementIsNotElementType()
            .ToElements();

        foreach (var elem in elements)
        {
            var levelParam = elem.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM)
                ?? elem.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM)
                ?? elem.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM);

            if (levelParam == null || levelParam.AsElementId() == ElementId.InvalidElementId)
            {
                unassigned.Add(new ElementInfo {
                    Id = elem.Id.Value,
                    Category = category,
                    Name = elem.Name
                });
            }
        }
    }

    return ToolResult.Ok($"Found {unassigned.Count} elements without level assignment");
}
```

### 4. find_duplicate_instances

```csharp
// Find elements at same location with same type
public Task<ToolResult> ExecuteAsync(...)
{
    var duplicates = elements
        .GroupBy(e => (GetLocation(e), e.GetTypeId()))
        .Where(g => g.Count() > 1)
        .SelectMany(g => g.Skip(1))
        .ToList();
    return ToolResult.Ok($"Found {duplicates.Count} duplicate instances");
}
```

### 5. analyze_model_size

```csharp
public Task<ToolResult> ExecuteAsync(...)
{
    var summary = new Dictionary<string, int>();
    foreach (BuiltInCategory cat in Enum.GetValues(typeof(BuiltInCategory)))
    {
        try
        {
            var count = new FilteredElementCollector(doc)
                .OfCategory(cat)
                .WhereElementIsNotElementType()
                .GetElementCount();
            if (count > 0)
                summary[cat.ToString()] = count;
        }
        catch { /* Some categories don't support collection */ }
    }
    return ToolResult.Ok(JsonSerializer.Serialize(summary.OrderByDescending(kv => kv.Value)));
}
```

---

## Verification (Manual)

1. Ask Claude "What warnings are in the model?"
2. Ask Claude "Find all overlapping walls"
3. Ask Claude "Are there any elements not assigned to a level?"
4. Ask Claude "How many elements are in the model by category?"
