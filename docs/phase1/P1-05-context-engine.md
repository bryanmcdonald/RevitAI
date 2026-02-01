# P1-05: Context Engine

**Goal**: Track Revit context (selection, view, level, project) and inject it into Claude prompts.

**Prerequisites**: P1-04 complete.

**Key Files to Create**:
- `src/RevitAI/Services/ContextEngine.cs`
- `src/RevitAI/Models/RevitContext.cs`

---

## Implementation Details

### 1. RevitContext Model

```csharp
public class RevitContext
{
    public ViewInfo? ActiveView { get; set; }
    public LevelInfo? ActiveLevel { get; set; }
    public List<ElementInfo> SelectedElements { get; set; } = new();
    public ProjectInfo? Project { get; set; }
    public Dictionary<string, List<string>> AvailableTypes { get; set; } = new();
}

public class ElementInfo
{
    public long Id { get; set; }
    public string Category { get; set; }
    public string TypeName { get; set; }
    public Dictionary<string, string> KeyParameters { get; set; }
}
```

### 2. ContextEngine

Gathers context on demand.

```csharp
public class ContextEngine
{
    public RevitContext GatherContext(UIApplication app)
    {
        var doc = app.ActiveUIDocument?.Document;
        var uidoc = app.ActiveUIDocument;

        var context = new RevitContext();

        // Active view
        if (uidoc?.ActiveView != null)
        {
            context.ActiveView = new ViewInfo
            {
                Name = uidoc.ActiveView.Name,
                ViewType = uidoc.ActiveView.ViewType.ToString(),
                // ...
            };
        }

        // Selected elements
        if (uidoc?.Selection != null)
        {
            foreach (var id in uidoc.Selection.GetElementIds())
            {
                var elem = doc.GetElement(id);
                context.SelectedElements.Add(ExtractElementInfo(elem));
            }
        }

        // Project info, levels, etc.
        return context;
    }
}
```

### 3. Selection Changed Event (optional for live updates)

```csharp
app.SelectionChanged += (sender, args) =>
{
    // Update context display in UI
};
```

### 4. Inject Context into System Prompt

With configurable verbosity. **Important**: Element IDs are always included at all verbosity levels so Claude can reference selected elements with tools (move, delete, modify, etc.).

```csharp
private string BuildSystemPrompt(RevitContext context, ContextVerbosity verbosity)
{
    var sb = new StringBuilder();
    sb.AppendLine("You are RevitAI, an assistant embedded in Autodesk Revit.");
    sb.AppendLine();
    sb.AppendLine("## Current Context");
    sb.AppendLine($"Active View: {context.ActiveView?.Name} ({context.ActiveView?.ViewType})");
    sb.AppendLine($"Active Level: {context.ActiveLevel?.Name}");

    // Element IDs always included (required for tool use)
    if (context.SelectedElements.Any())
    {
        sb.AppendLine("### Selected Elements:");
        foreach (var elem in context.SelectedElements.Take(20))
        {
            sb.AppendLine($"  - [{elem.Id}] {elem.Category}: {elem.TypeName}");
        }
    }

    if (verbosity == ContextVerbosity.Minimal)
        return sb.ToString();

    // Standard: Add element levels and ALL parameters (up to 200)
    if (context.SelectedElements.Any())
    {
        sb.AppendLine("### Selected Element Details:");
        foreach (var elem in context.SelectedElements.Take(20))
        {
            sb.AppendLine($"  [{elem.Id}] {elem.Category}: {elem.TypeName}");
            sb.AppendLine($"    Level: {elem.Level}");
            foreach (var param in elem.KeyParameters.Take(200))
            {
                sb.AppendLine($"    - {param.Key}: {param.Value}");
            }
        }
    }

    if (verbosity == ContextVerbosity.Standard)
        return sb.ToString();

    // Detailed: Add project info and available types
    sb.AppendLine($"Project: {context.Project?.Name} ({context.Project?.Number})");

    sb.AppendLine("### Available Types:");
    foreach (var category in context.AvailableTypes.Take(5))
    {
        sb.AppendLine($"  {category.Key}: {string.Join(", ", category.Value.Take(5))}");
    }

    return sb.ToString();
}
```

**Verbosity Level Summary:**

| Level | Includes |
|-------|----------|
| Minimal | View, level, element IDs with category/type |
| Standard | Above + element levels and ALL parameters (up to 200 per element) |
| Detailed | Above + project info and available family types |

---

## Verification (Manual)

1. Build and deploy
2. Open a Revit project with elements
3. Select some elements (walls, columns, etc.)
4. Open chat, send a message
5. Check console/debug output to verify context is being gathered
6. Ask Claude "What do I have selected?" - verify accurate response
