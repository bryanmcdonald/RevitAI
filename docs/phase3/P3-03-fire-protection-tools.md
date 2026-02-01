# P3-03: Fire Protection Tool Pack

**Goal**: Add tools for fire protection engineering.

**Prerequisites**: P3-02 complete.

**Scope**:
- `layout_sprinklers` - Grid-based sprinkler layout
- `identify_fire_rated` - Find fire-rated assemblies
- `analyze_egress` - Egress path analysis
- `identify_hazardous_areas` - Hazmat storage zones
- `calculate_coverage` - Sprinkler coverage analysis

---

## Key Revit API Areas

- MEP fixture placement
- Room/area analysis
- Parameter queries for fire ratings

---

## Key Files to Create

- `src/RevitAI/Tools/FireProtectionTools/LayoutSprinklersTool.cs`
- `src/RevitAI/Tools/FireProtectionTools/IdentifyFireRatedTool.cs`
- `src/RevitAI/Tools/FireProtectionTools/AnalyzeEgressTool.cs`
- `src/RevitAI/Tools/FireProtectionTools/IdentifyHazardousAreasTool.cs`
- `src/RevitAI/Tools/FireProtectionTools/CalculateCoverageTool.cs`

---

## Implementation Notes

*Detailed implementation to be added when ready for development.*

### layout_sprinklers
```csharp
// Input: { "room_id": 123, "spacing": 10, "sprinkler_type": "Pendant" }
// Calculate grid layout within room boundaries
// Place sprinkler fixtures at each grid point
```

### identify_fire_rated
```csharp
// Input: { "minimum_rating": "1-hour" }
// Query walls, floors, doors with Fire Rating parameter
// Return elements meeting or exceeding specified rating
```

### analyze_egress
```csharp
// Input: { "room_id": 123 }
// Find paths from room to exits
// Calculate travel distances
// Identify bottlenecks or code violations
```

---

## Verification (Manual)

1. Ask Claude "Layout sprinklers in this room at 10' spacing"
2. Ask Claude "Find all 2-hour fire rated walls"
3. Ask Claude "What is the egress path from Room 101?"
