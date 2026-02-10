# RevitAI

A Revit plugin that embeds an AI-powered conversational assistant directly into the Revit interface. Supports **Claude** (Anthropic) and **Google Gemini** as AI providers. Query model information, place and modify elements, and automate tasks using natural language.

## Table of Contents

- [Overview](#overview)
- [Current Status](#current-status)
- [Requirements](#requirements)
- [Installation](#installation)
  - [Quick Install](#quick-install)
  - [Manual Build](#manual-build)
- [Usage](#usage)
  - [Opening the Chat Panel](#opening-the-chat-panel)
  - [Configuring Settings](#configuring-settings)
  - [Example Queries](#example-queries)
  - [Context Awareness](#context-awareness)
- [Available Tools](#available-tools)
- [Development Status](#development-status)
  - [Phase 1: Foundation (MVP)](#phase-1-foundation-mvp)
  - [Phase 1.5: View & Navigation Foundation](#phase-15-view--navigation-foundation)
  - [Phase 2: Enhanced Capabilities](#phase-2-enhanced-capabilities)
  - [Phase 3: Advanced & Multi-Discipline](#phase-3-advanced--multi-discipline)
  - [Phase 4: Agentic Mode](#phase-4-agentic-mode)
- [Project Structure](#project-structure)
- [Contributing](#contributing)
- [Troubleshooting](#troubleshooting)
- [Known Issues & Limitations](#known-issues--limitations)
- [Team Deployment](#team-deployment)
- [License](#license)
- [Acknowledgments](#acknowledgments)

## Overview

RevitAI provides a dockable chat panel where you interact with AI (Claude or Gemini) to:
- **Query your model** - Get element counts, properties, levels, grids, warnings, and more
- **Understand context** - The AI sees your current selection, active view, and level
- **Automate tasks** - Place elements, modify parameters, and perform multi-step operations (coming soon)

Built for multi-discipline engineering teams (Structural, MEP, Fire Protection, Architecture) working in Revit 2026.

## Current Status

**Ready for Phase 2** (Phase 1 + 1.5 Foundation complete)

| Phase | Status |
|-------|--------|
| Phase 1 Foundation (10 chunks) | ✅ Complete |
| Phase 1.5 View & Navigation (4 chunks) | ✅ Complete |
| Phase 2 Enhanced Capabilities | 🔜 Next |

See [Development Status](#development-status) for full details.

## Requirements

- **Revit 2026** (required - uses .NET 8)
- **Windows 10/11** (64-bit)
- **AI Provider API Key** - one of:
  - **Anthropic API Key** for Claude ([Get one here](https://console.anthropic.com/))
  - **Google Gemini API Key** ([Get one here](https://aistudio.google.com/))

## Installation

### Quick Install

1. **Download the latest release** from the [Releases](../../releases) page
2. **Extract** the ZIP file
3. **Copy files** to your Revit addins folder:
   ```
   %AppData%\Autodesk\Revit\Addins\2026\
   ```
   Copy these files:
   - `RevitAI.addin`
   - `RevitAI.dll`
   - All supporting DLLs (CommunityToolkit.Mvvm.dll, Markdig.dll, etc.)

4. **Start Revit 2026**
5. **Configure API Key**: Click the gear icon in the RevitAI panel, select your AI provider (Claude or Gemini), and enter your API key

### Manual Build

If you want to build from source:

1. **Clone the repository**
   ```bash
   git clone https://github.com/bryanmcdonald/RevitAI.git
   cd RevitAI
   ```

2. **Open in Visual Studio 2022** (17.8 or later with .NET 8 SDK)
   ```
   RevitAI.sln
   ```

3. **Ensure Revit 2026 is installed** - The project references:
   - `C:\Program Files\Autodesk\Revit 2026\RevitAPI.dll`
   - `C:\Program Files\Autodesk\Revit 2026\RevitAPIUI.dll`

4. **Build the solution** (Release configuration recommended)
   - Files are automatically deployed to `%AppData%\Autodesk\Revit\Addins\2026\`

5. **Start Revit 2026** and look for the RevitAI tab

## Usage

### Opening the Chat Panel

1. In Revit, go to the **Add-Ins** tab
2. Click **Show RevitAI Chat** or find RevitAI in the dockable panels
3. The chat panel will dock to the side of your Revit window

### Configuring Settings

Click the **gear icon** in the chat panel to:
- Choose your AI provider (Claude or Google Gemini)
- Enter your API key (stored securely encrypted via DPAPI)
- Select the model (Claude: sonnet/opus/haiku; Gemini: gemini-3-pro-preview)
- Adjust temperature, max tokens, and context verbosity level

### Example Queries

```
"What elements are selected?"

"Show me all the levels in this project"

"What wall types are available?"

"List all structural columns on Level 1"

"What warnings are in this model?"

"Get the properties of element 12345"

"How many doors are on each level?"
```

### Context Awareness

RevitAI automatically provides Claude with context about your Revit session. The level of detail is configurable via the Settings dialog:

| Verbosity Level | What Claude Sees |
|-----------------|------------------|
| **Minimal** | Active view, level, and selected element IDs with category/type |
| **Standard** | Above + element level associations and all parameters (up to 200 per element, max 20 elements) |
| **Detailed** | Above + project info and available family types |

Element IDs are always included so Claude can reference selected elements with modification tools (move, delete, etc.).

This means you can ask contextual questions like:
- "What is selected?" (Claude sees your current selection)
- "What level am I on?" (Claude sees your active view's level)
- "Move the selected wall 5 feet north" (Claude has the element ID)

## Available Tools

RevitAI provides Claude with tools to query your Revit model:

| Tool | Description |
|------|-------------|
| `get_selected_elements` | Details of currently selected elements |
| `get_element_properties` | All parameters for a specific element |
| `get_elements_by_category` | List elements by category (walls, columns, etc.) |
| `get_levels` | All levels with elevations |
| `get_grids` | Grid lines with geometry, orientation, and angle |
| `get_view_info` | Active view details |
| `get_project_info` | Project metadata |
| `get_available_types` | Loaded family types by category |
| `get_warnings` | Model warnings and errors |
| `get_room_info` | Room boundaries and areas |
| `get_element_quantity_takeoff` | Element counts and summaries |
| `resolve_grid_intersection` | Get [x,y] coordinates where two grids intersect |
| `select_elements` | Select elements by ID |
| `zoom_to_element` | Zoom view to elements |
| `move_element` | Move element by translation vector |
| `delete_elements` | Delete elements by ID |
| `modify_element_parameter` | Change element parameter values |
| `change_element_type` | Change element to different type |
| `place_wall` | Create wall between two points |
| `place_column` | Place structural column |
| `place_beam` | Place structural beam |
| `place_floor` | Create floor from boundary |
| `place_level` | Create level at elevation |
| `place_grid` | Create grid line between two points |
| `place_detail_line` | Draw detail line in a view |
| `place_text_note` | Place text note annotation |
| `create_sheet` | Create sheet with title block and optional views |
| `place_tag` | Tag an element by category |
| `place_dimension` | Dimension between grids, levels, or walls |
| `capture_screenshot` | Capture Revit window or active view for Claude vision analysis |
| `get_view_list` | List all views with optional type filtering |
| `switch_view` | Switch active view by ID |
| `open_view` | Open view by name (partial match) |
| `create_floor_plan_view` | Create floor plan for a level |
| `create_ceiling_plan_view` | Create ceiling plan for a level |
| `create_3d_view` | Create 3D view with orientation preset |
| `create_section_view` | Create section at location/direction |
| `create_elevation_view` | Create elevation at location |
| `create_schedule_view` | Create schedule with fields |
| `create_drafting_view` | Create blank drafting view |
| `duplicate_view` | Duplicate view with options |
| `rename_view` | Rename an existing view |
| `delete_view` | Delete a view (requires confirmation) |
| `zoom_to_fit` | Zoom view to fit all visible content |
| `zoom_to_elements` | Zoom view to frame specific elements |
| `zoom_to_bounds` | Zoom view to coordinate bounds |
| `zoom_by_percent` | Zoom in/out by percentage |
| `pan_view` | Pan view by direction, element, or point |
| `orbit_view` | Orbit 3D view around model |
| `set_view_orientation` | Set 3D view to preset orientation |
| `isolate_elements` | Isolate elements (temporary or permanent) |
| `hide_elements` | Hide elements (temporary or permanent) |
| `reset_visibility` | Reset visibility overrides |
| `set_3d_section_box` | Create 3D section box around elements or bounds |
| `clear_section_box` | Remove section box from 3D view |
| `set_display_style` | Change view display style |
| `copy_element` | Copy elements with translation offset |
| `mirror_element` | Mirror elements about a vertical plane |
| `rotate_element` | Rotate elements by angle around a point |
| `array_elements` | Create linear or radial arrays |
| `align_elements` | Align elements to a reference element |
| `create_group` | Create Model Group from elements |
| `create_assembly` | Create Assembly from elements |
| `read_schedule_data` | Read data from a Revit schedule view |
| `export_element_data` | Export element data by category to CSV or JSON |
| `bulk_modify_parameters` | Bulk modify parameter values across elements in a category |
| `get_fill_patterns` | List available fill patterns (drafting and model) |
| `get_line_styles` | List available line styles with IDs |
| `get_detail_components` | List loaded detail component families and types |
| `get_revision_list` | List revisions with sequence, date, description |
| `get_sheet_list` | List sheets with numbers, names, viewport counts |
| `get_viewport_info` | Get viewport details on a specific sheet |
| `place_detail_arc` | Draw arc (center+radius+angles or three-point) |
| `place_detail_curve` | Draw spline or hermite curve through points |
| `place_detail_polyline` | Draw connected line segments with optional closing |
| `place_detail_circle` | Draw circle (two semicircular arcs) |
| `place_detail_rectangle` | Draw axis-aligned rectangle from two corners |
| `place_detail_ellipse` | Draw ellipse with optional rotation |
| `modify_detail_curve_style` | Change line style of existing detail curves |
| `place_filled_region` | Create hatched/filled region with boundary |
| `place_masking_region` | Create white-out masking region |
| `create_filled_region_type` | Create new region type (pattern + color) |
| `place_detail_component` | Place detail component family instance |
| `place_detail_group` | Place detail group instance |

**Coming Soon (Phase 2-08):**
- Sheet & viewport tools (place viewport, auto-arrange)
- Annotation tools (callouts, legends, revision clouds)
- Batch tools (batch detail lines, batch detail components)

## Development Status

### Phase 1: Foundation (MVP)
Core infrastructure for a working AI assistant.

- [x] **P1-01** Project Setup & Hello World
- [x] **P1-02** Dockable Chat Pane with themes
- [x] **P1-03** ExternalEvent Threading Infrastructure
- [x] **P1-04** Claude API Integration with Streaming
- [x] **P1-05** Context Engine (selection, view, level awareness)
- [x] **P1-06** Tool Framework & Registry
- [x] **P1-07** Read-Only Tools (11 query tools)
- [x] **P1-08** Transaction Manager
- [x] **P1-09** Modification Tools (10 tools)
- [x] **P1-10** Safety & Configuration

### Phase 1.5: View & Navigation Foundation
View manipulation and visual context tools for enhanced AI understanding.

- [x] **P1.5-01** Screenshot Capture (view snapshots for Claude vision)
- [x] **P1.5-02** View Management (13 tools: list, switch, open, create, duplicate, rename, delete views)
- [x] **P1.5-03** Camera Control (7 tools: zoom, pan, orbit, orientation presets)
- [x] **P1.5-04** Visual Isolation (6 tools: isolate, hide, reset visibility, section box, display style)

### Phase 2: Enhanced Capabilities
Advanced features and improved workflows.

- [x] **P2-01** Advanced Placement Tools (7 tools: grids, levels, detail lines, text notes, sheets, tags, dimensions)
- [x] **P2-02** Element Manipulation (7 tools: copy, mirror, rotate, array, align, group, assembly)
- [x] **P2-03** Multi-Step Design Operations (partial — within-round batching + system prompt guidance; cross-round single undo deferred)
- [x] **P2-04** Smart Context Awareness (grid intersections, relative positions, fuzzy type matching, level inference)
- [x] **P2-05** Visual Feedback (auto-select affected elements, markdown rendering in chat; preview graphics deferred)
- [x] **P2-06** Parameter & Schedule Tools (3 tools: read schedule data, export element data, bulk modify parameters)
- [x] **P2-07** Conversation Memory (project-keyed persistence, auto-load/save on document open/close, session change tracking, tool action summaries in system prompt)
- **P2-08** Drafting & Documentation Tools (split into 7 sub-chunks):
  - [x] **P2-08.1** DraftingHelper + Discovery Tools (6 tools: fill patterns, line styles, detail components, revisions, sheets, viewport info)
  - [x] **P2-08.2** Linework & Shape Tools (7 tools: arc, curve, polyline, circle, rectangle, ellipse, modify style)
  - [x] **P2-08.3** Region + Component Tools (5 tools: filled region, masking region, create region type, detail component, detail group)
  - [ ] **P2-08.4** Sheet & Viewport Tools (2 tools: place viewport, auto-arrange viewports)
  - [ ] **P2-08.5** Annotation & Reference Tools (4 tools: callout, legend, legend component, revision cloud)
  - [ ] **P2-08.6** Batch Tools (2 tools: batch detail lines, batch detail components)
  - [ ] **P2-08.7** System Prompt + Documentation

### Phase 3: Advanced & Multi-Discipline
Discipline-specific tool packs and advanced features.

- [ ] **P3-01** Structural Tool Pack
- [ ] **P3-02** MEP Tool Pack
- [ ] **P3-03** Fire Protection Tool Pack
- [ ] **P3-04** Architecture Tool Pack
- [ ] **P3-05** Export & Reporting Tools
- [ ] **P3-06** Model Health Tools
- [ ] **P3-07** Custom Prompt Templates

### Phase 4: Agentic Mode
Autonomous planning and execution for complex multi-step operations.

- [ ] **P4-01** Extended Thinking (deeper reasoning before responding)
- [ ] **P4-02** Planning Tools (create, update, complete execution plans)
- [ ] **P4-03** Agentic Session State (plan tracking within conversations)
- [ ] **P4-04** Auto-Verification Loop (screenshot + analysis after modifications)
- [ ] **P4-05** Agentic UI (plan progress panel, step visualization)
- [ ] **P4-06** Error Recovery & Adaptation (retry strategies, user escalation)

## Project Structure

```
RevitAI/
├── src/RevitAI/              # Main plugin source
│   ├── App.cs                # Plugin entry point
│   ├── Commands/             # Revit commands
│   ├── UI/                   # WPF views and view models
│   ├── Services/             # Core services
│   ├── Models/               # Data models
│   ├── Tools/                # Claude tool implementations
│   │   ├── ReadTools/        # Query tools (P1-07)
│   │   ├── ModifyTools/      # Modification tools (P1-09)
│   │   └── ViewTools/        # View & navigation tools (P1.5)
│   ├── Threading/            # ExternalEvent infrastructure
│   └── Transactions/         # Transaction management
├── docs/                     # Development documentation
│   ├── phase1/               # Phase 1 implementation details
│   ├── phase1.5/             # Phase 1.5 view & navigation specs
│   ├── phase2/               # Phase 2 specifications
│   ├── phase3/               # Phase 3 specifications
│   ├── phase4/               # Phase 4 agentic mode specs
│   └── appendix.md           # API patterns & reference
├── RevitAI.sln               # Visual Studio solution
├── RevitAI.addin             # Revit add-in manifest
└── CLAUDE.md                 # Development guide for AI assistance
```

## Contributing

We welcome contributions! Please see [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

### Quick Start

1. Fork and clone the repository
2. Open `RevitAI.sln` in Visual Studio 2022 (17.8+)
3. Build and test with Revit 2026
4. Submit a PR with your changes

### Requirements

- All commits must include a [DCO sign-off](https://developercertificate.org/) (`git commit -s`)
- New source files must include the [GPL-3.0 license header](CLAUDE.md#0-add-gpl-license-headers-to-new-files)
- Code should follow existing patterns in the codebase

### Key Architecture Notes

- **Threading**: All Revit API calls must run on the main UI thread via `ExternalEvent` + `IExternalEventHandler`
- **Tools**: Implement `IRevitTool` interface and register with `ToolRegistry`
- **Context**: `ContextEngine` gathers selection/view/level state for each Claude message

For detailed setup, coding standards, and the full contribution process, see [CONTRIBUTING.md](CONTRIBUTING.md).

## Troubleshooting

### Plugin doesn't appear in Revit

1. Verify files are in `%AppData%\Autodesk\Revit\Addins\2026\`
2. Check that both `RevitAI.addin` and `RevitAI.dll` exist
3. Ensure all dependency DLLs are present
4. Restart Revit completely

### API Key errors

1. Click the gear icon to open Settings
2. Verify the correct AI provider is selected (Claude or Gemini)
3. Re-enter your API key for the selected provider
4. For Claude: verify at [console.anthropic.com](https://console.anthropic.com/)
5. For Gemini: verify at [aistudio.google.com](https://aistudio.google.com/)

### Chat not responding

1. Check your internet connection
2. Verify API key is configured correctly
3. Click the Cancel button if a request is stuck
4. Clear the conversation and try again

## Known Issues & Limitations

### Current Limitations

| Limitation | Description | Planned Resolution |
|------------|-------------|-------------------|
| **Local file key breaks on move/rename** | Hash-based project key changes if .rvt file is moved | Acceptable trade-off |
| **Single document** | Only works with active document | Future consideration |

### Not Yet Supported

- Linked model queries
- Worksharing-specific operations
- Family editing context

## Team Deployment

### PowerShell Install Script

For deploying to multiple workstations:

```powershell
# install-revitai.ps1
param(
    [string]$SourcePath = "\\server\share\RevitAI\latest",
    [string]$RevitYear = "2026"
)

$addinsPath = "$env:APPDATA\Autodesk\Revit\Addins\$RevitYear"
$pluginPath = "$addinsPath\RevitAI"

# Create directory if needed
if (-not (Test-Path $pluginPath)) {
    New-Item -ItemType Directory -Path $pluginPath | Out-Null
}

# Copy files
Copy-Item "$SourcePath\RevitAI.addin" $addinsPath -Force
Copy-Item "$SourcePath\RevitAI\*" $pluginPath -Recurse -Force

Write-Host "RevitAI installed successfully to $pluginPath"
Write-Host "Restart Revit to load the plugin."
```

### Batch File Alternative

```batch
@echo off
REM install-revitai.bat

set SOURCE=\\server\share\RevitAI\latest
set ADDINS=%APPDATA%\Autodesk\Revit\Addins\2026
set PLUGIN=%ADDINS%\RevitAI

if not exist "%PLUGIN%" mkdir "%PLUGIN%"

copy /Y "%SOURCE%\RevitAI.addin" "%ADDINS%\"
xcopy /Y /E "%SOURCE%\RevitAI\*" "%PLUGIN%\"

echo RevitAI installed. Restart Revit to load.
pause
```

### Update Process

1. Close Revit on all workstations
2. Update files on network share
3. Users run install script (or automatic via login script)
4. Revit loads new version on next startup

## License

This project is licensed under the **GNU General Public License v3.0** - see the [LICENSE](LICENSE) file for details.

This means you are free to use, modify, and distribute this software, but any derivative works must also be open source under the same license.

## Acknowledgments

- Built with [Claude](https://anthropic.com/claude) by Anthropic
- Uses the Revit API by Autodesk
- UI components from [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)
