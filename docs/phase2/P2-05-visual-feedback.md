# P2-05: Visual Feedback System

**Goal**: Provide visual feedback when AI creates/modifies elements (auto-selection highlighting) and render markdown in chat messages.

**Prerequisites**: P2-04 complete.

**Status**: Implemented (partial scope — see Deferred Items below).

---

## What Was Implemented

### 1. Auto-Select Affected Elements

After any tool that creates or modifies Revit elements, the affected elements are automatically selected in the viewport using Revit's native selection highlight. This gives immediate visual feedback about what changed.

**Approach**:
- `ToolResult` gained an `AffectedElementIds` property (`IReadOnlyList<long>`) and an `OkWithElements()` factory method
- 21 tool files in `ModifyTools/` updated to report element IDs via `OkWithElements()`
- `ToolDispatcher` calls `UIDocument.Selection.SetElementIds()` after successful tool execution
- Works in both single-tool and batch-tool paths
- No transaction required (selection is a UI operation)
- Best-effort — failures in selection don't fail the tool

**Tools that report affected elements**:

| Category | Tools |
|----------|-------|
| **Creation** | PlaceWall, PlaceColumn, PlaceBeam, PlaceFloor, PlaceGrid, PlaceLevel, PlaceDetailLine, PlaceTextNote, CreateSheet, PlaceTag, PlaceDimension, CopyElement, MirrorElement, ArrayElements, CreateGroup, CreateAssembly |
| **Modification** | MoveElement, RotateElement, AlignElements, ModifyElementParameter, ChangeElementType |

**Excluded** (intentionally not highlighted):
- `DeleteElementsTool` — elements no longer exist after deletion
- `SelectElementsTool` — already performs its own selection
- `ZoomToElementTool` — non-modifying UI tool
- All read-only tools — no model changes to highlight

### 2. Markdown Rendering in Chat

AI responses now render with proper markdown formatting (bold, italic, lists, code blocks, tables) instead of plain text.

**Approach**:
- New `MarkdownBehavior` attached property converts markdown string → `FlowDocument` via existing `MarkdownService`
- Dual TextBox/RichTextBox in `ChatPane.xaml`:
  - `TextBox` visible during streaming (fast plain text updates)
  - `RichTextBox` visible after streaming completes (rich markdown rendering)
- Visibility-aware lazy conversion: skips Markdig pipeline while collapsed (streaming), applies when element becomes visible
- Theme colors inherited from RichTextBox (`Foreground`, `FontFamily`, `FontSize`)
- Cleans up event handlers on unload to prevent leaks in virtualized ListView

### 3. Status Bar (Pre-existing)

Status bar with `ShowStatus`/`StatusText` was already implemented in `ChatViewModel` + `ChatPane.xaml`. No additional work needed.

---

## Key Files

**New**:
- `src/RevitAI/UI/Behaviors/MarkdownBehavior.cs` — Attached property for markdown→FlowDocument conversion

**Modified**:
- `src/RevitAI/Tools/ToolResult.cs` — Added `AffectedElementIds` + `OkWithElements()`
- `src/RevitAI/Tools/ToolDispatcher.cs` — Added `SelectAffectedElements()` methods for single and batch paths
- `src/RevitAI/UI/ChatPane.xaml` — Dual TextBox/RichTextBox with streaming-aware visibility
- 21 tool files in `Tools/ModifyTools/` — `Ok()` → `OkWithElements()` with element IDs

---

## Deferred Items

- **Preview Graphics (DirectContext3D)**: Deferred per user decision. Would show ghost outlines before element creation.
- **Temporary Color Highlighting**: Deferred. Auto-selection provides sufficient visual feedback. Could add timed override graphics later if needed.
- **ViewTools auto-selection**: ViewTools like `CreateDraftingViewTool`, `DuplicateViewTool`, etc. were not updated. Could be added later if users want newly created views highlighted.

---

## Design Decisions

1. **Selection vs. Override Graphics**: Chose native selection (`SetElementIds`) over override graphics because it requires no transaction, no cleanup timer, and no undo pollution. Selection is automatically cleared when the user clicks elsewhere.

2. **Long IDs in ToolResult**: Used `long` (not `ElementId`) to keep `ToolResult` Revit-API-independent for testability.

3. **Lazy Markdown Conversion**: Streaming can fire dozens of content updates per second. Running Markdig on each would be wasteful. The dual-control approach with visibility-based lazy conversion avoids this cost entirely.

4. **ChangeElementType No-Op**: When the element is already the requested type, the tool returns plain `ToolResult.Ok()` (no selection) because nothing was modified.

---

## Verification (Manual)

1. **Markdown**: Send a message to the AI. Verify `**bold**` renders bold, `*italic*` renders italic, bullet lists render with bullets, code blocks render with monospace
2. **Streaming**: Verify streaming shows plain text, then switches to formatted markdown on completion
3. **Element selection**: Ask the AI to "place a wall from (0,0) to (10,0)". After creation, verify the wall appears selected (blue highlight) in the Revit viewport
4. **Batch selection**: Ask the AI to create multiple elements in one turn. Verify all created elements are selected after execution
5. **No undo pollution**: After a tool creates an element, verify Ctrl+Z undoes the element creation directly (no intermediate selection transactions)
6. **Theme check**: If dark theme is available, verify markdown text is readable
7. **Build**: `dotnet build src/RevitAI/RevitAI.csproj` — verify no errors
