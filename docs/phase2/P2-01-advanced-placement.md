# P2-01: Advanced Placement Tools

**Goal**: Add tools for grids, levels, dimensions, tags, sections, sheets, and annotations.

**Prerequisites**: Phase 1 complete.

**Key Files to Create**:
- `src/RevitAI/Tools/ModifyTools/PlaceGridTool.cs`
- `src/RevitAI/Tools/ModifyTools/PlaceLevelTool.cs`
- `src/RevitAI/Tools/ModifyTools/PlaceDimensionTool.cs`
- `src/RevitAI/Tools/ModifyTools/PlaceTagTool.cs`
- `src/RevitAI/Tools/ModifyTools/CreateSectionViewTool.cs`
- `src/RevitAI/Tools/ModifyTools/CreateSheetTool.cs`
- `src/RevitAI/Tools/ModifyTools/PlaceDetailLineTool.cs`
- `src/RevitAI/Tools/ModifyTools/PlaceTextNoteTool.cs`

---

## Implementation Details

### 1. place_grid

```csharp
// Input: { "name": "A", "start": [0, 0], "end": [0, 100] }
// Use Grid.Create(doc, line)
// Set grid name parameter
```

### 2. place_level

```csharp
// Input: { "name": "Level 3", "elevation": 20 }
// Use Level.Create(doc, elevation)
```

### 3. place_dimension

```csharp
// Input: { "references": [{"element_id": 123, "face": "left"}, ...], "view_id": 456 }
// Create ReferenceArray, use doc.Create.NewDimension
```

### 4. place_tag

```csharp
// Input: { "element_id": 123, "tag_type": "Wall Tag", "location": [10, 10] }
// Use IndependentTag.Create
```

### 5. create_section_view

```csharp
// Input: { "name": "Section A", "head": [0, 0, 0], "tail": [20, 0, 0], "up": [0, 0, 1] }
// Create BoundingBoxXYZ, use ViewSection.CreateSection
```

### 6. create_sheet

```csharp
// Input: { "number": "A101", "name": "Floor Plan", "title_block": "E1 30x42" }
// Use ViewSheet.Create, optionally place views
```

### 7. place_detail_line

```csharp
// Input: { "view_id": 123, "start": [0, 0], "end": [10, 10] }
// Use doc.Create.NewDetailCurve
```

### 8. place_text_note

```csharp
// Input: { "view_id": 123, "location": [5, 5], "text": "Note: Verify in field" }
// Use TextNote.Create
```

---

## Verification (Manual)

1. Ask Claude "Create a grid system: A through D at 25' spacing"
2. Ask Claude "Add Level 3 at 20 feet elevation"
3. Ask Claude "Create a section through the building at grid B"
4. Ask Claude "Place a tag on the selected wall"
5. Verify all operations work correctly
