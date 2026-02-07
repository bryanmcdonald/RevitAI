# P2-01: Advanced Placement Tools

**Goal**: Add tools for grids, levels, dimensions, tags, sheets, and annotations.

**Status**: ✅ Complete

**Prerequisites**: Phase 1.5 complete.

**Key Files Created**:
- `src/RevitAI/Tools/ModifyTools/PlaceLevelTool.cs`
- `src/RevitAI/Tools/ModifyTools/PlaceGridTool.cs`
- `src/RevitAI/Tools/ModifyTools/PlaceDetailLineTool.cs`
- `src/RevitAI/Tools/ModifyTools/PlaceTextNoteTool.cs`
- `src/RevitAI/Tools/ModifyTools/CreateSheetTool.cs`
- `src/RevitAI/Tools/ModifyTools/PlaceTagTool.cs`
- `src/RevitAI/Tools/ModifyTools/PlaceDimensionTool.cs`

**Files Modified**:
- `src/RevitAI/Tools/ModifyTools/Helpers/ElementLookupHelper.cs` — Added lookup methods for title blocks, text note types, dimension types, line styles, and tag types (with category mapping)
- `src/RevitAI/App.cs` — Registered all 7 new tools

> **Note**: `create_section_view` is implemented in Phase 1.5 (P1.5-02) under ViewTools.

---

## Implementation Details

### 1. place_level

- **Input**: `{ "name": "Level 3", "elevation": 30 }`
- **API**: `Level.Create(doc, elevation)` then set `level.Name`
- **Validation**: Checks for duplicate level names; returns available levels on error

### 2. place_grid

- **Input**: `{ "name": "A", "start": [0, 0], "end": [0, 100] }`
- **API**: `Line.CreateBound(start, end)` → `Grid.Create(doc, line)` → set `grid.Name`
- **Validation**: Points must be distinct; catches `ArgumentException` on duplicate names

### 3. place_detail_line

- **Input**: `{ "start": [0, 0], "end": [10, 5], "line_style": "Medium Lines" }`
- **API**: `doc.Create.NewDetailCurve(view, line)` → optionally set `LineStyle` via `GraphicsStyle`
- **Validation**: Rejects 3D views; points must be distinct
- **Supports**: 2D `[x,y]` or 3D `[x,y,z]` coordinates; optional `view_id`

### 4. place_text_note

- **Input**: `{ "location": [5, 5], "text": "VERIFY IN FIELD", "font_size": 12 }`
- **API**: `TextNote.Create(doc, viewId, position, text, typeId)`
- **Font size**: If specified, duplicates the base `TextNoteType` with custom `TEXT_SIZE` parameter (points → feet: `pt / 72 / 12`). Cached via naming convention `"TypeName (Npt)"`.
- **Alignment**: Supports `left`, `center`, `right` via `HorizontalTextAlignment`

### 5. create_sheet

- **Input**: `{ "number": "A101", "name": "Floor Plan", "title_block": "E1 30x42", "views_to_place": [{"view_id": 123}] }`
- **API**: `ViewSheet.Create(doc, tbId)` → set Number/Name → `Viewport.Create()` for each view
- **Validation**: Checks `Viewport.CanAddViewToSheet()` before placing; catches duplicate sheet numbers
- **Activates** title block `FamilySymbol` if not already active

### 6. place_tag

- **Input**: `{ "element_id": 123, "has_leader": false }`
- **API**: `IndependentTag.Create(doc, viewId, reference, addLeader, TagMode.TM_ADDBY_CATEGORY, orientation, position)`
- **Category mapping**: Static `TagCategoryMap` dictionary maps ~15 element categories (OST_Walls, OST_Doors, etc.) to their tag categories
- **Auto-location**: If no `location` specified, uses element bounding box center in the view
- **Validation**: Rejects 3D views; requires tag family loaded for element's category

### 7. place_dimension (V1 — Simplified)

- **Input**: `{ "references": [{"element_id": 100}, {"element_id": 101}], "offset": 3 }`
- **API**: `doc.Create.NewDimension(view, dimLine, refArray, dimType)`
- **Supported elements (V1)**: Grids, Levels, Walls (centerline, exterior face, interior face)
- **Reference types**: `"center"` (default), `"exterior"`, `"interior"` (wall faces only)
- **Dimension line**: Auto-calculated perpendicular to reference direction, offset by configurable distance
- **Wall references**: Uses `Options { ComputeReferences = true }` for stable geometry references; face selection based on `Wall.Orientation` vector sorting

#### V1 Limitations
- Only Grid, Level, and Wall elements supported for dimensioning
- Other element types return an error suggesting manual dimensioning
- Wall face references depend on geometry availability in the active view

---

## ElementLookupHelper Extensions

| Method | Purpose |
|--------|---------|
| `FindTitleBlockType` / `GetAvailableTitleBlockNames` | Title block FamilySymbol lookup via `OST_TitleBlocks` |
| `FindTextNoteType` / `GetAvailableTextNoteTypeNames` | `TextNoteType` lookup by name |
| `FindDimensionType` / `GetAvailableDimensionTypeNames` | `DimensionType` lookup by name |
| `FindLineStyle` / `GetAvailableLineStyleNames` | `GraphicsStyle` from `OST_Lines` subcategories |
| `FindTagTypeForElement` / `GetAvailableTagTypesForCategory` | Tag type lookup via `TagCategoryMap` |
| `TagCategoryMap` | Static dict mapping ~15 element categories to tag categories |

---

## Verification (Manual)

1. "Add Level 3 at 30 feet" → verify in project browser
2. "Create grids A through D at 25' spacing" → verify in plan view
3. "Draw a detail line from (0,0) to (10,5)" → verify in current view
4. "Add note 'VERIFY IN FIELD' at (5,5)" → verify text appears
5. "Create sheet A101 'Floor Plan' with default title block" → verify in project browser
6. Select a wall → "Tag the selected wall" → verify tag appears
7. Place two grids → "Dimension between grid A and grid B" → verify dimension
8. Error cases: Missing params, duplicate names, invalid view types → should return helpful errors
