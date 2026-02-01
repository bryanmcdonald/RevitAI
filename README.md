# RevitAI

A Revit plugin that embeds a Claude-powered conversational AI assistant directly into the Revit interface. Query model information, place and modify elements, and automate tasks using natural language.

## Overview

RevitAI provides a dockable chat panel where you interact with Claude to:
- **Query your model** - Get element counts, properties, levels, grids, warnings, and more
- **Understand context** - Claude sees your current selection, active view, and level
- **Automate tasks** - Place elements, modify parameters, and perform multi-step operations (coming soon)

Built for multi-discipline engineering teams (Structural, MEP, Fire Protection, Architecture) working in Revit 2026.

## Current Status

**Phase 1 Foundation: 70% Complete**

| Feature | Status |
|---------|--------|
| Dockable Chat UI | Complete |
| Claude API Integration | Complete |
| Streaming Responses | Complete |
| Context Engine | Complete |
| Tool Framework | Complete |
| Read-Only Tools (11 tools) | Complete |
| Transaction Manager | Pending |
| Modification Tools | Pending |
| Safety & Confirmation | Pending |

See [Development Status](#development-status) for details.

## Requirements

- **Revit 2026** (required - uses .NET 8)
- **Windows 10/11** (64-bit)
- **Anthropic API Key** ([Get one here](https://console.anthropic.com/))

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
5. **Configure API Key**: Click the gear icon in the RevitAI panel and enter your Anthropic API key

### Manual Build

If you want to build from source:

1. **Clone the repository**
   ```bash
   git clone https://github.com/YOUR_USERNAME/RevitAI.git
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
- Enter your Anthropic API key (stored securely encrypted)
- Select the Claude model (default: claude-sonnet-4-5-20250929)
- Adjust context verbosity level

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

RevitAI automatically provides Claude with:
- **Active view** - Name, type, scale, associated level
- **Current selection** - Element details for up to 20 selected items
- **Active level** - When working in a floor plan
- **Available types** - Loaded family types for common categories

This means you can ask contextual questions like:
- "What is selected?" (Claude sees your current selection)
- "What level am I on?" (Claude sees your active view's level)

## Available Tools

RevitAI provides Claude with tools to query your Revit model:

| Tool | Description |
|------|-------------|
| `get_selected_elements` | Details of currently selected elements |
| `get_element_properties` | All parameters for a specific element |
| `get_elements_by_category` | List elements by category (walls, columns, etc.) |
| `get_levels` | All levels with elevations |
| `get_grids` | Grid lines with geometry |
| `get_view_info` | Active view details |
| `get_project_info` | Project metadata |
| `get_available_types` | Loaded family types by category |
| `get_warnings` | Model warnings and errors |
| `get_room_info` | Room boundaries and areas |
| `get_element_quantity_takeoff` | Element counts and summaries |

**Coming Soon (Phase 1 completion):**
- Element placement (walls, columns, beams, floors)
- Parameter modification
- Element movement and deletion
- Transaction management with undo support

**Coming Soon (Phase 1.5):**
- Screenshot capture for Claude vision analysis
- View switching and creation
- Camera control (zoom, pan, 3D navigation)
- Visual isolation and graphics overrides

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
- [ ] **P1-08** Transaction Manager
- [ ] **P1-09** Modification Tools (10 tools)
- [ ] **P1-10** Safety & Configuration

### Phase 1.5: View & Navigation Foundation
View manipulation and visual context tools for enhanced AI understanding.

- [ ] **P1.5-01** Screenshot Capture (view snapshots for Claude vision)
- [ ] **P1.5-02** View Management (switch views, create views, list views)
- [ ] **P1.5-03** Camera Control (zoom, pan, 3D orbit, section boxes)
- [ ] **P1.5-04** Visual Isolation (isolate, hide, override graphics)

### Phase 2: Enhanced Capabilities
Advanced features and improved workflows.

- [ ] Advanced Placement Tools (grids, levels, dimensions, tags)
- [ ] Element Manipulation (copy, mirror, array, align)
- [ ] Multi-Step Design Operations
- [ ] Smart Context Awareness (grid snapping, type inference)
- [ ] Visual Feedback (element highlighting, previews)
- [ ] Parameter & Schedule Operations
- [ ] Conversation Memory & Persistence

### Phase 3: Advanced & Multi-Discipline
Discipline-specific tool packs and advanced features.

- [ ] Structural Tool Pack
- [ ] MEP Tool Pack
- [ ] Fire Protection Tool Pack
- [ ] Architecture Tool Pack
- [ ] Export & Reporting Tools
- [ ] Model Health Tools
- [ ] Custom Prompt Templates

## Project Structure

```
RevitAI/
├── src/RevitAI/              # Main plugin source
│   ├── App.cs                # Plugin entry point
│   ├── Commands/             # Revit commands
│   ├── UI/                   # WPF views and view models
│   ├── Services/             # Core services
│   ├── Tools/                # Claude tool implementations
│   │   ├── ReadTools/        # Query tools
│   │   ├── ModifyTools/      # Modification tools
│   │   └── ViewTools/        # View & navigation tools (P1.5)
│   ├── Threading/            # ExternalEvent infrastructure
│   └── Models/               # Data models
├── docs/                     # Development documentation
│   ├── phase1/               # Phase 1 implementation details
│   ├── phase1.5/             # Phase 1.5 view & navigation specs
│   ├── phase2/               # Phase 2 specifications
│   ├── phase3/               # Phase 3 specifications
│   └── appendix.md           # API patterns & reference
├── RevitAI.sln               # Visual Studio solution
├── RevitAI.addin             # Revit add-in manifest
└── CLAUDE.md                 # Development guide for AI assistance
```

## Contributing

### Development Setup

1. **Prerequisites**
   - Visual Studio 2022 (17.8+) with .NET 8 SDK
   - Revit 2026 installed
   - Anthropic API key for testing

2. **Clone and Build**
   ```bash
   git clone https://github.com/YOUR_USERNAME/RevitAI.git
   cd RevitAI
   ```
   Open `RevitAI.sln` in Visual Studio and build.

3. **Development Documentation**
   - Start with `CLAUDE.md` for project overview
   - Each phase has detailed implementation specs in `docs/`
   - Current work: `docs/phase1/README.md`

### Key Architecture Notes

- **Threading**: All Revit API calls MUST run on the main UI thread via `ExternalEvent` + `IExternalEventHandler`
- **Tools**: Implement `IRevitTool` interface and register with `ToolRegistry`
- **Context**: `ContextEngine` gathers selection/view/level state for each Claude message

## Troubleshooting

### Plugin doesn't appear in Revit

1. Verify files are in `%AppData%\Autodesk\Revit\Addins\2026\`
2. Check that both `RevitAI.addin` and `RevitAI.dll` exist
3. Ensure all dependency DLLs are present
4. Restart Revit completely

### API Key errors

1. Click the gear icon to open Settings
2. Re-enter your Anthropic API key
3. Ensure the key is valid at [console.anthropic.com](https://console.anthropic.com/)

### Chat not responding

1. Check your internet connection
2. Verify API key is configured correctly
3. Click the Cancel button if a request is stuck
4. Clear the conversation and try again

## License

[Add your license here]

## Acknowledgments

- Built with [Claude](https://anthropic.com/claude) by Anthropic
- Uses the Revit API by Autodesk
- UI components from [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)
