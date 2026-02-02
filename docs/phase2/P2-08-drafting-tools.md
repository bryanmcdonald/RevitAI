# P2-08: Drafting & Documentation Tools

**Goal**: Provide comprehensive 2D drafting and documentation capabilities including advanced linework, hatching, symbols, sheet layout, callouts, legends, and revision tracking.

**Prerequisites**: P2-01 complete (provides `create_sheet`, `place_detail_line`, `place_text_note`).

**Key Files to Create**:
- `src/RevitAI/Tools/DraftingTools/PlaceDetailArcTool.cs`
- `src/RevitAI/Tools/DraftingTools/PlaceDetailCurveTool.cs`
- `src/RevitAI/Tools/DraftingTools/PlaceFilledRegionTool.cs`
- `src/RevitAI/Tools/DraftingTools/PlaceMaskingRegionTool.cs`
- `src/RevitAI/Tools/DraftingTools/PlaceDetailComponentTool.cs`
- `src/RevitAI/Tools/DraftingTools/PlaceViewportTool.cs`
- `src/RevitAI/Tools/DraftingTools/PlaceCalloutTool.cs`
- `src/RevitAI/Tools/DraftingTools/CreateLegendTool.cs`
- `src/RevitAI/Tools/DraftingTools/PlaceLegendComponentTool.cs`
- `src/RevitAI/Tools/DraftingTools/PlaceRevisionCloudTool.cs`

---

## Overview

This chunk extends the basic drafting tools from P2-01 with a full 2D documentation suite. While P2-01 covers basic placement (detail lines, text, dimensions, tags), P2-08 focuses on:

- **Advanced linework**: Arcs, splines, and complex curves
- **Regions**: Filled regions (hatching) and masking regions
- **Symbols**: Detail component families (break lines, north arrows, etc.)
- **Sheet layout**: Placing views on sheets via viewports
- **Annotations**: Callouts, section marks, and revision clouds
- **Legends**: Creating legend views and populating with components

---

## Implementation Details

### 1. place_detail_arc

Draw arc linework in drafting or detail views.

```csharp
// Input: {
//   "view_id": 123,
//   "center": [10, 5],
//   "radius": 3.0,
//   "start_angle": 0,      // degrees
//   "end_angle": 90,       // degrees
//   "line_style": "Medium Lines"  // optional
// }

public class PlaceDetailArcTool : IRevitTool
{
    public string Name => "place_detail_arc";
    public string Description => "Draw an arc in a drafting or detail view";
    public bool RequiresConfirmation => true;

    public ToolResult Execute(Document doc, JsonElement args)
    {
        var viewId = new ElementId(args.GetProperty("view_id").GetInt64());
        var view = doc.GetElement(viewId) as View;

        if (view == null)
            return ToolResult.Error("View not found");

        var center = GetXYZ(args.GetProperty("center"));
        var radius = args.GetProperty("radius").GetDouble();
        var startAngle = args.GetProperty("start_angle").GetDouble() * Math.PI / 180;
        var endAngle = args.GetProperty("end_angle").GetDouble() * Math.PI / 180;

        // Create arc geometry
        var arc = Arc.Create(
            center,
            radius,
            startAngle,
            endAngle,
            XYZ.BasisX,
            XYZ.BasisY
        );

        using (var tx = new Transaction(doc, "Place Detail Arc"))
        {
            tx.Start();
            var detailCurve = doc.Create.NewDetailCurve(view, arc);

            // Apply line style if specified
            if (args.TryGetProperty("line_style", out var styleElement))
            {
                var style = GetLineStyle(doc, styleElement.GetString());
                if (style != null)
                    detailCurve.LineStyle = style;
            }

            tx.Commit();
            return ToolResult.Success($"Created arc with ID {detailCurve.Id.IntegerValue}");
        }
    }
}
```

### 2. place_detail_curve

Draw splines and complex curves in drafting views.

```csharp
// Input: {
//   "view_id": 123,
//   "curve_type": "spline",  // "spline" or "hermite"
//   "points": [[0,0], [5,3], [10,0], [15,5]],
//   "line_style": "Thin Lines"  // optional
// }

public class PlaceDetailCurveTool : IRevitTool
{
    public string Name => "place_detail_curve";
    public string Description => "Draw a spline or complex curve in a drafting view";
    public bool RequiresConfirmation => true;

    public ToolResult Execute(Document doc, JsonElement args)
    {
        var viewId = new ElementId(args.GetProperty("view_id").GetInt64());
        var view = doc.GetElement(viewId) as View;
        var curveType = args.GetProperty("curve_type").GetString();
        var pointsArray = args.GetProperty("points");

        var points = new List<XYZ>();
        foreach (var pt in pointsArray.EnumerateArray())
        {
            points.Add(GetXYZ(pt));
        }

        Curve curve;
        if (curveType == "hermite")
        {
            curve = HermiteSpline.Create(points, false);
        }
        else
        {
            // NurbSpline for general splines
            curve = NurbSpline.CreateCurve(points, points.Select(_ => 1.0).ToList());
        }

        using (var tx = new Transaction(doc, "Place Detail Curve"))
        {
            tx.Start();
            var detailCurve = doc.Create.NewDetailCurve(view, curve);
            tx.Commit();
            return ToolResult.Success($"Created {curveType} with ID {detailCurve.Id.IntegerValue}");
        }
    }
}
```

### 3. place_filled_region

Create hatched/filled areas with specified patterns.

```csharp
// Input: {
//   "view_id": 123,
//   "boundary_points": [[0,0], [10,0], [10,10], [0,10]],
//   "fill_pattern_name": "Concrete",  // or "Diagonal Up", "Earth", etc.
//   "region_type_name": "Filled Region 1"  // optional, defaults to first available
// }

public class PlaceFilledRegionTool : IRevitTool
{
    public string Name => "place_filled_region";
    public string Description => "Create a filled region with hatching pattern";
    public bool RequiresConfirmation => true;

    public ToolResult Execute(Document doc, JsonElement args)
    {
        var viewId = new ElementId(args.GetProperty("view_id").GetInt64());
        var view = doc.GetElement(viewId) as View;
        var patternName = args.GetProperty("fill_pattern_name").GetString();

        // Get boundary points
        var points = new List<XYZ>();
        foreach (var pt in args.GetProperty("boundary_points").EnumerateArray())
        {
            points.Add(GetXYZ(pt));
        }

        // Close the loop
        if (points.Count > 0 && !points[0].IsAlmostEqualTo(points[points.Count - 1]))
        {
            points.Add(points[0]);
        }

        // Create curve loop
        var curveLoop = new CurveLoop();
        for (int i = 0; i < points.Count - 1; i++)
        {
            curveLoop.Append(Line.CreateBound(points[i], points[i + 1]));
        }

        // Find or create filled region type
        var regionType = FindFilledRegionType(doc, args);
        if (regionType == null)
            return ToolResult.Error("No filled region type found");

        using (var tx = new Transaction(doc, "Place Filled Region"))
        {
            tx.Start();

            var region = FilledRegion.Create(
                doc,
                regionType.Id,
                viewId,
                new List<CurveLoop> { curveLoop }
            );

            tx.Commit();
            return ToolResult.Success($"Created filled region with ID {region.Id.IntegerValue}");
        }
    }

    private FilledRegionType FindFilledRegionType(Document doc, JsonElement args)
    {
        var collector = new FilteredElementCollector(doc)
            .OfClass(typeof(FilledRegionType));

        if (args.TryGetProperty("region_type_name", out var typeName))
        {
            return collector.Cast<FilledRegionType>()
                .FirstOrDefault(t => t.Name == typeName.GetString());
        }

        return collector.Cast<FilledRegionType>().FirstOrDefault();
    }
}
```

### 4. place_masking_region

Create white-out masking areas to hide underlying elements.

```csharp
// Input: {
//   "view_id": 123,
//   "boundary_points": [[0,0], [10,0], [10,10], [0,10]]
// }

public class PlaceMaskingRegionTool : IRevitTool
{
    public string Name => "place_masking_region";
    public string Description => "Create a masking region to hide underlying elements";
    public bool RequiresConfirmation => true;

    public ToolResult Execute(Document doc, JsonElement args)
    {
        // Implementation similar to filled region, but uses masking region type
        // Masking regions have a solid white fill with no pattern

        var viewId = new ElementId(args.GetProperty("view_id").GetInt64());
        var points = ParseBoundaryPoints(args.GetProperty("boundary_points"));
        var curveLoop = CreateCurveLoop(points);

        // Find masking region type (typically named "Masking Region" or similar)
        var maskingType = new FilteredElementCollector(doc)
            .OfClass(typeof(FilledRegionType))
            .Cast<FilledRegionType>()
            .FirstOrDefault(t => t.IsMasking);

        if (maskingType == null)
            return ToolResult.Error("No masking region type found in document");

        using (var tx = new Transaction(doc, "Place Masking Region"))
        {
            tx.Start();
            var region = FilledRegion.Create(doc, maskingType.Id, viewId,
                new List<CurveLoop> { curveLoop });
            tx.Commit();
            return ToolResult.Success($"Created masking region with ID {region.Id.IntegerValue}");
        }
    }
}
```

### 5. place_detail_component

Insert detail families (symbols) like break lines, north arrows, section marks.

```csharp
// Input: {
//   "view_id": 123,
//   "family_name": "Break Line",
//   "type_name": "Break Line - 1/8\"",  // optional, uses default type
//   "location": [10, 5],
//   "rotation": 45  // degrees, optional
// }

public class PlaceDetailComponentTool : IRevitTool
{
    public string Name => "place_detail_component";
    public string Description => "Insert a detail component family (symbol) in a view";
    public bool RequiresConfirmation => true;

    public ToolResult Execute(Document doc, JsonElement args)
    {
        var viewId = new ElementId(args.GetProperty("view_id").GetInt64());
        var view = doc.GetElement(viewId) as View;
        var familyName = args.GetProperty("family_name").GetString();
        var location = GetXYZ(args.GetProperty("location"));

        // Find detail component family
        var symbol = FindDetailSymbol(doc, familyName, args);
        if (symbol == null)
            return ToolResult.Error($"Detail component '{familyName}' not found");

        using (var tx = new Transaction(doc, "Place Detail Component"))
        {
            tx.Start();

            // Ensure symbol is activated
            if (!symbol.IsActive)
                symbol.Activate();

            var instance = doc.Create.NewFamilyInstance(
                location,
                symbol,
                view
            );

            // Apply rotation if specified
            if (args.TryGetProperty("rotation", out var rotElement))
            {
                var angle = rotElement.GetDouble() * Math.PI / 180;
                var axis = Line.CreateBound(location, location + XYZ.BasisZ);
                ElementTransformUtils.RotateElement(doc, instance.Id, axis, angle);
            }

            tx.Commit();
            return ToolResult.Success($"Placed detail component with ID {instance.Id.IntegerValue}");
        }
    }

    private FamilySymbol FindDetailSymbol(Document doc, string familyName, JsonElement args)
    {
        var collector = new FilteredElementCollector(doc)
            .OfClass(typeof(FamilySymbol))
            .OfCategory(BuiltInCategory.OST_DetailComponents)
            .Cast<FamilySymbol>();

        // Filter by family name
        var familySymbols = collector.Where(s =>
            s.FamilyName.Equals(familyName, StringComparison.OrdinalIgnoreCase));

        // Filter by type name if specified
        if (args.TryGetProperty("type_name", out var typeName))
        {
            return familySymbols.FirstOrDefault(s =>
                s.Name.Equals(typeName.GetString(), StringComparison.OrdinalIgnoreCase));
        }

        return familySymbols.FirstOrDefault();
    }
}
```

### 6. place_viewport

Add views to sheets at specified locations.

```csharp
// Input: {
//   "sheet_id": 456,
//   "view_id": 789,
//   "center_point": [1.0, 0.5]  // sheet coordinates in feet
// }

public class PlaceViewportTool : IRevitTool
{
    public string Name => "place_viewport";
    public string Description => "Place a view on a sheet as a viewport";
    public bool RequiresConfirmation => true;

    public ToolResult Execute(Document doc, JsonElement args)
    {
        var sheetId = new ElementId(args.GetProperty("sheet_id").GetInt64());
        var viewId = new ElementId(args.GetProperty("view_id").GetInt64());
        var centerPoint = GetXYZ(args.GetProperty("center_point"));

        var sheet = doc.GetElement(sheetId) as ViewSheet;
        var view = doc.GetElement(viewId) as View;

        if (sheet == null)
            return ToolResult.Error("Sheet not found");
        if (view == null)
            return ToolResult.Error("View not found");

        // Check if view can be placed on sheet
        if (!Viewport.CanAddViewToSheet(doc, sheetId, viewId))
            return ToolResult.Error("View cannot be added to sheet (may already be placed or is a sheet)");

        using (var tx = new Transaction(doc, "Place Viewport"))
        {
            tx.Start();
            var viewport = Viewport.Create(doc, sheetId, viewId, centerPoint);
            tx.Commit();

            return ToolResult.Success(
                $"Placed view '{view.Name}' on sheet '{sheet.SheetNumber}' " +
                $"with viewport ID {viewport.Id.IntegerValue}");
        }
    }
}
```

### 7. place_callout

Create callout annotations that reference detail views.

```csharp
// Input: {
//   "parent_view_id": 123,
//   "callout_type": "detail",  // "detail" or "section"
//   "min_point": [0, 0],
//   "max_point": [10, 10],
//   "reference_view_id": 456   // optional, creates new view if not specified
// }

public class PlaceCalloutTool : IRevitTool
{
    public string Name => "place_callout";
    public string Description => "Create a callout that references a detail or section view";
    public bool RequiresConfirmation => true;

    public ToolResult Execute(Document doc, JsonElement args)
    {
        var parentViewId = new ElementId(args.GetProperty("parent_view_id").GetInt64());
        var parentView = doc.GetElement(parentViewId) as View;
        var calloutType = args.GetProperty("callout_type").GetString();
        var minPt = GetXYZ(args.GetProperty("min_point"));
        var maxPt = GetXYZ(args.GetProperty("max_point"));

        if (parentView == null)
            return ToolResult.Error("Parent view not found");

        using (var tx = new Transaction(doc, "Create Callout"))
        {
            tx.Start();

            View calloutView;
            if (calloutType == "section")
            {
                // Section callout
                var sectionType = GetViewFamilyType(doc, ViewFamily.Section);
                calloutView = ViewSection.CreateCallout(doc, parentViewId, sectionType.Id, minPt, maxPt);
            }
            else
            {
                // Detail callout
                var detailType = GetViewFamilyType(doc, ViewFamily.Detail);
                calloutView = ViewSection.CreateCallout(doc, parentViewId, detailType.Id, minPt, maxPt);
            }

            tx.Commit();

            return ToolResult.Success(
                $"Created {calloutType} callout with view ID {calloutView.Id.IntegerValue}");
        }
    }

    private ViewFamilyType GetViewFamilyType(Document doc, ViewFamily family)
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .FirstOrDefault(vft => vft.ViewFamily == family);
    }
}
```

### 8. create_legend

Create a new legend view.

```csharp
// Input: {
//   "name": "Door Legend",
//   "scale": 96  // 1/8" = 1'-0" (optional, defaults to 1:100)
// }

public class CreateLegendTool : IRevitTool
{
    public string Name => "create_legend";
    public string Description => "Create a new legend view";
    public bool RequiresConfirmation => true;

    public ToolResult Execute(Document doc, JsonElement args)
    {
        var name = args.GetProperty("name").GetString();
        var scale = args.TryGetProperty("scale", out var scaleEl) ? scaleEl.GetInt32() : 100;

        // Find legend view family type
        var legendType = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.Legend);

        if (legendType == null)
            return ToolResult.Error("No legend view type found in document");

        using (var tx = new Transaction(doc, "Create Legend"))
        {
            tx.Start();

            var legend = View.CreateLegend(doc, legendType.Id);
            legend.Name = name;
            legend.Scale = scale;

            tx.Commit();

            return ToolResult.Success($"Created legend '{name}' with ID {legend.Id.IntegerValue}");
        }
    }
}
```

### 9. place_legend_component

Add family type representations to legend views.

```csharp
// Input: {
//   "legend_view_id": 123,
//   "family_name": "Single-Flush",
//   "type_name": "36\" x 84\"",
//   "location": [0.5, 0.5],
//   "view_direction": "plan"  // "plan", "front", "back", "left", "right"
// }

public class PlaceLegendComponentTool : IRevitTool
{
    public string Name => "place_legend_component";
    public string Description => "Add a family type component to a legend view";
    public bool RequiresConfirmation => true;

    public ToolResult Execute(Document doc, JsonElement args)
    {
        var viewId = new ElementId(args.GetProperty("legend_view_id").GetInt64());
        var view = doc.GetElement(viewId) as View;

        if (view == null || view.ViewType != ViewType.Legend)
            return ToolResult.Error("Valid legend view required");

        var familyName = args.GetProperty("family_name").GetString();
        var typeName = args.GetProperty("type_name").GetString();
        var location = GetXYZ(args.GetProperty("location"));

        // Find the family symbol
        var symbol = new FilteredElementCollector(doc)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .FirstOrDefault(s =>
                s.FamilyName.Equals(familyName, StringComparison.OrdinalIgnoreCase) &&
                s.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));

        if (symbol == null)
            return ToolResult.Error($"Family type '{familyName}: {typeName}' not found");

        using (var tx = new Transaction(doc, "Place Legend Component"))
        {
            tx.Start();

            if (!symbol.IsActive)
                symbol.Activate();

            // Legend components use NewFamilyInstance with the legend view
            var instance = doc.Create.NewFamilyInstance(
                location,
                symbol,
                view
            );

            tx.Commit();

            return ToolResult.Success(
                $"Placed '{familyName}: {typeName}' in legend with ID {instance.Id.IntegerValue}");
        }
    }
}
```

### 10. place_revision_cloud

Draw revision markup clouds.

```csharp
// Input: {
//   "view_id": 123,
//   "points": [[0,0], [10,0], [10,5], [0,5]],
//   "revision_id": 789  // optional, uses latest revision if not specified
// }

public class PlaceRevisionCloudTool : IRevitTool
{
    public string Name => "place_revision_cloud";
    public string Description => "Create a revision cloud markup";
    public bool RequiresConfirmation => true;

    public ToolResult Execute(Document doc, JsonElement args)
    {
        var viewId = new ElementId(args.GetProperty("view_id").GetInt64());
        var view = doc.GetElement(viewId) as View;

        if (view == null)
            return ToolResult.Error("View not found");

        // Get revision ID
        ElementId revisionId;
        if (args.TryGetProperty("revision_id", out var revEl))
        {
            revisionId = new ElementId(revEl.GetInt64());
        }
        else
        {
            // Get latest revision
            var revisions = Revision.GetAllRevisionIds(doc);
            if (revisions.Count == 0)
                return ToolResult.Error("No revisions exist in document. Create a revision first.");
            revisionId = revisions.Last();
        }

        // Build curve loop from points
        var points = new List<XYZ>();
        foreach (var pt in args.GetProperty("points").EnumerateArray())
        {
            points.Add(GetXYZ(pt));
        }

        // Close the loop
        if (!points[0].IsAlmostEqualTo(points[points.Count - 1]))
        {
            points.Add(points[0]);
        }

        var curveLoop = new CurveLoop();
        for (int i = 0; i < points.Count - 1; i++)
        {
            curveLoop.Append(Line.CreateBound(points[i], points[i + 1]));
        }

        using (var tx = new Transaction(doc, "Place Revision Cloud"))
        {
            tx.Start();

            var cloud = RevisionCloud.Create(
                doc,
                view,
                revisionId,
                new List<CurveLoop> { curveLoop }
            );

            tx.Commit();

            var revision = doc.GetElement(revisionId) as Revision;
            return ToolResult.Success(
                $"Created revision cloud for '{revision?.Description ?? "Revision"}' " +
                $"with ID {cloud.Id.IntegerValue}");
        }
    }
}
```

---

## Relationship to P2-01 Tools

P2-08 complements the basic drafting tools in P2-01:

| P2-01 Tool | P2-08 Extension |
|------------|-----------------|
| `create_sheet` | `place_viewport` (places views on sheets) |
| `place_detail_line` | `place_detail_arc`, `place_detail_curve` |
| `place_text_note` | N/A (text notes are complete) |
| `place_dimension` | N/A (kept in P2-01) |
| `place_tag` | N/A (kept in P2-01) |

---

## Tool Schema Definitions

```json
{
  "tools": [
    {
      "name": "place_detail_arc",
      "description": "Draw an arc in a drafting or detail view",
      "input_schema": {
        "type": "object",
        "properties": {
          "view_id": { "type": "integer", "description": "ID of drafting/detail view" },
          "center": { "type": "array", "items": { "type": "number" }, "description": "[x, y] center point" },
          "radius": { "type": "number", "description": "Arc radius in feet" },
          "start_angle": { "type": "number", "description": "Start angle in degrees" },
          "end_angle": { "type": "number", "description": "End angle in degrees" },
          "line_style": { "type": "string", "description": "Optional line style name" }
        },
        "required": ["view_id", "center", "radius", "start_angle", "end_angle"]
      }
    },
    {
      "name": "place_detail_curve",
      "description": "Draw a spline or complex curve in a drafting view",
      "input_schema": {
        "type": "object",
        "properties": {
          "view_id": { "type": "integer" },
          "curve_type": { "type": "string", "enum": ["spline", "hermite"] },
          "points": { "type": "array", "items": { "type": "array" }, "description": "Array of [x, y] points" },
          "line_style": { "type": "string" }
        },
        "required": ["view_id", "curve_type", "points"]
      }
    },
    {
      "name": "place_filled_region",
      "description": "Create a filled region with hatching pattern",
      "input_schema": {
        "type": "object",
        "properties": {
          "view_id": { "type": "integer" },
          "boundary_points": { "type": "array", "items": { "type": "array" } },
          "fill_pattern_name": { "type": "string", "description": "Fill pattern (Concrete, Earth, etc.)" },
          "region_type_name": { "type": "string" }
        },
        "required": ["view_id", "boundary_points", "fill_pattern_name"]
      }
    },
    {
      "name": "place_masking_region",
      "description": "Create a masking region to hide underlying elements",
      "input_schema": {
        "type": "object",
        "properties": {
          "view_id": { "type": "integer" },
          "boundary_points": { "type": "array", "items": { "type": "array" } }
        },
        "required": ["view_id", "boundary_points"]
      }
    },
    {
      "name": "place_detail_component",
      "description": "Insert a detail component family (symbol) in a view",
      "input_schema": {
        "type": "object",
        "properties": {
          "view_id": { "type": "integer" },
          "family_name": { "type": "string", "description": "Detail family name (Break Line, North Arrow, etc.)" },
          "type_name": { "type": "string" },
          "location": { "type": "array", "items": { "type": "number" } },
          "rotation": { "type": "number", "description": "Rotation in degrees" }
        },
        "required": ["view_id", "family_name", "location"]
      }
    },
    {
      "name": "place_viewport",
      "description": "Place a view on a sheet as a viewport",
      "input_schema": {
        "type": "object",
        "properties": {
          "sheet_id": { "type": "integer" },
          "view_id": { "type": "integer" },
          "center_point": { "type": "array", "items": { "type": "number" }, "description": "[x, y] on sheet" }
        },
        "required": ["sheet_id", "view_id", "center_point"]
      }
    },
    {
      "name": "place_callout",
      "description": "Create a callout that references a detail or section view",
      "input_schema": {
        "type": "object",
        "properties": {
          "parent_view_id": { "type": "integer" },
          "callout_type": { "type": "string", "enum": ["detail", "section"] },
          "min_point": { "type": "array", "items": { "type": "number" } },
          "max_point": { "type": "array", "items": { "type": "number" } },
          "reference_view_id": { "type": "integer" }
        },
        "required": ["parent_view_id", "callout_type", "min_point", "max_point"]
      }
    },
    {
      "name": "create_legend",
      "description": "Create a new legend view",
      "input_schema": {
        "type": "object",
        "properties": {
          "name": { "type": "string" },
          "scale": { "type": "integer", "description": "View scale (e.g., 96 for 1/8\"=1'-0\")" }
        },
        "required": ["name"]
      }
    },
    {
      "name": "place_legend_component",
      "description": "Add a family type component to a legend view",
      "input_schema": {
        "type": "object",
        "properties": {
          "legend_view_id": { "type": "integer" },
          "family_name": { "type": "string" },
          "type_name": { "type": "string" },
          "location": { "type": "array", "items": { "type": "number" } },
          "view_direction": { "type": "string", "enum": ["plan", "front", "back", "left", "right"] }
        },
        "required": ["legend_view_id", "family_name", "type_name", "location"]
      }
    },
    {
      "name": "place_revision_cloud",
      "description": "Create a revision cloud markup",
      "input_schema": {
        "type": "object",
        "properties": {
          "view_id": { "type": "integer" },
          "points": { "type": "array", "items": { "type": "array" }, "description": "Boundary points" },
          "revision_id": { "type": "integer", "description": "Optional, uses latest revision if omitted" }
        },
        "required": ["view_id", "points"]
      }
    }
  ]
}
```

---

## Verification (Manual)

### Linework Tests
1. Create a drafting view
2. Ask Claude: "Draw an arc with radius 5 feet centered at 10,10"
3. Ask Claude: "Draw a spline through points (0,0), (5,3), (10,0), (15,5)"
4. Verify curves appear correctly

### Region Tests
1. Ask Claude: "Create a filled region with concrete hatch pattern from (0,0) to (10,10)"
2. Ask Claude: "Add a masking region over this area"
3. Verify hatching and masking display correctly

### Symbol Tests
1. Ask Claude: "Insert a break line symbol at location 5,5"
2. Ask Claude: "Place a north arrow rotated 45 degrees"
3. Verify components appear with correct rotation

### Sheet Layout Tests
1. Create a floor plan and a sheet
2. Ask Claude: "Place the Level 1 floor plan on sheet A101 at center"
3. Verify viewport is correctly positioned

### Annotation Tests
1. In a floor plan, ask Claude: "Create a detail callout around the stair"
2. Ask Claude: "Add a revision cloud around these changes"
3. Verify callout creates a new view and revision cloud appears

### Legend Tests
1. Ask Claude: "Create a door legend"
2. Ask Claude: "Add the 36x84 single-flush door to the legend"
3. Verify legend view and component placement

---

## Implementation Notes

### Pattern Discovery
To help users find available fill patterns:

```csharp
// Consider adding a read-only tool: get_fill_patterns
var patterns = new FilteredElementCollector(doc)
    .OfClass(typeof(FillPatternElement))
    .Cast<FillPatternElement>()
    .Select(p => new { p.Name, p.GetFillPattern().IsSolidFill })
    .ToList();
```

### Line Style Discovery
Detail curves can use different line styles:

```csharp
// Consider adding: get_line_styles
var styles = new FilteredElementCollector(doc)
    .OfClass(typeof(GraphicsStyle))
    .Cast<GraphicsStyle>()
    .Where(gs => gs.GraphicsStyleCategory?.Parent?.Id ==
        new ElementId(BuiltInCategory.OST_Lines))
    .Select(gs => gs.Name)
    .ToList();
```

### Revision Workflow
Revision clouds require an active revision. If the document has no revisions, the tool should provide a helpful error message suggesting the user create a revision first (via Revit UI or a future `create_revision` tool).

---

## Future Enhancements

Consider adding these tools in future phases:

- `get_fill_patterns` - List available fill patterns
- `get_line_styles` - List available line styles
- `get_detail_components` - List loaded detail families
- `create_revision` - Create a new revision for tracking changes
- `move_viewport` - Reposition viewport on sheet
- `set_viewport_title` - Configure viewport title display
