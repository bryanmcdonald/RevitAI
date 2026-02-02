# Phase 4: Agentic Mode

> Each chunk represents a 1-2 day development session.
> **Phase 3 must be complete before starting Phase 4.**

---

## Overview

Phase 4 introduces **Agentic Mode**, enabling Claude to autonomously plan, execute, and verify complex multi-step Revit operations. Inspired by Claude Code's workflow, this phase transforms the assistant from a reactive tool-caller into a proactive agent that can:

- **Plan before acting** - Create structured execution plans for complex requests
- **Track progress** - Maintain task state and update users on completion status
- **Self-verify** - Capture screenshots and analyze results after modifications
- **Adapt and recover** - Retry failed operations with different approaches
- **Work autonomously** - Execute full workflows without step-by-step confirmation

Upon completion, users can request complex operations like "Create a structural grid with columns at all intersections, then verify the placement" and watch Claude plan, execute, verify, and report the results—all autonomously.

---

## Key Concepts

### What is Agentic Mode?

In standard mode, Claude responds to each message individually, calling tools as needed. In **Agentic Mode**, Claude:

1. Receives a complex request
2. Uses extended thinking to reason through the approach
3. Creates an explicit plan with numbered steps
4. Executes each step, updating progress
5. Verifies results using screenshots and model queries
6. Adapts if something fails
7. Reports a structured summary of what was accomplished

### Extended Thinking

Claude's extended thinking feature allows longer reasoning before responding. This is the foundation of planning—Claude can think through complex multi-step operations before taking any action.

### Planning Tools vs. Action Tools

| Tool Type | Purpose | Examples |
|-----------|---------|----------|
| **Planning Tools** | Create, update, and complete execution plans | `create_plan`, `update_plan`, `complete_plan` |
| **Action Tools** | Actually modify the Revit model | `place_wall`, `move_element`, `create_3d_view` |
| **Verification Tools** | Check results after actions | `capture_screenshot`, `get_element_properties` |

### User Control

Even in agentic mode, users maintain control:

- **Plan approval** - Optionally require approval before execution starts
- **Cancel anytime** - Stop button works mid-execution
- **Step confirmations** - High-risk steps can still require confirmation
- **Rollback per step** - Each plan step is a transaction group (single undo)

---

## Chunk Index

| Chunk | Name | Description | Prerequisites |
|-------|------|-------------|---------------|
| [P4-01](P4-01-extended-thinking.md) | Extended Thinking | Add thinking configuration to API, parse thinking blocks, expose settings | Phase 3 |
| [P4-02](P4-02-planning-tools.md) | Planning Tools | `create_plan`, `update_plan`, `complete_plan` tools for structured planning | P4-01 |
| [P4-03](P4-03-session-state.md) | Agentic Session State | Plan state management, step tracking, conversation-scoped persistence | P4-02 |
| [P4-04](P4-04-auto-verification.md) | Auto-Verification Loop | Automatic screenshot + analysis after modifications, quality gates | P4-03 |
| [P4-05](P4-05-agentic-ui.md) | Agentic UI | Plan progress panel, step visualization, status indicators | P4-04 |
| [P4-06](P4-06-error-recovery.md) | Error Recovery & Adaptation | Retry strategies, rollback capabilities, user escalation | P4-05 |

---

## Key Files Created in Phase 4

```
src/RevitAI/
├── Models/
│   ├── AgenticSession.cs            # P4-03: Plan and step state
│   ├── PlanStep.cs                  # P4-03: Individual step model
│   ├── ThinkingConfig.cs            # P4-01: Extended thinking settings
│   └── ThinkingContentBlock.cs      # P4-01: Thinking block parsing
├── Tools/
│   └── AgenticTools/                # P4-02
│       ├── CreatePlanTool.cs        # Create structured execution plan
│       ├── UpdatePlanTool.cs        # Track progress, adapt plan
│       └── CompletePlanTool.cs      # Mark plan complete with summary
├── Services/
│   ├── AgenticModeService.cs        # P4-03: Session state management
│   ├── VerificationService.cs       # P4-04: Auto-verification logic
│   └── RecoveryService.cs           # P4-06: Error recovery strategies
├── UI/
│   ├── PlanProgressPanel.xaml       # P4-05: Plan visualization
│   ├── PlanProgressPanel.xaml.cs    # P4-05
│   └── PlanProgressViewModel.cs     # P4-05: Plan display logic
└── ...
```

---

## Architecture Integration

### API Service Changes

The `ClaudeApiService` is extended to:

1. **Support thinking configuration** - New `thinking` parameter in requests
2. **Parse thinking blocks** - Handle `thinking` content blocks in responses
3. **Longer timeouts** - Extended thinking may take longer; adjust timeouts accordingly

### System Prompt Additions

When agentic mode is enabled, additional instructions guide Claude:

```
## Agentic Mode Active

You are operating in autonomous mode. Follow this workflow:

1. **PLAN FIRST**: For multi-step requests, use `create_plan` before executing
2. **EXECUTE SYSTEMATICALLY**: Work through steps in order, using `update_plan` to track
3. **VERIFY RESULTS**: After modifications, capture a screenshot and analyze
4. **ADAPT**: If something fails, analyze why and adjust approach
5. **REPORT COMPLETION**: Use `complete_plan` to summarize what was accomplished

Do NOT ask for user confirmation between steps unless:
- The plan needs significant revision
- An operation failed and you need guidance
- The request is genuinely ambiguous
```

### Tool Execution Flow

```
User: "Create grid with columns"
            │
            ▼
┌─────────────────────────────────────┐
│    Extended Thinking (10-30 sec)    │
│    Claude reasons through approach  │
└─────────────────────────────────────┘
            │
            ▼
┌─────────────────────────────────────┐
│    create_plan tool call            │
│    • Goal: Grid with columns        │
│    • Steps: 1-5 detailed steps      │
│    • Success criteria               │
└─────────────────────────────────────┘
            │
            ▼
┌─────────────────────────────────────┐
│    Execute Step 1                   │
│    • update_plan: step 1 started    │
│    • place_grid x5                  │
│    • update_plan: step 1 complete   │
└─────────────────────────────────────┘
            │
            ▼
         ... (steps 2-4) ...
            │
            ▼
┌─────────────────────────────────────┐
│    Verification Step                │
│    • capture_screenshot             │
│    • Analyze: does this look right? │
│    • If issues: retry or escalate   │
└─────────────────────────────────────┘
            │
            ▼
┌─────────────────────────────────────┐
│    complete_plan tool call          │
│    • Summary of accomplishments     │
│    • Any issues encountered         │
│    • Final status                   │
└─────────────────────────────────────┘
```

### Transaction Scope

Each plan step that modifies the model uses a transaction group:

- **Step-level undo**: Each step can be individually undone (Ctrl+Z)
- **Not plan-level**: Users can undo step 3 without losing steps 1-2
- **Rollback on failure**: If a step fails, all its changes are rolled back

---

## Configuration Options

| Setting | Default | Description |
|---------|---------|-------------|
| `AgenticModeEnabled` | `false` | Enable/disable agentic mode globally |
| `ThinkingBudgetTokens` | `10000` | Max tokens for extended thinking |
| `RequirePlanApproval` | `false` | Show plan for approval before execution |
| `AutoVerification` | `true` | Auto-capture screenshots after modifications |
| `MaxRetries` | `2` | Max retry attempts per failed step |

### Settings Migration

When users upgrade from Phase 1-3 to Phase 4, the new agentic settings are automatically added with their default values. The `ConfigurationService` handles missing properties gracefully:

```csharp
// In ConfigurationService.LoadSettings(), new properties get defaults
public ApiSettings LoadSettings()
{
    var settings = LoadFromFile() ?? new ApiSettings();

    // New Phase 4 settings will be null/default if upgrading from earlier version
    // Defaults are defined in ApiSettings class, so no migration code needed
    return settings;
}
```

### Agentic Mode Indicator

When agentic mode is enabled, the UI should indicate this to users so they understand Claude is operating autonomously. Consider adding a visual indicator in the chat header:

```xml
<!-- In ChatPane.xaml header -->
<StackPanel Orientation="Horizontal" Visibility="{Binding IsAgenticModeEnabled, Converter={StaticResource BoolToVis}}">
    <TextBlock Text="[AUTO]" Foreground="#FFA500" FontWeight="Bold" Margin="8,0"/>
    <TextBlock Text="Agentic Mode" Foreground="#888888" FontSize="11"/>
</StackPanel>
```

---

## Phase 4 Completion Criteria

- [ ] Extended thinking works with configurable token budget (P4-01)
- [ ] Thinking content is captured but not shown in chat by default (P4-01)
- [ ] Planning tools create and track structured execution plans (P4-02)
- [ ] Plan state persists within conversation scope (P4-03)
- [ ] Auto-verification captures and analyzes results after modifications (P4-04)
- [ ] UI shows plan progress with step-by-step status (P4-05)
- [ ] Failed steps trigger retry or user escalation (P4-06)
- [ ] Complex multi-step requests execute autonomously (end-to-end)
- [ ] Each plan step is a separate undo operation
- [ ] User can cancel at any point during agentic execution

---

## Example Workflows

### Structural Grid with Columns

**User**: "Create a 4x4 structural grid at 30' spacing, then place W10x49 columns at all intersections"

**Claude's Plan**:
1. Create 5 vertical grid lines (A-E) at 30' spacing
2. Create 5 horizontal grid lines (1-5) at 30' spacing
3. Query all grid intersections
4. Place W10x49 columns at each of 25 intersections
5. Verify placement with screenshot

**Execution**: Each step updates progress UI, final screenshot verifies layout.

### View Setup for Documentation

**User**: "Set up documentation for Level 1: floor plan, reflected ceiling plan, and 4 elevations, then place them all on a new sheet"

**Claude's Plan**:
1. Create Level 1 floor plan view
2. Create Level 1 RCP view
3. Create 4 elevation views (North, South, East, West)
4. Create new sheet
5. Place all 6 views on sheet with appropriate spacing
6. Capture screenshot of sheet

---

## Relationship to Other Phases

| Phase | How Phase 4 Builds on It |
|-------|-------------------------|
| Phase 1 | Uses existing tool framework, threading, transactions |
| Phase 1.5 | Uses screenshot capture for verification |
| Phase 2 | Uses multi-step operations, smart context |
| Phase 3 | Uses discipline-specific tools in complex plans |

---

## Future Considerations

After Phase 4, potential enhancements include:

- **Learning from failures** - Remember what didn't work across sessions
- **Plan templates** - Save successful plans as reusable templates
- **Parallel execution** - Execute independent steps concurrently
- **User coaching** - Explain decisions and teach Revit concepts
- **Collaborative planning** - Allow user to modify plan before execution

---

## Next Steps

When ready to implement Phase 4, start with **[P4-01: Extended Thinking](P4-01-extended-thinking.md)**.
