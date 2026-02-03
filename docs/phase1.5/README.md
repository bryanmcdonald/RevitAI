# Phase 1.5: View & Navigation Foundation

> Each chunk represents a 1-2 day development session.
> Phase 1 (through P1-08) must be complete before starting Phase 1.5.

---

## Overview

Phase 1.5 adds foundational view navigation, camera control, and visual context tools. These capabilities enable Claude to capture screenshots for visual analysis, navigate between views, control the camera, and isolate elements for clearer context. These tools are essential prerequisites for the enhanced capabilities in Phase 2.

---

## Chunk Index

| Chunk | Name | Description | Prerequisites | Status |
|-------|------|-------------|---------------|--------|
| [P1.5-01](P1.5-01-screenshot-capture.md) | Screenshot Capture | Capture Revit window for Claude vision analysis | P1-08 | ✅ Complete |
| [P1.5-02](P1.5-02-view-management.md) | View Management | List, switch, open, and create views | P1.5-01 | ✅ Complete |
| [P1.5-03](P1.5-03-camera-control.md) | Camera Control | Zoom, pan, orbit, and view orientation tools | P1.5-02 | ✅ Complete |
| [P1.5-04](P1.5-04-visual-isolation.md) | Visual Isolation | Isolate/hide elements, section boxes, display styles | P1.5-03 | Pending |

---

## Key Files Created in Phase 1.5

```
src/RevitAI/
├── Tools/
│   └── ViewTools/                          # All P1.5 tools
│       ├── CaptureScreenshotTool.cs        # P1.5-01
│       ├── GetViewListTool.cs              # P1.5-02
│       ├── SwitchViewTool.cs               # P1.5-02
│       ├── OpenViewTool.cs                 # P1.5-02
│       ├── CreateFloorPlanViewTool.cs      # P1.5-02
│       ├── Create3DViewTool.cs             # P1.5-02
│       ├── CreateSectionViewTool.cs        # P1.5-02
│       ├── CreateScheduleViewTool.cs       # P1.5-02
│       ├── CreateDraftingViewTool.cs       # P1.5-02
│       ├── ZoomToFitTool.cs                # P1.5-03
│       ├── ZoomToElementsTool.cs           # P1.5-03
│       ├── ZoomToBoundsTool.cs             # P1.5-03
│       ├── ZoomByPercentTool.cs            # P1.5-03
│       ├── PanViewTool.cs                  # P1.5-03
│       ├── OrbitViewTool.cs                # P1.5-03
│       ├── SetViewOrientationTool.cs       # P1.5-03
│       ├── IsolateElementsTool.cs          # P1.5-04
│       ├── HideElementsTool.cs             # P1.5-04
│       ├── ResetVisibilityTool.cs          # P1.5-04
│       ├── Set3DSectionBoxTool.cs          # P1.5-04
│       ├── ClearSectionBoxTool.cs          # P1.5-04
│       └── SetDisplayStyleTool.cs          # P1.5-04
├── Services/
│   └── ScreenCaptureService.cs             # P1.5-01
└── Helpers/
    └── ViewOrientationHelper.cs            # P1.5-03
```

---

## Phase 1.5 Completion Criteria

- [x] Screenshot captures full Revit window and returns base64 image
- [x] Claude can analyze screenshots via vision API
- [x] All view types can be listed with `get_view_list`
- [x] Views can be switched and opened by name or ID
- [x] All 5 view creation tools work (floor plan, 3D, section, schedule, drafting)
- [x] All zoom modes work (fit, elements, bounds, percent)
- [x] Pan works by direction and by centering on element/point
- [x] Orbit works in 3D views (free rotation + preset orientations)
- [ ] Elements can be isolated/hidden (temporary and permanent)
- [ ] 3D section boxes can be created around elements
- [ ] Display styles can be changed

---

## Why Phase 1.5?

These tools are foundational for effective AI interaction but don't fit cleanly into Phase 1 (basic infrastructure) or Phase 2 (enhanced workflows). They enable:

1. **Visual Context**: Claude can "see" what the user sees via screenshots
2. **Navigation**: Claude can focus on relevant parts of the model
3. **Isolation**: Claude can reduce visual noise for clearer analysis
4. **View Creation**: Claude can set up appropriate views for tasks

Without these tools, Claude operates "blind" and relies entirely on text-based queries. With them, Claude can verify its actions visually and navigate the model like a human user would.
