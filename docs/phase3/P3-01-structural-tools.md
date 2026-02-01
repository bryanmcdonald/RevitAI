# P3-01: Structural Tool Pack

**Goal**: Add specialized tools for structural engineering workflows.

**Prerequisites**: Phase 2 complete.

**Scope**:
- `place_foundation` - Isolated, strip, mat foundations
- `place_brace` - Diagonal bracing between frames
- `place_truss` - Truss member creation
- `create_structural_connection` - Connection plates, welds
- `analyze_load_path` - Query load path through structure

---

## Key Revit API Areas

- StructuralType enum
- AnalyticalModel access
- Family placement with structural parameters

---

## Key Files to Create

- `src/RevitAI/Tools/StructuralTools/PlaceFoundationTool.cs`
- `src/RevitAI/Tools/StructuralTools/PlaceBraceTool.cs`
- `src/RevitAI/Tools/StructuralTools/PlaceTrussTool.cs`
- `src/RevitAI/Tools/StructuralTools/CreateStructuralConnectionTool.cs`
- `src/RevitAI/Tools/StructuralTools/AnalyzeLoadPathTool.cs`

---

## Implementation Notes

*Detailed implementation to be added when ready for development.*

### place_foundation
```csharp
// Input: { "type": "isolated", "location": [10, 10], "foundation_type": "24x24x12 Footing" }
// Use doc.Create.NewFamilyInstance with structural foundation family
```

### place_brace
```csharp
// Input: { "start": [0, 0, 0], "end": [10, 0, 10], "brace_type": "HSS4x4x1/4" }
// Use doc.Create.NewFamilyInstance with StructuralType.Brace
```

### place_truss
```csharp
// Input: { "start": [0, 0, 10], "end": [30, 0, 10], "truss_type": "Pratt Truss" }
// Use truss family placement or individual member creation
```

### analyze_load_path
```csharp
// Input: { "element_id": 123 }
// Trace structural connections from element down to foundation
// Return path of connected structural elements
```

---

## Verification (Manual)

1. Ask Claude "Place a 24x24 isolated footing at grid A-1"
2. Ask Claude "Add diagonal bracing between these columns"
3. Ask Claude "What is the load path from this beam to the foundation?"
