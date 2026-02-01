# Phase 1: Foundation (MVP)

> Each chunk represents a 1-2 day development session.
> Prerequisites must be complete before starting a chunk.

---

## Overview

Phase 1 establishes the core plugin infrastructure: a dockable chat UI, Claude API integration, Revit context awareness, and basic read/write tools. Upon completion, users can converse with Claude about their Revit model, query element information, and perform basic modifications.

---

## Chunk Index

| Chunk | Name | Description | Prerequisites | Status |
|-------|------|-------------|---------------|--------|
| [P1-01](P1-01-project-setup.md) | Project Setup & Hello World | Solution structure, minimal plugin that loads in Revit | Dev environment | ✅ Complete |
| [P1-02](P1-02-chat-pane.md) | Dockable Chat Pane | WPF chat UI with message display, input, status | P1-01 | ✅ Complete |
| [P1-03](P1-03-threading.md) | ExternalEvent Threading | Thread marshalling infrastructure for Revit API calls | P1-02 | ✅ Complete |
| [P1-04](P1-04-claude-api.md) | Claude API Integration | Messages API, streaming, configuration service | P1-03 | ✅ Complete |
| [P1-05](P1-05-context-engine.md) | Context Engine | Selection/view/level tracking, system prompt injection | P1-04 | ✅ Complete |
| [P1-06](P1-06-tool-framework.md) | Tool Framework & Registry | IRevitTool interface, registry, dispatcher | P1-05 | ✅ Complete |
| [P1-07](P1-07-read-tools.md) | Read-Only Tools | 11 query tools for model information | P1-06 | ✅ Complete |
| [P1-08](P1-08-transaction-manager.md) | Transaction Manager | Transaction/TransactionGroup handling, undo support | P1-07 | Pending |
| [P1-09](P1-09-modify-tools.md) | Modification Tools | 10 tools for element placement and modification | P1-08 | Pending |
| [P1-10](P1-10-safety-config.md) | Safety & Configuration | Confirmation dialogs, settings UI, dry-run mode | P1-09 | Pending |

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
│   ├── SettingsDialog.xaml             # P1-04
│   ├── SettingsDialog.xaml.cs          # P1-04
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
│   ├── SecureStorage.cs                # P1-04
│   ├── ContextEngine.cs                # P1-05
│   └── SafetyService.cs                # P1-10
├── Models/
│   ├── ApiSettings.cs                  # P1-04
│   ├── ClaudeModels.cs                 # P1-04 (request/response/message types)
│   ├── StreamEvents.cs                 # P1-04 (SSE streaming events)
│   └── RevitContext.cs                 # P1-05
├── Tools/
│   ├── IRevitTool.cs                   # P1-06
│   ├── ToolResult.cs                   # P1-06
│   ├── ToolRegistry.cs                 # P1-06
│   ├── ToolDispatcher.cs               # P1-06
│   ├── EchoTool.cs                     # P1-06 (test tool)
│   ├── ReadTools/                      # P1-07
│   │   ├── Helpers/
│   │   │   └── CategoryHelper.cs       # P1-07 (category name mapping)
│   │   ├── GetLevelsTool.cs            # P1-07
│   │   ├── GetGridsTool.cs             # P1-07
│   │   ├── GetProjectInfoTool.cs       # P1-07
│   │   ├── GetViewInfoTool.cs          # P1-07
│   │   ├── GetSelectedElementsTool.cs  # P1-07
│   │   ├── GetWarningsTool.cs          # P1-07
│   │   ├── GetAvailableTypesTool.cs    # P1-07
│   │   ├── GetElementsByCategoryTool.cs # P1-07
│   │   ├── GetElementPropertiesTool.cs # P1-07
│   │   ├── GetRoomInfoTool.cs          # P1-07
│   │   └── GetElementQuantityTakeoffTool.cs # P1-07
│   └── ModifyTools/                    # P1-09 (10 tools)
└── Transactions/
    ├── TransactionManager.cs           # P1-08
    └── TransactionScope.cs             # P1-08
```

---

## Phase 1 Completion Criteria

- [x] Plugin loads in Revit 2026 without errors (P1-01)
- [x] Dockable chat pane displays and accepts input (P1-02)
- [x] Claude API responds to messages (P1-04)
- [x] Streaming responses display progressively (P1-04)
- [x] Request cancellation works (P1-04)
- [x] Context (selection, view, level) is captured and sent to Claude (P1-05)
- [x] Read-only tools return accurate model information (P1-07)
- [ ] Modification tools create/modify elements successfully
- [ ] All modifications can be undone with single Ctrl+Z
- [ ] Destructive operations show confirmation dialog
- [x] Settings persist between sessions (P1-04)

---

## Deferred Items

| Item | Reason | Deferred To |
|------|--------|-------------|
| Markdown rendering in chat | RichTextBox.Document doesn't support direct binding; requires custom attached behavior | P2-05 |

---

## Next Phase

After completing Phase 1, proceed to **[Phase 1.5: View & Navigation Foundation](../phase1.5/README.md)** before starting Phase 2. Phase 1.5 adds essential view navigation, camera control, and visual context tools that enable Claude to "see" and navigate the model like a human user.
