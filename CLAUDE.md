# RevitAI Development Guide

> **Purpose**: This file guides Claude Code sessions for implementing the RevitAI plugin.
> Detailed implementation chunks are in the `docs/` folder.

---

## Project Overview

**RevitAI** is a Revit plugin that embeds an AI-powered conversational assistant directly into the Revit interface. Supports **Claude** (Anthropic) and **Google Gemini** as AI providers. Users interact via a dockable chat panel to query model information, place/modify elements, and automate tasks.

### Tech Stack
- **Target**: Revit 2026 (.NET 8)
- **UI**: WPF (IDockablePaneProvider)
- **AI Providers**: Claude (Anthropic) and Google Gemini via `IAiProvider` abstraction
- **API**: Messages API via HttpClient (both providers)
- **Default Models**: claude-sonnet-4-5-20250929 (Claude), gemini-3-pro-preview (Gemini)

### Architecture Layers
1. **UI Layer** - WPF dockable chat pane
2. **Context Engine** - Selection/view/level tracking
3. **AI Provider Service** - `IAiProvider` interface with Claude and Gemini implementations; message construction, tool definitions, response parsing
4. **Command Execution** - ExternalEvent marshalling, transactions, tool dispatch

### Critical Threading Rule
All Revit API calls MUST run on the main UI thread via `ExternalEvent` + `IExternalEventHandler`. Background threads handle API calls; results marshal back to main thread for model operations.

---

## Public Repository Notice

**This is a public open-source repository.** All code, commits, and documentation are publicly visible on GitHub.

### Do NOT Include
- Personal email addresses (use GitHub noreply emails for commits)
- API keys, tokens, or credentials (even in comments or examples)
- Personal file paths (e.g., `C:\Users\YourName\...`)
- Private hostnames, IP addresses, or internal URLs
- Any personally identifiable information (PII)

### Safe Patterns
- Use environment variables or secure storage for secrets
- Use generic paths in examples: `%APPDATA%`, `C:\RevitSDK\2026\`
- Use placeholder emails in examples: `your.email@example.com`
- Configure git to use your GitHub noreply email:
  ```
  git config user.email "YOUR_ID+username@users.noreply.github.com"
  ```

### Before Committing
Review your changes for any personal information. If you accidentally commit sensitive data, it must be scrubbed from git history (not just deleted in a new commit).

---

## Git Workflow

**Always use feature branches, never commit directly to main.**

### Starting a Session
1. Check if already on a feature branch: `git branch --show-current`
2. If on `main`, create a feature branch before making changes:
   ```
   git checkout -b feature/short-description
   ```
3. Use descriptive branch names: `feature/fix-autoscroll`, `feature/add-grid-orientation`, etc.

### During Development
- Commit to the feature branch as work progresses
- Each commit should be a logical unit of work with a clear message
- It's fine to accumulate multiple commits before creating a PR

### Creating a Pull Request
When the user asks to create a PR (or when a set of changes is complete):
1. Push the feature branch to origin (`git push -u origin feature/branch-name`)
   - This uploads the *feature branch* to GitHub, not main. Main is unaffected.
2. Create a PR from the feature branch to `main` using `gh pr create`
3. The PR description should explain the "why" behind the changes
4. Include a summary of all commits in the PR body
5. After the PR is merged on GitHub, main is updated with the changes

### Merging a Pull Request
This repo has branch protection and disallows merge commits. Always use:
```
gh pr merge <number> --squash --delete-branch --admin
```

### After PR is Merged
- The command above automatically updates local main and deletes the feature branch
- If needed manually: `git checkout main && git pull`

### Exception: Documentation-Only Changes
Minor documentation updates (typo fixes, small clarifications, README tweaks) can be committed directly to `main` without a PR. Use a PR for documentation changes only when:
- Significant overhaul affects the overall roadmap or architecture
- Changes to CLAUDE.md that affect development workflow
- New phase documentation or major restructuring

### Why This Workflow?
- PRs provide a detailed record of why changes were made
- Easier to review, revert, or reference changes later
- Keeps `main` history clean with meaningful merge commits

---

## Development Environment Setup

### Prerequisites
- Visual Studio 2022 (17.8+) with .NET 8 SDK
- Revit 2026 installed
- Revit 2026 SDK (download from Autodesk Developer Portal)
- API key (Anthropic for Claude, or Google for Gemini)

### Revit SDK Setup
1. Download Revit 2026 SDK from Autodesk Developer Portal
2. Extract to a known location (e.g., `C:\RevitSDK\2026`)
3. Reference assemblies are in `C:\Program Files\Autodesk\Revit 2026\` (RevitAPI.dll, RevitAPIUI.dll)

### Project Folder Structure

> **Canonical source**: See [docs/phase1/README.md](docs/phase1/README.md) for the most detailed file listing.

```
RevitAI/
├── src/
│   ├── RevitAI/                    # Main plugin project
│   │   ├── App.cs                  # IExternalApplication entry point
│   │   ├── RevitAI.csproj
│   │   ├── Commands/
│   │   │   └── ShowChatPaneCommand.cs
│   │   ├── UI/
│   │   │   ├── ChatPane.xaml
│   │   │   ├── ChatPane.xaml.cs
│   │   │   ├── ChatViewModel.cs
│   │   │   ├── ChatMessage.cs
│   │   │   ├── SettingsDialog.xaml      # P1-04: Quick API key setup dialog
│   │   │   ├── SettingsDialog.xaml.cs
│   │   │   ├── SettingsPane.xaml        # P1-10: Full configuration pane
│   │   │   ├── SettingsViewModel.cs
│   │   │   └── ConfirmationDialog.xaml
│   │   ├── Services/
│   │   │   ├── IAiProvider.cs           # Provider interface
│   │   │   ├── AiProviderFactory.cs     # Creates provider from config
│   │   │   ├── ClaudeApiService.cs      # Claude implementation
│   │   │   ├── GeminiApiService.cs      # Gemini implementation
│   │   │   ├── ConfigurationService.cs
│   │   │   ├── SecureStorage.cs
│   │   │   ├── ContextEngine.cs
│   │   │   ├── SafetyService.cs
│   │   │   └── UsageTracker.cs
│   │   ├── Models/
│   │   │   ├── ApiSettings.cs
│   │   │   ├── ClaudeModels.cs
│   │   │   ├── GeminiModels.cs          # Gemini API DTOs
│   │   │   ├── StreamEvents.cs
│   │   │   └── RevitContext.cs
│   │   ├── Tools/
│   │   │   ├── IRevitTool.cs
│   │   │   ├── ToolResult.cs
│   │   │   ├── ToolRegistry.cs
│   │   │   ├── ToolDispatcher.cs
│   │   │   ├── ReadTools/              # P1-07: Query tools
│   │   │   ├── ModifyTools/            # P1-09: Modification tools
│   │   │   ├── ViewTools/              # P1.5: View/navigation tools
│   │   │   └── DraftingTools/          # P2-08: Drafting & documentation
│   │   ├── Threading/
│   │   │   ├── RevitEventHandler.cs
│   │   │   ├── CommandQueue.cs
│   │   │   └── RevitCommand.cs
│   │   └── Transactions/
│   │       ├── TransactionManager.cs
│   │       └── TransactionScope.cs
│   └── RevitAI.Tests/              # Unit tests (non-Revit logic)
├── docs/                           # Development documentation
│   ├── phase1/                     # Phase 1 chunks
│   ├── phase1.5/                   # Phase 1.5 chunks (view & navigation)
│   ├── phase2/                     # Phase 2 chunks
│   ├── phase3/                     # Phase 3 chunks
│   ├── phase4/                     # Phase 4 chunks (agentic mode)
│   └── appendix.md                 # API patterns, threading, troubleshooting
├── RevitAI.sln
├── RevitAI.addin                   # Manifest file
└── CLAUDE.md                       # This file
```

### NuGet Dependencies

| Package | Purpose |
|---------|---------|
| `CommunityToolkit.Mvvm` | MVVM framework (ObservableObject, RelayCommand) |
| `System.Text.Json` | JSON serialization for Claude API |
| `System.Security.Cryptography.ProtectedData` | DPAPI for secure API key storage |

> **Note**: RevitAPI.dll and RevitAPIUI.dll are referenced directly from the Revit installation, not via NuGet.

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
| **Phase 2** | Enhanced Capabilities | P2-01 to P2-08 | [docs/phase2/README.md](docs/phase2/README.md) |
| **Phase 3** | Advanced & Multi-Discipline | P3-01 to P3-07 | [docs/phase3/README.md](docs/phase3/README.md) |
| **Phase 4** | Agentic Mode | P4-01 to P4-06 | [docs/phase4/README.md](docs/phase4/README.md) |
| **Appendix** | API Patterns & Reference | A.1 to A.8 | [docs/appendix.md](docs/appendix.md) |

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
| P2-08 | Drafting & Documentation Tools | [P2-08-drafting-tools.md](docs/phase2/P2-08-drafting-tools.md) |

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

### Phase 4 Quick Links

| Chunk | Description | File |
|-------|-------------|------|
| P4-01 | Extended Thinking | [P4-01-extended-thinking.md](docs/phase4/P4-01-extended-thinking.md) |
| P4-02 | Planning Tools | [P4-02-planning-tools.md](docs/phase4/P4-02-planning-tools.md) |
| P4-03 | Agentic Session State | [P4-03-session-state.md](docs/phase4/P4-03-session-state.md) |
| P4-04 | Auto-Verification Loop | [P4-04-auto-verification.md](docs/phase4/P4-04-auto-verification.md) |
| P4-05 | Agentic UI | [P4-05-agentic-ui.md](docs/phase4/P4-05-agentic-ui.md) |
| P4-06 | Error Recovery & Adaptation | [P4-06-error-recovery.md](docs/phase4/P4-06-error-recovery.md) |

---

## Current Status

See [README.md](README.md#development-status) for detailed development status with checkboxes.

**Next chunk to implement**: P2-07 (Conversation Memory)

> **Note**: P2-03 (Multi-Step Design Operations) is partial. Cross-round single-undo was deferred because Revit auto-closes TransactionGroups between ExternalEvent handler calls. Within-round batching works. See [P2-03 doc](docs/phase2/P2-03-multi-step-operations.md) for details.

> **Note**: P2-05 (Visual Feedback) is partial. Auto-selection of affected elements and markdown rendering are implemented. Preview graphics (DirectContext3D) were deferred. See [P2-05 doc](docs/phase2/P2-05-visual-feedback.md) for details.

---

## Post-Change Requirements

After making any meaningful changes to the codebase, ensure all documentation stays synchronized:

### 0. Add GPL License Headers to New Files

This project is licensed under GPL-3.0. **All new source files must include the GPL license header.**

**For C# files (.cs):**
```csharp
// RevitAI - AI-powered assistant for Autodesk Revit
// Copyright (C) 2025 Bryan McDonald
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.
```

**For XAML files (.xaml):**
```xml
<!--
    RevitAI - AI-powered assistant for Autodesk Revit
    Copyright (C) 2025 Bryan McDonald

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
-->
```

> **Note**: Auto-generated files in `obj/` directories do not need license headers.

### 1. Run Code Review

After completing code changes and verifying the build succeeds, run the **code-reviewer** agent to review all modified files before updating documentation. Address any issues found by the reviewer before proceeding. This ensures code quality is validated while context is fresh, before shifting focus to docs.

### 2. Update CLAUDE.md
- Reflect any architectural changes, new patterns, or workflow modifications
- Update the "Current Status" section to track progress
- Add new entries to "Known Limitations / Deferred Items" as discovered
- Update "Project Folder Structure" if new directories or key files are added

### 3. Update README.md (Project Root)
- Reflect user-facing changes: new features, modified setup steps, changed requirements
- Update "Available Tools" table when tools are added or modified
- Update "Development Status" checkboxes as chunks are completed
- Keep installation and usage instructions current

### 4. Update Phase Documentation (`docs/`)
- **Phase README.md files**: Update status, add cross-references to related chunks
- **Chunk .md files**: Add implementation notes, lessons learned, or gotchas discovered during development
- **Cross-phase references**: When work in one phase affects another, add notes to both
- **New phases/chunks**: Create new documentation files following existing naming conventions (`P#-##-description.md`)

### 5. Keep Everything in Sync
- Documentation should reflect the actual state of the codebase
- When adding a feature, update all relevant docs in the same commit when practical
- Future Claude Code sessions rely on accurate documentation for context

### 6. Tool Safety Classification
When creating new tools:
- Set `RequiresConfirmation = true` for tools that modify the Revit model
- Implement `GetDryRunDescription()` to describe what the tool would do
- See [docs/phase1/P1-06-tool-framework.md](docs/phase1/P1-06-tool-framework.md) for detailed guidance

---

## Quick Reference

### Chunk Naming Convention
- `P1-XX`: Phase 1 - Foundation
- `P1.5-XX`: Phase 1.5 - View & Navigation Foundation
- `P2-XX`: Phase 2 - Enhanced Capabilities
- `P3-XX`: Phase 3 - Advanced & Multi-Discipline
- `P4-XX`: Phase 4 - Agentic Mode

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
| 1.6 | Doc Cleanup | Consolidated status tracking to README.md, added NuGet deps, synced folder structure with Phase 1 README, removed duplicate tools from P2 |
| 1.7 | GPL Headers | Added GPL-3.0 license headers to all source files; added license header templates to Post-Change Requirements |
| 1.8 | Public Repo | Added Public Repository Notice section with guidelines for avoiding personal information in commits |
| 1.9 | P2-08 | Added P2-08 Drafting & Documentation Tools chunk with 10 tools for advanced linework, regions, viewports, callouts, legends, and revision clouds |
| 2.0 | Phase 4 | **Major version**: Added Phase 4 (Agentic Mode) with 6 chunks: extended thinking, planning tools, session state, auto-verification, agentic UI, error recovery. This is a major feature addition enabling autonomous operation. |
| 2.1 | Multi-Provider | Added Google Gemini as AI provider. New `IAiProvider` abstraction, `AiProviderFactory`, `GeminiApiService`, provider-aware settings UI, per-provider API keys and model selection. |
| 2.2 | P2-02 | Added 7 element manipulation tools: copy, mirror, rotate, array (linear/radial), align, create group, create assembly. Replaced `place_scope_box` with `rotate_element`. |
| 2.3 | P2-03 | Multi-step design operations: within-round batching cleanup, `AnyToolRequiresTransaction` helper, `externalGroup` param, system prompt guidance to batch modifications. Cross-round grouping not possible (Revit auto-closes TransactionGroups between ExternalEvent calls). Added code review step to Post-Change Requirements. |
| 2.4 | P2-04 | Smart Context Awareness: `GeometryResolver` for grid intersections (2D line math) and relative positions, fuzzy type matching (Levenshtein) in `ElementLookupHelper`, `GridSummary` in system prompt, optional level inference from active ViewPlan, `resolve_grid_intersection` read tool, `grid_intersection`/`relative_to` params on placement tools. |
| 2.5 | P2-05 | Visual Feedback System: `AffectedElementIds` on `ToolResult` + `OkWithElements()` factory, auto-selection of created/modified elements in viewport via `ToolDispatcher`, `MarkdownBehavior` attached property for RichTextBox with lazy visibility-aware conversion, dual TextBox/RichTextBox streaming pattern. Preview graphics deferred. |
| 2.6 | P2-06 | Parameter & Schedule Tools: 3 new tools — `read_schedule_data` (schedule reading with hidden-field-aware column mapping), `export_element_data` (CSV/JSON export with special parameter handling), `bulk_modify_parameters` (bulk modification with `{index}`/`{index:N}` placeholders, confirmation, auto-selection). |
