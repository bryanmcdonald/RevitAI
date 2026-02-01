# P2-05: Visual Feedback System

**Goal**: Add temporary highlighting, preview graphics, and status bar integration.

**Prerequisites**: P2-04 complete.

**Key Files to Create**:
- `src/RevitAI/UI/HighlightService.cs`
- `src/RevitAI/UI/PreviewGraphics.cs`
- `src/RevitAI/UI/StatusBarService.cs`

---

## Implementation Details

### 1. Temporary Element Highlighting

```csharp
public class HighlightService
{
    private readonly Dictionary<ElementId, OverrideGraphicSettings> _originalSettings = new();

    public void HighlightElements(Document doc, IEnumerable<ElementId> elementIds, Color color)
    {
        var view = doc.ActiveView;
        var settings = new OverrideGraphicSettings();
        settings.SetProjectionLineColor(color);
        settings.SetProjectionLineWeight(5);

        foreach (var id in elementIds)
        {
            _originalSettings[id] = view.GetElementOverrides(id);
            view.SetElementOverrides(id, settings);
        }
    }

    public void ClearHighlights(Document doc)
    {
        var view = doc.ActiveView;
        foreach (var (id, original) in _originalSettings)
        {
            view.SetElementOverrides(id, original);
        }
        _originalSettings.Clear();
    }
}
```

### 2. Preview Graphics

Using DirectContext3D or temporary lines.

```csharp
public class PreviewGraphics : IDirectContext3DServer
{
    private List<XYZ> _previewPoints = new();
    private List<Line> _previewLines = new();

    public void ShowWallPreview(XYZ start, XYZ end, double height)
    {
        _previewLines.Clear();
        // Create 3D box outline for wall preview
        _previewLines.Add(Line.CreateBound(start, end));
        // ... add other edges
        Refresh();
    }

    public void RenderScene(View view, DisplayStyle displayStyle)
    {
        // Draw preview lines using DirectContext3D
    }
}
```

### 3. Status Bar Integration

```csharp
public class StatusBarService
{
    public void SetStatus(string message)
    {
        // Revit doesn't have direct status bar API
        // Option 1: Update a status label in the chat pane
        // Option 2: Use TaskDialog for important status (not recommended for frequent updates)
    }
}
```

### 4. Integration with Tool Execution

```csharp
// Before execution: show preview
_previewGraphics.ShowWallPreview(start, end, height);

// After confirmation: execute and clear preview
_previewGraphics.Clear();
await ExecuteToolAsync(...);

// After execution: highlight created elements
_highlightService.HighlightElements(doc, new[] { newElementId }, Colors.Green);
await Task.Delay(2000);
_highlightService.ClearHighlights(doc);
```

---

## Verification (Manual)

1. Ask Claude to describe where it would place a wall
2. Verify preview graphics appear
3. After element creation, verify temporary green highlight
4. Verify highlights clear after a few seconds
