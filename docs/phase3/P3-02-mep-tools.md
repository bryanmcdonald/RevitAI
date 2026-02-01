# P3-02: MEP Tool Pack

**Goal**: Add tools for mechanical, electrical, and plumbing workflows.

**Prerequisites**: P3-01 complete.

**Scope**:
- `route_duct` - Auto-route ductwork between points
- `route_pipe` - Auto-route piping
- `route_conduit` - Electrical conduit routing
- `place_equipment` - Mechanical/electrical equipment
- `create_system` - Create and assign MEP systems
- `check_clashes` - Query spatial conflicts

---

## Key Revit API Areas

- MEPCurve, Duct, Pipe, Conduit classes
- Connector API for system connections
- RoutingPreferenceManager

---

## Key Files to Create

- `src/RevitAI/Tools/MEPTools/RouteDuctTool.cs`
- `src/RevitAI/Tools/MEPTools/RoutePipeTool.cs`
- `src/RevitAI/Tools/MEPTools/RouteConduitTool.cs`
- `src/RevitAI/Tools/MEPTools/PlaceEquipmentTool.cs`
- `src/RevitAI/Tools/MEPTools/CreateSystemTool.cs`
- `src/RevitAI/Tools/MEPTools/CheckClashesTool.cs`

---

## Implementation Notes

*Detailed implementation to be added when ready for development.*

### route_duct
```csharp
// Input: { "start": [0, 0, 10], "end": [50, 30, 10], "duct_type": "Rectangular Duct", "size": "12x8" }
// Use RoutingPreferenceManager for automatic routing
// Or create duct segments manually with fittings
```

### route_pipe
```csharp
// Input: { "start": [0, 0, 8], "end": [20, 0, 8], "pipe_type": "Copper", "diameter": 2 }
// Similar approach to duct routing
```

### check_clashes
```csharp
// Input: { "category1": "Ducts", "category2": "Structural Framing" }
// Use BoundingBoxIntersectsFilter to find geometric conflicts
// Return list of clashing element pairs with locations
```

---

## Verification (Manual)

1. Ask Claude "Route a 12x8 duct from the AHU to this diffuser"
2. Ask Claude "Check for clashes between ducts and beams"
3. Ask Claude "Create a supply air system and assign these ducts to it"
