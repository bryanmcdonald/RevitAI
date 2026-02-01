# Revit + Claude AI Plugin — Project Overview & Feature Set

## Project Summary

Build a Revit plugin ("RevitAI") that embeds a conversational AI assistant powered by the Claude API directly into the Revit interface. The plugin provides a dockable chat panel where users can interact with Claude in natural language to query model information, place and modify elements, automate repetitive tasks, and get design assistance — all with live awareness of the current Revit model state, active view, and user selection.

The target users are a multi-discipline engineering team (Structural, Civil, Mechanical, Electrical, Fire Protection, Architecture, Industrial Engineering) working across multiple concurrent projects. The plugin should feel intuitive to engineers who are not necessarily software developers.

---

## Architecture Overview

### Technology Stack
- **Language:** C# (.NET 8 — Revit 2025+ migrated from .NET Framework 4.8 to .NET 8)
- **UI Framework:** WPF (dockable pane registered via `IDockablePaneProvider`)
- **Revit API:** Autodesk.Revit.DB and Autodesk.Revit.UI assemblies (included with Revit SDK)
- **Claude API:** Anthropic Messages API via `HttpClient` (REST, JSON)
- **Claude Model:** claude-sonnet-4-5-20250929 (recommended balance of speed/cost/capability for interactive use; can be made configurable)
- **Configuration:** Local JSON config file or Windows registry for API key storage, user preferences, and plugin settings

### Core Architecture Layers

```
┌─────────────────────────────────────────────────────┐
│                  Revit Application                   │
│                                                      │
│  ┌────────────────────────────────────────────────┐  │
│  │         1. UI LAYER (WPF Dockable Pane)        │  │
│  │  - Chat message display (scrollable history)    │  │
│  │  - Text input with send button                  │  │
│  │  - Status indicators (thinking, executing)      │  │
│  │  - Settings/config panel                        │  │
│  │  - Action confirmation dialogs                  │  │
│  │  - Context display (what Claude can "see")      │  │
│  └──────────────────┬─────────────────────────────┘  │
│                     │                                 │
│  ┌──────────────────▼─────────────────────────────┐  │
│  │       2. CONTEXT GATHERING ENGINE              │  │
│  │  - Selection monitor (event-driven)             │  │
│  │  - Active view/level tracker                    │  │
│  │  - Element property reader                      │  │
│  │  - Available families/types enumerator          │  │
│  │  - Grid system resolver                         │  │
│  │  - Parameter value extractor                    │  │
│  │  - Model-wide summary generator                 │  │
│  └──────────────────┬─────────────────────────────┘  │
│                     │                                 │
│  ┌──────────────────▼─────────────────────────────┐  │
│  │       3. CLAUDE API SERVICE LAYER              │  │
│  │  - Message construction (system prompt +        │  │
│  │    context + user message + conversation        │  │
│  │    history)                                     │  │
│  │  - Tool definitions registry                    │  │
│  │  - API call management (async, cancellation,    │  │
│  │    retry, error handling)                       │  │
│  │  - Response parsing (text + tool_use blocks)    │  │
│  │  - Token usage tracking                         │  │
│  │  - Streaming support for real-time responses    │  │
│  └──────────────────┬─────────────────────────────┘  │
│                     │                                 │
│  ┌──────────────────▼─────────────────────────────┐  │
│  │       4. COMMAND EXECUTION ENGINE              │  │
│  │  - Tool call dispatcher                         │  │
│  │  - ExternalEvent / IExternalEventHandler        │  │
│  │    bridge (marshal to Revit main thread)        │  │
│  │  - Transaction manager (open, commit, rollback) │  │
│  │  - Result collector and error reporter          │  │
│  │  - Undo group management                        │  │
│  │  - Action confirmation gate (for destructive    │  │
│  │    operations)                                  │  │
│  └────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────┘
```

### Threading Model (Critical)
All Revit API calls MUST execute on Revit's main UI thread within a valid API context. The plugin must use `ExternalEvent` + `IExternalEventHandler` to marshal commands from the async API response thread back to the main thread. The flow is:

1. User sends message → async task calls Claude API (off main thread)
2. Claude responds with tool_use → parsed on background thread
3. Tool call payload is queued into an `ExternalEventHandler`
4. `ExternalEvent.Raise()` signals Revit to execute on next idle
5. Handler runs on main thread → opens `Transaction` → executes Revit API calls → commits
6. Result is captured and sent back to Claude API as `tool_result` (if multi-turn tool use) or displayed to user

---

## Feature Set

### Phase 1: Foundation (MVP)

#### 1.1 Chat Interface
- Dockable WPF panel that registers on Revit startup
- Chat history display with distinct styling for user messages, Claude responses, and system/status messages
- Multi-line text input with Enter-to-send (Shift+Enter for newline)
- "Thinking..." and "Executing..." status indicators
- Clear conversation button (resets context)
- Conversation persistence within a Revit session (lost on close for MVP)

#### 1.2 Live Context Engine
The plugin automatically gathers and injects the following context with every message sent to Claude:

- **Active view:** View name, type (floor plan, section, 3D, sheet, etc.), associated level
- **Current selection:** Element IDs, categories, type names, key parameters, location/geometry summary
- **Active level:** Level name and elevation
- **Project info:** Project name, project number, site location
- **Available types:** Loaded family types relevant to the current context (e.g., if user mentions "wall," include available wall types)

Context is injected into the system prompt or as a structured preamble to the user message so Claude always has situational awareness.

#### 1.3 Read-Only Query Tools
Tools that let Claude answer questions about the model without modifying anything:

| Tool Name | Description |
|-----------|-------------|
| `get_selected_elements` | Returns details of currently selected elements |
| `get_element_properties` | Returns all parameters/properties for a given element ID |
| `get_elements_by_category` | Lists elements of a given category (walls, columns, beams, etc.) optionally filtered by view or level |
| `get_view_info` | Returns details about the active or a specified view |
| `get_levels` | Lists all levels with elevations |
| `get_grids` | Lists all grids with their geometry/intersections |
| `get_available_types` | Returns loaded family types for a given category |
| `get_project_info` | Returns project metadata |
| `get_element_quantity_takeoff` | Count and summarize elements by category, type, level |
| `get_room_info` | Returns room boundaries, areas, and associated elements |
| `get_warnings` | Returns current Revit warnings/errors in the model |

#### 1.4 Basic Modification Tools
Tools that modify the model (all execute inside Transactions with undo support):

| Tool Name | Description |
|-----------|-------------|
| `place_wall` | Creates a wall given start/end points, wall type, base/top level |
| `place_column` | Places a structural column at a point, given type and base/top level |
| `place_beam` | Places a structural beam between two points, given type and level |
| `place_floor` | Creates a floor from a boundary loop, given floor type and level |
| `modify_element_parameter` | Sets a parameter value on an element by ID |
| `change_element_type` | Changes the type of an element (e.g., swap wall type) |
| `move_element` | Translates an element by a vector |
| `delete_elements` | Deletes elements by ID (with confirmation) |
| `select_elements` | Sets the Revit selection to specified element IDs (visual feedback) |
| `zoom_to_element` | Adjusts the view to show a specific element |

#### 1.5 Confirmation & Safety
- Destructive operations (delete, bulk modify) require user confirmation via a dialog before execution
- All tool-based modifications are wrapped in named `TransactionGroups` so they appear as a single undo operation (user can Ctrl+Z to reverse everything Claude just did)
- A "dry run" mode where Claude describes what it would do without executing

#### 1.6 Configuration
- API key input and secure storage (encrypted in local config, not plaintext)
- Model selection (claude-sonnet-4-5-20250929 default, option for opus-4-5 for complex tasks)
- Temperature setting
- Toggle confirmation dialogs on/off for trusted operations
- Max token budget per request

---

### Phase 2: Enhanced Capabilities

#### 2.1 Advanced Placement and Design Tools

| Tool Name | Description |
|-----------|-------------|
| `place_grid` | Creates grid lines |
| `place_level` | Creates new levels |
| `place_dimension` | Adds dimensions between references |
| `place_tag` | Tags elements in a view |
| `create_section_view` | Creates section views at specified locations |
| `create_sheet` | Creates a sheet and places views on it |
| `place_detail_line` | Draws detail lines in a view |
| `place_text_note` | Places text annotations |
| `create_assembly` | Groups elements into assemblies |
| `create_group` | Creates groups from selected elements |
| `copy_element` | Copies elements with offset |
| `mirror_element` | Mirrors elements about an axis |
| `array_elements` | Creates linear or radial arrays |
| `align_elements` | Aligns elements to a reference |
| `place_scope_box` | Creates scope boxes |

#### 2.2 Multi-Step Design Operations
Allow Claude to execute complex, multi-element operations as a coordinated sequence:

- "Create a structural bay with columns at corners, beams connecting them, and a floor slab" → Multiple tool calls in a single TransactionGroup
- "Set up a grid system: 4 grids at 30' spacing in X, 3 grids at 25' spacing in Y" → Series of `place_grid` calls
- "Add columns at all grid intersections on Level 1" → Query grids, compute intersections, place columns in batch

#### 2.3 Smart Context Awareness
- **Snapping/Grid awareness:** When Claude places elements "at grid A-1," the plugin resolves the grid intersection coordinates automatically
- **Relative placement:** Support for "3 feet to the right of the selected column" type instructions
- **Type inference:** If user says "W10x49 column," Claude searches available types and finds the matching family
- **Level inference:** If working in a Level 2 floor plan, default element placement to Level 2

#### 2.4 Visual Feedback
- Highlight elements Claude is referencing (temporary color override)
- Preview graphics for proposed placements before confirming (using `DirectContext3D` or temporary detail lines)
- Status bar integration showing plugin state

#### 2.5 Parameter & Schedule Operations

| Tool Name | Description |
|-----------|-------------|
| `bulk_modify_parameters` | Modify a parameter across multiple elements matching a filter |
| `read_schedule_data` | Extract data from a schedule view |
| `export_element_data` | Export element data to CSV/JSON for a category + filter |
| `create_schedule` | Create a new schedule view with specified fields |

#### 2.6 Conversation Memory
- Persist conversation history to local file (per project)
- Allow Claude to reference earlier decisions in the conversation
- "Undo what we discussed earlier" type interactions
- Maintain a session-level model diff (track all changes Claude has made this session)

---

### Phase 3: Advanced & Multi-Discipline

#### 3.1 Discipline-Specific Tool Packs
Modular tool sets that can be enabled/disabled based on the user's discipline:

**Structural:**
- Foundation placement
- Brace/truss creation
- Structural connection management
- Load path analysis queries

**MEP:**
- Duct/pipe/conduit routing assistance
- Equipment placement
- System creation and assignment
- Clash detection queries

**Fire Protection:**
- Sprinkler layout assistance
- Fire-rated wall/assembly identification
- Egress path analysis queries
- Hazardous material storage area identification

**Architecture:**
- Room/space creation and management
- Door/window placement
- Curtain wall/panel management
- Area plans and color schemes

#### 3.2 Document & Export Tools

| Tool Name | Description |
|-----------|-------------|
| `export_view_to_image` | Export a view as PNG/JPG |
| `print_sheets` | Batch print sheets to PDF |
| `export_to_ifc` | Export model or selection to IFC |
| `generate_report` | Create a text report summarizing model data for a category/system |

#### 3.3 Revit Warnings & Model Health
- "What warnings are in the model?" → Reads Revit warnings
- "Find all overlapping walls" → Clash/overlap detection
- "Show me elements not on any level" → Model hygiene queries

#### 3.4 Custom Prompt Templates
- Saveable prompt templates for common workflows (e.g., "Column Layout Assistant," "Schedule QC Checker," "Drawing Setup")
- Template includes a custom system prompt and pre-configured tool availability
- Users can share templates with the team

---

## System Prompt Strategy

The system prompt sent to Claude with every API call is critical. It should include:

1. **Role definition:** "You are an AI assistant embedded in Autodesk Revit. You help engineers design, query, and modify BIM models through natural language conversation. You have access to tools that read and modify the Revit model."

2. **Tool usage instructions:** Clear guidance on when to use which tools, how to handle ambiguity (ask the user rather than guess), and how to chain tools for complex operations.

3. **Safety rules:**
   - Always confirm before deleting elements
   - Never modify elements on sheets/views without confirmation
   - Wrap related operations in a single transaction group for clean undo
   - When uncertain about type or location, query first then act

4. **Context injection point:** The live context (selection, view, level, etc.) gets appended here as structured data.

5. **Discipline awareness:** If configured for a specific discipline, include relevant terminology and conventions.

6. **Unit awareness:** Revit internal units are feet (decimal). The system prompt should instruct Claude to accept user input in common formats (feet-inches, millimeters, etc.) and convert appropriately.

---

## Installation & Distribution

- **Packaging:** Single `.addin` manifest file + compiled DLL(s) + config template
- **Install location:** `%AppData%\Autodesk\Revit\Addins\<year>\`
- **Distribution method:** Shared network folder with install script (PowerShell or batch), or simple MSI installer
- **Updates:** Overwrite DLL in install directory; Revit loads new version on next startup
- **Revit version support:** Target Revit 2026 (Revit API version compatibility is per-year)
- **No Autodesk developer account or App Store listing required for internal use**

---

## API Key Management

For a team deployment, options include:
- **Per-user key:** Each user enters their own Anthropic API key in the plugin settings (simplest)
- **Shared team key:** A single API key stored in a team-accessible encrypted config file or environment variable
- **Proxy server:** Route API calls through an internal proxy that adds the API key server-side (most secure, prevents key exposure on workstations; adds infrastructure overhead)

Recommendation for initial deployment: per-user API key stored encrypted in local user config.

---

## Key Technical Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| Revit threading violations (API calls off main thread) | Strict use of ExternalEvent + IExternalEventHandler for all model operations; code review checklist |
| Claude hallucinating element IDs or invalid parameters | Always validate element IDs exist before operating; validate parameter names and value types; return clear errors to Claude so it can self-correct |
| API latency degrading UX | Streaming responses for text; loading indicators; async/await throughout; cancel button for long operations |
| Token cost with large context payloads | Configurable context verbosity (minimal/standard/detailed); only include types/elements relevant to conversation; token counter in UI |
| Revit crash from bad geometry operations | Wrap all transactions in try-catch; rollback on failure; validate geometry inputs before execution |
| Model corruption | Named TransactionGroups with clean rollback; "undo all AI changes" option; encourage users to save before major operations |
| API key security | Encrypted local storage; option for proxy server; never log API keys |

---

## Development Phases & Rough Effort Estimates

| Phase | Scope | Estimated Effort |
|-------|-------|-----------------|
| Phase 1 MVP | Chat UI, context engine, 10-12 read tools, 8-10 modification tools, configuration, safety/confirmation system | 4-6 weeks |
| Phase 2 Enhanced | Advanced tools, multi-step operations, smart context, visual feedback, conversation memory | 4-6 weeks |
| Phase 3 Advanced | Discipline-specific packs, export tools, model health, prompt templates | 6-8 weeks (modular, can be incremental) |

---

## Success Criteria

- Engineers can place and modify common elements (walls, columns, beams, floors) via natural language with >90% accuracy on first attempt
- Context-aware interactions ("make that thicker," "move it 2 feet north") work reliably using live selection and view state
- All AI-initiated model changes are fully undoable as a single Ctrl+Z operation
- Response latency (message sent to action completed) under 5 seconds for simple operations
- Plugin installs in under 2 minutes with no admin privileges required
- Zero Revit crashes attributable to the plugin under normal use
