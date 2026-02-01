# P2-06: Parameter & Schedule Tools

**Goal**: Add bulk parameter modification and schedule operations.

**Prerequisites**: P2-05 complete.

**Key Files to Create**:
- `src/RevitAI/Tools/ModifyTools/BulkModifyParametersTool.cs`
- `src/RevitAI/Tools/ReadTools/ReadScheduleDataTool.cs`
- `src/RevitAI/Tools/ModifyTools/ExportElementDataTool.cs`
- `src/RevitAI/Tools/ModifyTools/CreateScheduleTool.cs`

---

## Implementation Details

### 1. bulk_modify_parameters

```csharp
// Input: {
//   "category": "Structural Columns",
//   "filter": { "parameter": "Level", "value": "Level 1" },
//   "modify": { "parameter": "Mark", "value": "COL-L1-{index}" }
// }
public Task<ToolResult> ExecuteAsync(...)
{
    var collector = new FilteredElementCollector(doc)
        .OfCategory(category)
        .WhereElementIsNotElementType();

    // Apply filter
    var filtered = collector.Where(e => MatchesFilter(e, filter));

    int index = 1;
    foreach (var elem in filtered)
    {
        var param = elem.LookupParameter(modifyParam);
        var value = modifyValue.Replace("{index}", index.ToString());
        param.Set(value);
        index++;
    }

    return ToolResult.Ok($"Modified {index - 1} elements");
}
```

### 2. read_schedule_data

```csharp
// Input: { "schedule_name": "Column Schedule" }
public Task<ToolResult> ExecuteAsync(...)
{
    var schedule = new FilteredElementCollector(doc)
        .OfClass(typeof(ViewSchedule))
        .Cast<ViewSchedule>()
        .FirstOrDefault(s => s.Name == scheduleName);

    var tableData = schedule.GetTableData();
    var sectionData = tableData.GetSectionData(SectionType.Body);

    var result = new List<Dictionary<string, string>>();
    for (int row = 0; row < sectionData.NumberOfRows; row++)
    {
        var rowData = new Dictionary<string, string>();
        for (int col = 0; col < sectionData.NumberOfColumns; col++)
        {
            var header = GetHeaderForColumn(col);
            rowData[header] = schedule.GetCellText(SectionType.Body, row, col);
        }
        result.Add(rowData);
    }

    return ToolResult.Ok(JsonSerializer.Serialize(result));
}
```

### 3. export_element_data

```csharp
// Input: { "category": "Walls", "format": "csv", "parameters": ["Type", "Length", "Area"] }
// Export to file or return as string
```

### 4. create_schedule

```csharp
// Input: { "name": "Beam Schedule", "category": "Structural Framing", "fields": ["Type", "Length", "Level"] }
public Task<ToolResult> ExecuteAsync(...)
{
    var schedule = ViewSchedule.CreateSchedule(doc, categoryId);
    schedule.Name = name;

    var definition = schedule.Definition;
    foreach (var fieldName in fields)
    {
        var field = definition.GetSchedulableFields()
            .FirstOrDefault(f => f.GetName(doc) == fieldName);
        if (field != null)
            definition.AddField(field);
    }

    return ToolResult.Ok($"Created schedule: {name}");
}
```

---

## Verification (Manual)

1. Ask Claude "Set the Mark parameter to 'C-{n}' for all columns on Level 1"
2. Ask Claude "What's in the Column Schedule?"
3. Ask Claude "Create a beam schedule showing Type, Length, and Level"
4. Ask Claude "Export all wall data to CSV"
