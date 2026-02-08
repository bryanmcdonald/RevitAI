# P2-03: Multi-Step Design Operations

**Goal**: Improve undo behavior for multi-tool AI operations.

**Prerequisites**: P2-02 complete.

**Status**: Complete

---

## Problem

When the AI executes a complex request like "create a structural bay with 4 columns and beams," it uses multiple tool rounds (round 1: query levels/types, round 2: place columns, round 3: place beams). Each round dispatches tools that may create separate undo entries.

## Solution

### Within-Round Batching (already existed, cleaned up)

When the AI issues multiple tools in a single round (e.g., 4 `place_column` calls at once), `ToolDispatcher.ExecuteAllToolsInGroupAsync` wraps them in a TransactionGroup. This was already implemented; P2-03 cleaned up the code with `AnyToolRequiresTransaction()` and the `externalGroup` parameter for future extensibility.

### System Prompt Guidance (new)

The system prompt now instructs the AI to prefer issuing all related modifications in the SAME tool round when possible, so they group into one Ctrl+Z. For example, "place all columns in one round rather than one per round."

### Cross-Round Grouping Limitation

Cross-round TransactionGroup wrapping was attempted but **Revit auto-closes uncommitted TransactionGroups when each ExternalEvent handler returns**. Since each `App.ExecuteOnRevitThreadAsync` call is a separate handler invocation, a group started in one call is invalid by the next. This is a fundamental constraint of the ExternalEvent architecture.

**Practical impact**: If the AI uses round 1 to query and round 2 to modify, each modification round is a separate undo entry. However, with the system prompt guidance, the AI batches related modifications into fewer rounds, minimizing the number of Ctrl+Z presses needed.

### Key Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Cross-round grouping | Not possible | Revit auto-closes TransactionGroups between ExternalEvent handler calls |
| Within-round batching | Preserved (cleaned up) | Works correctly within a single handler call |
| System prompt | Guide AI to batch modifications | Minimizes separate undo entries without fighting Revit's architecture |
| `externalGroup` param | Added for future use | If a different execution model enables cross-round grouping later |

## Files Modified

| File | Change |
|------|--------|
| `src/RevitAI/UI/ChatViewModel.cs` | Documented cross-round limitation in method comment |
| `src/RevitAI/Tools/ToolDispatcher.cs` | `AnyToolRequiresTransaction()` helper, `externalGroup` param on `ExecuteAllToolsInGroupAsync`, routing logic cleanup in `DispatchAllAsync` |
| `src/RevitAI/Services/ContextEngine.cs` | Multi-step operations guidance in system prompt |
| `CLAUDE.md` | Added Code Review step to Post-Change Requirements; updated next chunk pointer |
| `README.md` | P2-03 checkbox |

No new files. No changes to `TransactionManager.cs`.

## Verification (Manual)

1. **Within-round batching**: Ask AI to "place 4 columns at 25' spacing" — if AI issues all 4 in one round, Ctrl+Z undoes all 4 at once
2. **Multi-round operation**: "Copy this column 20 feet south, then rotate it 90 degrees" — works correctly (may be 2 undo entries if AI uses separate rounds)
3. **Single-tool unchanged**: Simple single-tool requests work as before
4. **System prompt effect**: AI should prefer batching related modifications in the same tool round
