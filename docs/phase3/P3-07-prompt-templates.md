# P3-07: Custom Prompt Templates

**Goal**: Enable saveable, shareable workflow templates.

**Prerequisites**: P3-06 complete.

**Scope**:
- Template definition format (JSON)
- Template management UI
- Custom system prompts per template
- Tool availability per template
- Team sharing via file export/import

---

## Implementation Notes

- Templates stored in user-accessible folder
- Include sample templates (Column Layout, QC Checker, Drawing Setup)
- Template selection in chat UI

---

## Key Files to Create

- `src/RevitAI/Templates/PromptTemplate.cs`
- `src/RevitAI/Templates/TemplateManager.cs`
- `src/RevitAI/UI/TemplateSelector.xaml`
- `src/RevitAI/UI/TemplateSelectorViewModel.cs`
- `templates/column-layout.json`
- `templates/qc-checker.json`
- `templates/drawing-setup.json`

---

## Template Format

```json
{
  "name": "Column Layout Assistant",
  "description": "Helps with structural column placement and grid alignment",
  "systemPrompt": "You are a structural engineering assistant specializing in column layout...",
  "enabledTools": [
    "get_grids",
    "place_column",
    "get_levels",
    "array_elements"
  ],
  "defaultContext": {
    "category": "Structural Columns"
  }
}
```

---

## Implementation Details

> *This is a preliminary outline. Detailed implementation will be added during the chunk planning session.*

### 1. PromptTemplate Model

```csharp
public class PromptTemplate
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string SystemPrompt { get; set; }
    public List<string> EnabledTools { get; set; }
    public Dictionary<string, string> DefaultContext { get; set; }
    public string Author { get; set; }
    public DateTime CreatedDate { get; set; }
}
```

### 2. TemplateManager

```csharp
public class TemplateManager
{
    private readonly string _templatesDir;

    public List<PromptTemplate> GetAvailableTemplates()
    {
        var templates = new List<PromptTemplate>();
        foreach (var file in Directory.GetFiles(_templatesDir, "*.json"))
        {
            var json = File.ReadAllText(file);
            var template = JsonSerializer.Deserialize<PromptTemplate>(json);
            if (template != null)
                templates.Add(template);
        }
        return templates;
    }

    public void SaveTemplate(PromptTemplate template)
    {
        var path = Path.Combine(_templatesDir, $"{SanitizeFileName(template.Name)}.json");
        var json = JsonSerializer.Serialize(template, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    public void ExportTemplate(PromptTemplate template, string exportPath)
    {
        var json = JsonSerializer.Serialize(template, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(exportPath, json);
    }

    public PromptTemplate ImportTemplate(string importPath)
    {
        var json = File.ReadAllText(importPath);
        return JsonSerializer.Deserialize<PromptTemplate>(json);
    }
}
```

### 3. Template Selector UI

```xaml
<UserControl>
  <StackPanel>
    <Label Content="Select Workflow Template:"/>
    <ComboBox ItemsSource="{Binding Templates}"
              SelectedItem="{Binding SelectedTemplate}"
              DisplayMemberPath="Name"/>

    <TextBlock Text="{Binding SelectedTemplate.Description}"
               TextWrapping="Wrap" Margin="0,10,0,0"/>

    <StackPanel Orientation="Horizontal" Margin="0,10,0,0">
      <Button Content="Apply" Command="{Binding ApplyCommand}"/>
      <Button Content="Create New" Command="{Binding CreateCommand}"/>
      <Button Content="Export" Command="{Binding ExportCommand}"/>
      <Button Content="Import" Command="{Binding ImportCommand}"/>
    </StackPanel>
  </StackPanel>
</UserControl>
```

### 4. Sample Templates

**Column Layout Assistant**
```json
{
  "name": "Column Layout Assistant",
  "description": "Helps with structural column placement and grid alignment",
  "systemPrompt": "You are a structural engineering assistant specializing in column layout. Focus on grid alignment, spacing consistency, and structural efficiency. Always verify column types match the structural requirements.",
  "enabledTools": ["get_grids", "place_column", "get_levels", "array_elements", "align_elements"],
  "defaultContext": { "category": "Structural Columns" }
}
```

**QC Checker**
```json
{
  "name": "Model QC Checker",
  "description": "Reviews model for common issues and warnings",
  "systemPrompt": "You are a model quality control assistant. Systematically check for warnings, duplicate elements, unhosted elements, and model health issues. Provide clear reports and suggest fixes.",
  "enabledTools": ["get_warnings", "find_overlapping_elements", "find_duplicate_instances", "analyze_model_size"],
  "defaultContext": {}
}
```

**Drawing Setup**
```json
{
  "name": "Drawing Setup Assistant",
  "description": "Helps create sheets, place views, and add annotations",
  "systemPrompt": "You are a drafting assistant. Help set up sheets, place views at appropriate scales, and add annotations. Follow standard drafting conventions.",
  "enabledTools": ["create_sheet", "get_view_info", "place_tag", "place_dimension", "place_text_note"],
  "defaultContext": {}
}
```

---

## Verification (Manual)

1. Open template selector, choose "Column Layout Assistant"
2. Verify system prompt changes and only relevant tools are available
3. Create a new custom template
4. Export template and share with another user
5. Import a template from file
