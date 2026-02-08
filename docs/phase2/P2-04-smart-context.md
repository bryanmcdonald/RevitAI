# P2-04: Smart Context Awareness

**Goal**: Add intelligent interpretation of relative positions, grid references, and type inference so users can interact naturally ("place a column at grid A-1" instead of providing raw coordinates).

**Prerequisites**: P2-03 complete.

**Status**: ✅ Complete

**Key Files Created/Modified**:
- `src/RevitAI/Services/GeometryResolver.cs` (new)
- `src/RevitAI/Tools/ReadTools/ResolveGridIntersectionTool.cs` (new)
- `src/RevitAI/Tools/ModifyTools/Helpers/ElementLookupHelper.cs` (modified)
- `src/RevitAI/Models/RevitContext.cs` (modified)
- `src/RevitAI/Services/ContextEngine.cs` (modified)
- `src/RevitAI/Tools/ModifyTools/PlaceColumnTool.cs` (modified)
- `src/RevitAI/Tools/ModifyTools/PlaceBeamTool.cs` (modified)
- `src/RevitAI/Tools/ModifyTools/PlaceWallTool.cs` (modified)
- `src/RevitAI/Tools/ModifyTools/PlaceFloorTool.cs` (modified)
- `src/RevitAI/App.cs` (modified - register new tool)

---

## Implementation Details

### 1. GeometryResolver (New Static Utility)

**File**: `src/RevitAI/Services/GeometryResolver.cs`

Three static methods:

- **`ResolveGridIntersection(doc, grid1Name, grid2Name)`** - Finds two grids by name (case-insensitive), computes mathematical 2D line intersection using parametric form (not `Curve.Intersect` which has issues with bounded grid extents). Returns XYZ with Z=0.

- **`ResolveRelativePosition(doc, referenceElementId, direction, distanceFeet)`** - Gets element location (LocationPoint -> point, LocationCurve -> midpoint), maps direction to offset vector (east=+X, west=-X, north=+Y, south=-Y, up=+Z, down=-Z).

- **`GetGridNamesByOrientation(doc)`** - Classifies all grids by line direction angle (< 45° from X-axis = horizontal/east-west, >= 45° = vertical/north-south).

### 2. Fuzzy Type Matching (ElementLookupHelper Enhancement)

**File**: `src/RevitAI/Tools/ModifyTools/Helpers/ElementLookupHelper.cs`

Added private `LevenshteinDistance` helper and three new fuzzy lookup methods:

- **`FindFamilySymbolInCategoryFuzzy`** - Search order: exact match -> partial contains -> Levenshtein distance (threshold = max(2, input.Length / 3))
- **`FindWallTypeByNameFuzzy`** - Same strategy for WallType
- **`FindFloorTypeByNameFuzzy`** - Same strategy for FloorType

Each returns `(Type?, bool IsFuzzy, string? MatchedName)` tuple. Existing non-fuzzy methods remain unchanged for backward compatibility.

### 3. Grid Context in System Prompt

**File**: `src/RevitAI/Models/RevitContext.cs` — Added `GridSummary` class and `GridInfo` property.

**File**: `src/RevitAI/Services/ContextEngine.cs` — At verbosity >= 2, extracts grid names by orientation and includes a "Grid Layout" section in the system prompt showing horizontal and vertical grid names. Also added "Smart Placement" tool usage notes.

### 4. Updated Placement Tools

All four placement tools now support:

| Feature | `place_column` | `place_beam` | `place_wall` | `place_floor` |
|---------|:-:|:-:|:-:|:-:|
| Grid intersection params | `grid_intersection` | `start/end_grid_intersection` | `start/end_grid_intersection` | — (use resolve tool) |
| Relative position | `relative_to` | — | — | — |
| Optional level (infer from view) | ✅ | ✅ | ✅ | ✅ |
| Fuzzy type matching | ✅ | ✅ | ✅ | ✅ |

**Priority for location resolution**: `grid_intersection` > `relative_to` > raw coordinates.

**Level inference**: If no level is specified, tools check if the active view is a ViewPlan and use its `GenLevel`. If not a plan view, returns an error asking for explicit level.

**Fuzzy match reporting**: When a fuzzy match is used, the tool result includes `fuzzy_matched` field and a note in the message (e.g., "Type fuzzy-matched to 'W-Wide Flange-Column: W10x49'").

### 5. ResolveGridIntersectionTool (New Read Tool)

**File**: `src/RevitAI/Tools/ReadTools/ResolveGridIntersectionTool.cs`

Lightweight read-only tool (`resolve_grid_intersection`) for the AI to explicitly resolve grid intersections to coordinates. Returns `{x, y, grid1, grid2, message}`. Useful for:
- `place_floor` boundaries (boundary arrays don't fit the grid_intersection pattern)
- Multi-step planning where the AI needs to know coordinates before acting

### Design Decisions

1. **Mathematical 2D intersection vs Curve.Intersect**: Grid curves in Revit are bounded — `Curve.Intersect` only finds intersections within the drawn extent. Mathematical intersection of unbounded lines always works.

2. **No separate TypeResolver class**: Fuzzy matching was added directly to `ElementLookupHelper` since it already handles all type lookups. Adding a separate class would fragment the lookup logic.

3. **No grid_intersection on place_floor**: Floor boundaries are point arrays, not pairs of endpoints. The AI should use `resolve_grid_intersection` to get coordinates first, then pass them as boundary points.

4. **Levenshtein threshold scaling**: `max(2, length/3)` balances between allowing reasonable typos (e.g., "W10x4" matching "W10x49") and preventing wild mismatches on short strings.

---

## Verification (Manual)

1. **Grid intersection**: "Place a column at grid A-1" → column appears at correct intersection
2. **Relative position**: "Add a column 3 feet east of the selected column" → column offset correctly
3. **Fuzzy type**: "Use a W10x49 column" → resolves to full "W-Wide Flange-Column: W10x49"
4. **Level inference**: In Level 2 plan, place element without specifying level → defaults to Level 2
5. **Grid context**: At verbosity 2, system prompt shows grid names
6. **Resolve tool**: "What are the coordinates of grid A-1?" → uses resolve_grid_intersection
7. **Beam grid endpoints**: "Place a beam from grid A/1 to grid A/3" → resolves both endpoints from grids
8. **Wall grid endpoints**: "Place a wall from grid 1/A to grid 1/D" → resolves both endpoints
9. **Error cases**: Non-existent grid names, parallel grids, elements without locations → clear error messages
