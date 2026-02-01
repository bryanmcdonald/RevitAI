# P3-05: Export & Reporting Tools

**Goal**: Add document export and report generation.

**Prerequisites**: P3-04 complete.

**Scope**:
- `export_view_to_image` - PNG/JPG export
- `print_sheets` - Batch PDF printing
- `export_to_ifc` - IFC export
- `export_to_dwg` - DWG export
- `generate_report` - Custom text/HTML reports

---

## Key Revit API Areas

- ImageExportOptions
- PrintManager
- IFCExportOptions
- DWGExportOptions

---

## Key Files to Create

- `src/RevitAI/Tools/ExportTools/ExportViewToImageTool.cs`
- `src/RevitAI/Tools/ExportTools/PrintSheetsTool.cs`
- `src/RevitAI/Tools/ExportTools/ExportToIfcTool.cs`
- `src/RevitAI/Tools/ExportTools/ExportToDwgTool.cs`
- `src/RevitAI/Tools/ExportTools/GenerateReportTool.cs`

---

## Implementation Details

### 1. export_view_to_image

```csharp
// Input: { "view_id": 123, "format": "png", "output_path": "C:\\exports\\view.png", "resolution": 300 }
public Task<ToolResult> ExecuteAsync(...)
{
    var view = doc.GetElement(new ElementId(viewId)) as View;
    var options = new ImageExportOptions
    {
        ExportRange = ExportRange.SetOfViews,
        FilePath = outputPath,
        ImageResolution = resolution == 300 ? ImageResolution.DPI_300 : ImageResolution.DPI_150,
        PixelSize = 1920,
        FitDirection = FitDirectionType.Horizontal
    };
    options.SetViewsAndSheets(new List<ElementId> { view.Id });
    doc.ExportImage(options);
    return ToolResult.Ok($"Exported view to {outputPath}");
}
```

### 2. print_sheets

```csharp
// Input: { "sheet_numbers": ["A101", "A102"], "printer": "Microsoft Print to PDF", "output_folder": "C:\\prints" }
public Task<ToolResult> ExecuteAsync(...)
{
    var printManager = doc.PrintManager;
    printManager.SelectNewPrintDriver(printerName);
    printManager.PrintRange = PrintRange.Select;
    // Configure and print each sheet
}
```

### 3. export_to_ifc

```csharp
// Input: { "output_path": "C:\\exports\\model.ifc", "version": "IFC4" }
public Task<ToolResult> ExecuteAsync(...)
{
    var options = new IFCExportOptions();
    options.FileVersion = version == "IFC4" ? IFCVersion.IFC4 : IFCVersion.IFC2x3;
    doc.Export(Path.GetDirectoryName(outputPath), Path.GetFileName(outputPath), options);
    return ToolResult.Ok($"Exported to {outputPath}");
}
```

### 4. export_to_dwg

```csharp
// Input: { "view_ids": [123, 456], "output_folder": "C:\\exports", "version": "AutoCAD2018" }
public Task<ToolResult> ExecuteAsync(...)
{
    var options = new DWGExportOptions();
    options.FileVersion = ACADVersion.R2018;
    options.ExportOfSolids = SolidGeometry.ACIS;
    options.LayerMapping = "AIA"; // or custom mapping file

    var viewIds = viewIdsList.Select(id => new ElementId(id)).ToList();
    doc.Export(outputFolder, "export", viewIds, options);
    return ToolResult.Ok($"Exported {viewIds.Count} views to DWG");
}
```

### 5. generate_report

```csharp
// Input: { "category": "Structural Columns", "format": "html", "output_path": "C:\\reports\\columns.html" }
public Task<ToolResult> ExecuteAsync(...)
{
    var elements = new FilteredElementCollector(doc)
        .OfCategory(category)
        .WhereElementIsNotElementType()
        .ToElements();

    var report = format == "html" ? GenerateHtmlReport(elements) : GenerateTextReport(elements);
    File.WriteAllText(outputPath, report);
    return ToolResult.Ok($"Generated report with {elements.Count} elements");
}
```

---

## Verification (Manual)

1. Ask Claude "Export the current view as a PNG"
2. Ask Claude "Print sheets A101 through A105 to PDF"
3. Ask Claude "Export the model to IFC4 format"
4. Ask Claude "Generate a report of all structural columns"
