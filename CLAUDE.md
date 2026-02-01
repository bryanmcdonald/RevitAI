# RevitAI Development Guide

> **Purpose**: This file guides Claude Code sessions for implementing the RevitAI plugin.
> Detailed implementation chunks are in the `docs/` folder.

---

## Project Overview

**RevitAI** is a Revit plugin that embeds a Claude-powered conversational AI assistant directly into the Revit interface. Users interact via a dockable chat panel to query model information, place/modify elements, and automate tasks.

### Tech Stack
- **Target**: Revit 2026 (.NET 8)
- **UI**: WPF (IDockablePaneProvider)
- **API**: Claude Messages API via HttpClient
- **Model**: claude-sonnet-4-5-20250929 (configurable)

### Architecture Layers
1. **UI Layer** - WPF dockable chat pane
2. **Context Engine** - Selection/view/level tracking
3. **Claude API Service** - Message construction, tool definitions, response parsing
4. **Command Execution** - ExternalEvent marshalling, transactions, tool dispatch

### Critical Threading Rule
All Revit API calls MUST run on the main UI thread via `ExternalEvent` + `IExternalEventHandler`. Background threads handle API calls; results marshal back to main thread for model operations.

---

## Development Environment Setup

### Prerequisites
- Visual Studio 2022 (17.8+) with .NET 8 SDK
- Revit 2026 installed
- Revit 2026 SDK (download from Autodesk Developer Portal)
- Anthropic API key

### Revit SDK Setup
1. Download Revit 2026 SDK from Autodesk Developer Portal
2. Extract to a known location (e.g., `C:\RevitSDK\2026`)
3. Reference assemblies are in `C:\Program Files\Autodesk\Revit 2026\` (RevitAPI.dll, RevitAPIUI.dll)

### Project Folder Structure
```
RevitAI/
├── src/
│   ├── RevitAI/                    # Main plugin project
│   │   ├── App.cs                  # IExternalApplication entry point
│   │   ├── RevitAI.csproj
│   │   ├── Commands/               # IExternalCommand implementations
│   │   ├── UI/                     # WPF views and view models
│   │   │   ├── ChatPane.xaml
│   │   │   ├── ChatPane.xaml.cs
│   │   │   └── ChatViewModel.cs
│   │   ├── Services/               # Core services
│   │   │   ├── ClaudeApiService.cs
│   │   │   ├── ContextEngine.cs
│   │   │   └── ConfigurationService.cs
│   │   ├── Tools/                  # Claude tool implementations
│   │   │   ├── IRevitTool.cs
│   │   │   ├── ToolRegistry.cs
│   │   │   ├── ReadTools/
│   │   │   ├── ModifyTools/
│   │   │   └── ViewTools/          # P1.5: View/navigation tools
│   │   ├── Threading/              # ExternalEvent infrastructure
│   │   │   ├── RevitEventHandler.cs
│   │   │   └── CommandQueue.cs
│   │   └── Transactions/           # Transaction management
│   │       └── TransactionManager.cs
│   └── RevitAI.Tests/              # Unit tests (non-Revit logic)
├── docs/                           # Development documentation
│   ├── phase1/                     # Phase 1 chunks (individual files)
│   │   ├── README.md               # Phase 1 overview & index
│   │   ├── P1-01-project-setup.md
│   │   ├── P1-02-chat-pane.md
│   │   ├── P1-03-threading.md
│   │   ├── P1-04-claude-api.md
│   │   ├── P1-05-context-engine.md
│   │   ├── P1-06-tool-framework.md
│   │   ├── P1-07-read-tools.md
│   │   ├── P1-08-transaction-manager.md
│   │   ├── P1-09-modify-tools.md
│   │   └── P1-10-safety-config.md
│   ├── phase1.5/                   # Phase 1.5 chunks (view & navigation)
│   │   ├── README.md               # Phase 1.5 overview & index
│   │   ├── P1.5-01-screenshot-capture.md
│   │   ├── P1.5-02-view-management.md
│   │   ├── P1.5-03-camera-control.md
│   │   └── P1.5-04-visual-isolation.md
│   ├── phase2/                     # Phase 2 chunks (individual files)
│   │   ├── README.md               # Phase 2 overview & index
│   │   ├── P2-01-advanced-placement.md
│   │   ├── P2-02-element-manipulation.md
│   │   ├── P2-03-multi-step-operations.md
│   │   ├── P2-04-smart-context.md
│   │   ├── P2-05-visual-feedback.md
│   │   ├── P2-06-parameter-schedule.md
│   │   └── P2-07-conversation-memory.md
│   ├── phase3/                     # Phase 3 chunks (individual files)
│   │   ├── README.md               # Phase 3 overview & index
│   │   ├── P3-01-structural-tools.md
│   │   ├── P3-02-mep-tools.md
│   │   ├── P3-03-fire-protection-tools.md
│   │   ├── P3-04-architecture-tools.md
│   │   ├── P3-05-export-tools.md
│   │   ├── P3-06-model-health.md
│   │   └── P3-07-prompt-templates.md
│   └── appendix.md                 # API patterns, threading, troubleshooting
├── RevitAI.sln
├── RevitAI.addin                   # Manifest file
└── CLAUDE.md                       # This file
```

### Addin Installation Path
```
%AppData%\Autodesk\Revit\Addins\2026\RevitAI.addin
```

---

## Development Phases

| Phase | Description | Chunks | Documentation |
|-------|-------------|--------|---------------|
| **Phase 1** | Foundation (MVP) | P1-01 to P1-10 | [docs/phase1/README.md](docs/phase1/README.md) |
| **Phase 1.5** | View & Navigation Foundation | P1.5-01 to P1.5-04 | [docs/phase1.5/README.md](docs/phase1.5/README.md) |
| **Phase 2** | Enhanced Capabilities | P2-01 to P2-07 | [docs/phase2/README.md](docs/phase2/README.md) |
| **Phase 3** | Advanced & Multi-Discipline | P3-01 to P3-07 | [docs/phase3/README.md](docs/phase3/README.md) |
| **Appendix** | API Patterns & Reference | A.1 to A.9 | [docs/appendix.md](docs/appendix.md) |

### Phase 1 Quick Links

| Chunk | Description | File |
|-------|-------------|------|
| P1-01 | Project Setup & Hello World | [P1-01-project-setup.md](docs/phase1/P1-01-project-setup.md) |
| P1-02 | Dockable Chat Pane | [P1-02-chat-pane.md](docs/phase1/P1-02-chat-pane.md) |
| P1-03 | ExternalEvent Threading | [P1-03-threading.md](docs/phase1/P1-03-threading.md) |
| P1-04 | Claude API Integration | [P1-04-claude-api.md](docs/phase1/P1-04-claude-api.md) |
| P1-05 | Context Engine | [P1-05-context-engine.md](docs/phase1/P1-05-context-engine.md) |
| P1-06 | Tool Framework & Registry | [P1-06-tool-framework.md](docs/phase1/P1-06-tool-framework.md) |
| P1-07 | Read-Only Tools | [P1-07-read-tools.md](docs/phase1/P1-07-read-tools.md) |
| P1-08 | Transaction Manager | [P1-08-transaction-manager.md](docs/phase1/P1-08-transaction-manager.md) |
| P1-09 | Modification Tools | [P1-09-modify-tools.md](docs/phase1/P1-09-modify-tools.md) |
| P1-10 | Safety & Configuration | [P1-10-safety-config.md](docs/phase1/P1-10-safety-config.md) |

### Phase 1.5 Quick Links

| Chunk | Description | File |
|-------|-------------|------|
| P1.5-01 | Screenshot Capture | [P1.5-01-screenshot-capture.md](docs/phase1.5/P1.5-01-screenshot-capture.md) |
| P1.5-02 | View Management | [P1.5-02-view-management.md](docs/phase1.5/P1.5-02-view-management.md) |
| P1.5-03 | Camera Control | [P1.5-03-camera-control.md](docs/phase1.5/P1.5-03-camera-control.md) |
| P1.5-04 | Visual Isolation | [P1.5-04-visual-isolation.md](docs/phase1.5/P1.5-04-visual-isolation.md) |

### Phase 2 Quick Links

| Chunk | Description | File |
|-------|-------------|------|
| P2-01 | Advanced Placement Tools | [P2-01-advanced-placement.md](docs/phase2/P2-01-advanced-placement.md) |
| P2-02 | Element Manipulation Tools | [P2-02-element-manipulation.md](docs/phase2/P2-02-element-manipulation.md) |
| P2-03 | Multi-Step Design Operations | [P2-03-multi-step-operations.md](docs/phase2/P2-03-multi-step-operations.md) |
| P2-04 | Smart Context Awareness | [P2-04-smart-context.md](docs/phase2/P2-04-smart-context.md) |
| P2-05 | Visual Feedback System | [P2-05-visual-feedback.md](docs/phase2/P2-05-visual-feedback.md) |
| P2-06 | Parameter & Schedule Tools | [P2-06-parameter-schedule.md](docs/phase2/P2-06-parameter-schedule.md) |
| P2-07 | Conversation Memory | [P2-07-conversation-memory.md](docs/phase2/P2-07-conversation-memory.md) |

### Phase 3 Quick Links

| Chunk | Description | File |
|-------|-------------|------|
| P3-01 | Structural Tool Pack | [P3-01-structural-tools.md](docs/phase3/P3-01-structural-tools.md) |
| P3-02 | MEP Tool Pack | [P3-02-mep-tools.md](docs/phase3/P3-02-mep-tools.md) |
| P3-03 | Fire Protection Tool Pack | [P3-03-fire-protection-tools.md](docs/phase3/P3-03-fire-protection-tools.md) |
| P3-04 | Architecture Tool Pack | [P3-04-architecture-tools.md](docs/phase3/P3-04-architecture-tools.md) |
| P3-05 | Export & Reporting Tools | [P3-05-export-tools.md](docs/phase3/P3-05-export-tools.md) |
| P3-06 | Model Health Tools | [P3-06-model-health.md](docs/phase3/P3-06-model-health.md) |
| P3-07 | Custom Prompt Templates | [P3-07-prompt-templates.md](docs/phase3/P3-07-prompt-templates.md) |

---

## Current Status

**Currently working on**: P1-07 Complete

**Next chunk**: P1-08 (Transaction Manager)

### Known Limitations / Deferred Items
- **Markdown rendering in chat** - Chat messages display raw markdown (e.g., `**bold**` instead of **bold**). RichTextBox binding requires custom attached behavior. Deferred to P2-05 (Visual Feedback System).

---

## Post-Change Requirements

After making any meaningful changes to the codebase, ensure all documentation stays synchronized:

### 1. Update CLAUDE.md
- Reflect any architectural changes, new patterns, or workflow modifications
- Update the "Current Status" section to track progress
- Add new entries to "Known Limitations / Deferred Items" as discovered
- Update "Project Folder Structure" if new directories or key files are added

### 2. Update README.md (Project Root)
- Reflect user-facing changes: new features, modified setup steps, changed requirements
- Update "Available Tools" table when tools are added or modified
- Update "Development Status" checkboxes as chunks are completed
- Keep installation and usage instructions current

### 3. Update Phase Documentation (`docs/`)
- **Phase README.md files**: Update status, add cross-references to related chunks
- **Chunk .md files**: Add implementation notes, lessons learned, or gotchas discovered during development
- **Cross-phase references**: When work in one phase affects another, add notes to both
- **New phases/chunks**: Create new documentation files following existing naming conventions (`P#-##-description.md`)

### 4. Keep Everything in Sync
- Documentation should reflect the actual state of the codebase
- When adding a feature, update all relevant docs in the same commit when practical
- Future Claude Code sessions rely on accurate documentation for context

---

## Quick Reference

### Chunk Naming Convention
- `P1-XX`: Phase 1 - Foundation
- `P1.5-XX`: Phase 1.5 - View & Navigation Foundation
- `P2-XX`: Phase 2 - Enhanced Capabilities
- `P3-XX`: Phase 3 - Advanced & Multi-Discipline

### Each Chunk Includes
- **Goal**: What we're building
- **Prerequisites**: What must be complete first
- **Key Files**: Files to create/modify
- **Implementation Details**: Code samples and guidance
- **Verification**: How to test manually

---

## Success Criteria

The plugin will be considered successful when it meets these criteria:

| Criteria | Target | How to Measure |
|----------|--------|----------------|
| **First-attempt accuracy** | >90% | Engineers can place/modify elements via natural language correctly on first try |
| **Context awareness** | Reliable | "Make that thicker", "move it 2 feet north" work using live selection/view state |
| **Undo support** | Single Ctrl+Z | All AI-initiated changes can be reversed as one operation |
| **Response latency** | <5 seconds | Time from message sent to action completed for simple operations |
| **Installation time** | <2 minutes | Fresh install without admin privileges |
| **Stability** | Zero crashes | No Revit crashes attributable to the plugin under normal use |

---

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0 | Initial | Complete roadmap created |
| 1.1 | Restructure | Split into phase-based documentation files |
| 1.2 | Gap Analysis | Added missing features from overview: streaming, cancellation, temperature, max tokens, context verbosity, system prompt strategy, distribution scripts, success criteria |
| 1.3 | Restructure Phases | Split all phase docs into individual chunk files (phase1/, phase2/, phase3/) for easier Claude Code context management |
| 1.4 | Phase 1.5 | Added Phase 1.5 (View & Navigation Foundation) with 4 chunks: screenshot capture, view management, camera control, visual isolation |
| 1.5 | Post-Change Reqs | Added Post-Change Requirements section with documentation sync guidelines |
