# P3-04: Architecture Tool Pack

**Goal**: Add tools for architectural workflows.

**Prerequisites**: P3-03 complete.

**Scope**:
- `create_room` - Room/space creation and tagging
- `place_door` - Door placement in walls
- `place_window` - Window placement
- `create_curtain_wall` - Curtain wall systems
- `modify_curtain_panel` - Panel/mullion modifications
- `create_area_plan` - Area plans and color schemes

---

## Key Revit API Areas

- Room, Space classes
- FamilyInstance hosted placement
- CurtainGrid API

---

## Key Files to Create

- `src/RevitAI/Tools/ArchitectureTools/CreateRoomTool.cs`
- `src/RevitAI/Tools/ArchitectureTools/PlaceDoorTool.cs`
- `src/RevitAI/Tools/ArchitectureTools/PlaceWindowTool.cs`
- `src/RevitAI/Tools/ArchitectureTools/CreateCurtainWallTool.cs`
- `src/RevitAI/Tools/ArchitectureTools/ModifyCurtainPanelTool.cs`
- `src/RevitAI/Tools/ArchitectureTools/CreateAreaPlanTool.cs`

---

## Implementation Notes

*Detailed implementation to be added when ready for development.*

### create_room
```csharp
// Input: { "name": "Conference Room", "number": "101", "location": [25, 15] }
// Use doc.Create.NewRoom with phase and level
// Set room name and number parameters
```

### place_door
```csharp
// Input: { "wall_id": 123, "door_type": "Single Flush 36x84", "location": 0.5 }
// Location is fraction along wall (0 to 1)
// Use doc.Create.NewFamilyInstance with host wall
```

### create_curtain_wall
```csharp
// Input: { "start": [0, 0], "end": [30, 0], "type": "Storefront", "level": "Level 1" }
// Create wall with curtain wall type
// Configure grid spacing and mullion types
```

---

## Verification (Manual)

1. Ask Claude "Create a room called 'Conference Room 101' here"
2. Ask Claude "Place a 36" door in this wall"
3. Ask Claude "Create a curtain wall along this line with 5' grid spacing"
