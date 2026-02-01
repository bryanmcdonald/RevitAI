# Phase 1: Foundation (MVP)

> Each chunk represents a 1-2 day development session.
> Prerequisites must be complete before starting a chunk.

---

## Overview

Phase 1 establishes the core plugin infrastructure: a dockable chat UI, Claude API integration, Revit context awareness, and basic read/write tools. Upon completion, users can converse with Claude about their Revit model, query element information, and perform basic modifications.

---

## Chunk Index

| Chunk | Name | Description | Prerequisites |
|-------|------|-------------|---------------|
| [P1-01](P1-01-project-setup.md) | Project Setup & Hello World | Solution structure, minimal plugin that loads in Revit | Dev environment |
| [P1-02](P1-02-chat-pane.md) | Dockable Chat Pane | WPF chat UI with message display, input, status | P1-01 |
| [P1-03](P1-03-threading.md) | ExternalEvent Threading | Thread marshalling infrastructure for Revit API calls | P1-02 |
| [P1-04](P1-04-claude-api.md) | Claude API Integration | Messages API, streaming, configuration service | P1-03 |
| [P1-05](P1-05-context-engine.md) | Context Engine | Selection/view/level tracking, system prompt injection | P1-04 |
| [P1-06](P1-06-tool-framework.md) | Tool Framework & Registry | IRevitTool interface, registry, dispatcher | P1-05 |
| [P1-07](P1-07-read-tools.md) | Read-Only Tools | 11 query tools for model information | P1-06 |
| [P1-08](P1-08-transaction-manager.md) | Transaction Manager | Transaction/TransactionGroup handling, undo support | P1-07 |
| [P1-09](P1-09-modify-tools.md) | Modification Tools | 10 tools for element placement and modification | P1-08 |
| [P1-10](P1-10-safety-config.md) | Safety & Configuration | Confirmation dialogs, settings UI, dry-run mode | P1-09 |

---

## Key Files Created in Phase 1

```
src/RevitAI/
├── App.cs                              # P1-01
├── RevitAI.csproj                      # P1-01
├── Commands/
│   └── ShowChatPaneCommand.cs          # P1-02
├── UI/
│   ├── ChatPane.xaml                   # P1-02
│   ├── ChatPane.xaml.cs                # P1-02
│   ├── ChatViewModel.cs                # P1-02
│   ├── ChatMessage.cs                  # P1-02
│   ├── SettingsPane.xaml               # P1-10
│   ├── SettingsViewModel.cs            # P1-10
│   └── ConfirmationDialog.xaml         # P1-10
├── Threading/
│   ├── RevitEventHandler.cs            # P1-03
│   ├── CommandQueue.cs                 # P1-03
│   └── RevitCommand.cs                 # P1-03
├── Services/
│   ├── ClaudeApiService.cs             # P1-04
│   ├── ConfigurationService.cs         # P1-04
│   ├── ContextEngine.cs                # P1-05
│   └── SafetyService.cs                # P1-10
├── Models/
│   ├── ClaudeRequest.cs                # P1-04
│   ├── ClaudeResponse.cs               # P1-04
│   ├── Message.cs                      # P1-04
│   └── RevitContext.cs                 # P1-05
├── Tools/
│   ├── IRevitTool.cs                   # P1-06
│   ├── ToolDefinition.cs               # P1-06
│   ├── ToolRegistry.cs                 # P1-06
│   ├── ToolDispatcher.cs               # P1-06
│   ├── ToolResult.cs                   # P1-06
│   ├── ReadTools/                      # P1-07 (11 tools)
│   └── ModifyTools/                    # P1-09 (10 tools)
└── Transactions/
    ├── TransactionManager.cs           # P1-08
    └── TransactionScope.cs             # P1-08
```

---

## Phase 1 Completion Criteria

- [ ] Plugin loads in Revit 2026 without errors
- [ ] Dockable chat pane displays and accepts input
- [ ] Claude API responds to messages
- [ ] Context (selection, view, level) is captured and sent to Claude
- [ ] Read-only tools return accurate model information
- [ ] Modification tools create/modify elements successfully
- [ ] All modifications can be undone with single Ctrl+Z
- [ ] Destructive operations show confirmation dialog
- [ ] Settings persist between sessions
