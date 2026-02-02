# P4-02: Planning Tools

**Status**: Pending

**Goal**: Create tools that allow Claude to create, update, and complete structured execution plans for complex multi-step operations.

**Prerequisites**: P4-01 complete.

**Files Created**:
- `src/RevitAI/Tools/AgenticTools/CreatePlanTool.cs` - Create structured execution plan
- `src/RevitAI/Tools/AgenticTools/UpdatePlanTool.cs` - Track progress and adapt plan
- `src/RevitAI/Tools/AgenticTools/CompletePlanTool.cs` - Mark plan complete with summary

**Files Modified**:
- `src/RevitAI/App.cs` - Register agentic tools
- `src/RevitAI/Tools/ToolRegistry.cs` - Conditional registration for agentic tools

---

## Implementation Details

### 1. CreatePlanTool

This tool allows Claude to create a structured plan before executing complex operations:

```csharp
// src/RevitAI/Tools/AgenticTools/CreatePlanTool.cs

public class CreatePlanTool : IRevitTool
{
    public string Name => "create_plan";

    public string Description => @"Create a structured execution plan before performing complex multi-step operations.
Use this tool when a user request requires multiple tools, careful sequencing, or verification steps.
The plan helps track progress and ensures all steps are completed successfully.";

    public bool RequiresTransaction => false;
    public bool RequiresConfirmation => false;  // Plan approval handled separately

    public JsonElement InputSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "goal": {
                "type": "string",
                "description": "The overall objective this plan will accomplish"
            },
            "steps": {
                "type": "array",
                "description": "Ordered list of steps to execute",
                "items": {
                    "type": "object",
                    "properties": {
                        "step_number": {
                            "type": "integer",
                            "description": "Step number (1-based)"
                        },
                        "description": {
                            "type": "string",
                            "description": "What this step accomplishes"
                        },
                        "tools_to_use": {
                            "type": "array",
                            "items": { "type": "string" },
                            "description": "Tool names that will be called in this step"
                        },
                        "success_criteria": {
                            "type": "string",
                            "description": "How to verify this step succeeded"
                        },
                        "depends_on": {
                            "type": "array",
                            "items": { "type": "integer" },
                            "description": "Step numbers that must complete first"
                        },
                        "is_verification": {
                            "type": "boolean",
                            "description": "True if this is a verification/QC step"
                        }
                    },
                    "required": ["step_number", "description"]
                }
            },
            "verification_approach": {
                "type": "string",
                "description": "How results will be verified (e.g., 'screenshot and visual inspection')"
            },
            "estimated_tool_calls": {
                "type": "integer",
                "description": "Approximate number of tool calls needed"
            },
            "rollback_strategy": {
                "type": "string",
                "description": "How to handle failures (e.g., 'undo step and retry with different approach')"
            }
        },
        "required": ["goal", "steps"]
    }
    """).RootElement;

    public string GetDryRunDescription(JsonElement input)
    {
        var goal = input.TryGetProperty("goal", out var g) ? g.GetString() : "unknown goal";
        var stepCount = input.TryGetProperty("steps", out var s) ? s.GetArrayLength() : 0;
        return $"Would create plan for '{goal}' with {stepCount} steps.";
    }

    public Task<ToolResult> ExecuteAsync(
        JsonElement input,
        UIApplication app,
        CancellationToken ct)
    {
        try
        {
            var goal = input.GetProperty("goal").GetString() ?? "Unspecified goal";
            var steps = input.GetProperty("steps");
            var stepCount = steps.GetArrayLength();

            // Parse and validate steps
            var planSteps = new List<string>();
            foreach (var step in steps.EnumerateArray())
            {
                var num = step.GetProperty("step_number").GetInt32();
                var desc = step.GetProperty("description").GetString();
                planSteps.Add($"  {num}. {desc}");
            }

            var verification = input.TryGetProperty("verification_approach", out var v)
                ? v.GetString()
                : "Visual verification";

            // Build response
            var sb = new StringBuilder();
            sb.AppendLine($"## Execution Plan Created");
            sb.AppendLine();
            sb.AppendLine($"**Goal**: {goal}");
            sb.AppendLine();
            sb.AppendLine($"**Steps** ({stepCount}):");
            foreach (var step in planSteps)
            {
                sb.AppendLine(step);
            }
            sb.AppendLine();
            sb.AppendLine($"**Verification**: {verification}");
            sb.AppendLine();
            sb.AppendLine("Plan is ready. Proceeding with execution...");

            // The AgenticModeService will pick up this plan from the tool result
            // and store it in session state (handled in ChatViewModel)

            return Task.FromResult(ToolResult.Ok(sb.ToString()));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to create plan: {ex.Message}"));
        }
    }
}
```

### 2. UpdatePlanTool

This tool allows Claude to update progress and adapt the plan during execution:

```csharp
// src/RevitAI/Tools/AgenticTools/UpdatePlanTool.cs

public class UpdatePlanTool : IRevitTool
{
    public string Name => "update_plan";

    public string Description => @"Update the current plan's progress. Use this to:
- Mark a step as started (in_progress)
- Mark a step as completed with result
- Mark a step as failed with reason
- Add new steps discovered during execution
- Skip steps that are no longer needed
- Record observations or learnings";

    public bool RequiresTransaction => false;
    public bool RequiresConfirmation => false;

    public JsonElement InputSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "action": {
                "type": "string",
                "enum": ["start_step", "complete_step", "fail_step", "skip_step", "add_step", "note"],
                "description": "The type of update to make"
            },
            "step_number": {
                "type": "integer",
                "description": "The step number being updated"
            },
            "result": {
                "type": "string",
                "description": "Result or outcome of the step (for complete_step)"
            },
            "reason": {
                "type": "string",
                "description": "Reason for failure or skip (for fail_step, skip_step)"
            },
            "new_step": {
                "type": "object",
                "description": "New step to add (for add_step action)",
                "properties": {
                    "description": { "type": "string" },
                    "after_step": { "type": "integer" }
                }
            },
            "note": {
                "type": "string",
                "description": "Observation or learning to record (for note action)"
            }
        },
        "required": ["action"]
    }
    """).RootElement;

    public string GetDryRunDescription(JsonElement input)
    {
        var action = input.TryGetProperty("action", out var a) ? a.GetString() : "unknown";
        var step = input.TryGetProperty("step_number", out var s) ? s.GetInt32() : 0;
        return $"Would {action} for step {step}.";
    }

    public Task<ToolResult> ExecuteAsync(
        JsonElement input,
        UIApplication app,
        CancellationToken ct)
    {
        try
        {
            var action = input.GetProperty("action").GetString();

            var sb = new StringBuilder();

            switch (action)
            {
                case "start_step":
                    var startStep = input.GetProperty("step_number").GetInt32();
                    sb.AppendLine($"**Step {startStep}**: Started");
                    break;

                case "complete_step":
                    var completeStep = input.GetProperty("step_number").GetInt32();
                    var result = input.TryGetProperty("result", out var r)
                        ? r.GetString()
                        : "Completed successfully";
                    sb.AppendLine($"**Step {completeStep}**: Completed");
                    sb.AppendLine($"Result: {result}");
                    break;

                case "fail_step":
                    var failStep = input.GetProperty("step_number").GetInt32();
                    var reason = input.TryGetProperty("reason", out var fr)
                        ? fr.GetString()
                        : "Unknown reason";
                    sb.AppendLine($"**Step {failStep}**: Failed");
                    sb.AppendLine($"Reason: {reason}");
                    break;

                case "skip_step":
                    var skipStep = input.GetProperty("step_number").GetInt32();
                    var skipReason = input.TryGetProperty("reason", out var sr)
                        ? sr.GetString()
                        : "No longer needed";
                    sb.AppendLine($"**Step {skipStep}**: Skipped");
                    sb.AppendLine($"Reason: {skipReason}");
                    break;

                case "add_step":
                    var newStep = input.GetProperty("new_step");
                    var desc = newStep.GetProperty("description").GetString();
                    var afterStep = newStep.TryGetProperty("after_step", out var after)
                        ? after.GetInt32()
                        : 0;
                    sb.AppendLine($"**New Step Added** (after step {afterStep}):");
                    sb.AppendLine($"  {desc}");
                    break;

                case "note":
                    var note = input.GetProperty("note").GetString();
                    sb.AppendLine($"**Note**: {note}");
                    break;

                default:
                    return Task.FromResult(ToolResult.Error($"Unknown action: {action}"));
            }

            return Task.FromResult(ToolResult.Ok(sb.ToString()));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to update plan: {ex.Message}"));
        }
    }
}
```

### 3. CompletePlanTool

This tool marks the plan as complete and provides a summary:

```csharp
// src/RevitAI/Tools/AgenticTools/CompletePlanTool.cs

public class CompletePlanTool : IRevitTool
{
    public string Name => "complete_plan";

    public string Description => @"Mark the current plan as complete and provide a summary of what was accomplished.
Use this as the final step after all planned work is done.
Include any issues encountered and their resolutions.";

    public bool RequiresTransaction => false;
    public bool RequiresConfirmation => false;

    public JsonElement InputSchema => JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "status": {
                "type": "string",
                "enum": ["success", "partial_success", "failed"],
                "description": "Overall completion status"
            },
            "summary": {
                "type": "string",
                "description": "Summary of what was accomplished"
            },
            "steps_completed": {
                "type": "integer",
                "description": "Number of steps that completed successfully"
            },
            "steps_failed": {
                "type": "integer",
                "description": "Number of steps that failed"
            },
            "steps_skipped": {
                "type": "integer",
                "description": "Number of steps that were skipped"
            },
            "issues_encountered": {
                "type": "array",
                "items": { "type": "string" },
                "description": "List of issues that were encountered"
            },
            "elements_created": {
                "type": "array",
                "items": { "type": "integer" },
                "description": "Element IDs of newly created elements"
            },
            "elements_modified": {
                "type": "array",
                "items": { "type": "integer" },
                "description": "Element IDs of modified elements"
            },
            "recommendations": {
                "type": "string",
                "description": "Any follow-up recommendations for the user"
            }
        },
        "required": ["status", "summary"]
    }
    """).RootElement;

    public string GetDryRunDescription(JsonElement input)
    {
        var status = input.TryGetProperty("status", out var s) ? s.GetString() : "unknown";
        return $"Would complete plan with status: {status}.";
    }

    public Task<ToolResult> ExecuteAsync(
        JsonElement input,
        UIApplication app,
        CancellationToken ct)
    {
        try
        {
            var status = input.GetProperty("status").GetString();
            var summary = input.GetProperty("summary").GetString();

            var sb = new StringBuilder();
            sb.AppendLine("---");
            sb.AppendLine("## Plan Completed");
            sb.AppendLine();

            // Status indicator
            var statusEmoji = status switch
            {
                "success" => "[SUCCESS]",
                "partial_success" => "[PARTIAL]",
                "failed" => "[FAILED]",
                _ => "[UNKNOWN]"
            };
            sb.AppendLine($"**Status**: {statusEmoji} {status}");
            sb.AppendLine();

            // Summary
            sb.AppendLine($"**Summary**: {summary}");
            sb.AppendLine();

            // Step counts
            if (input.TryGetProperty("steps_completed", out var completed))
            {
                sb.AppendLine($"- Steps completed: {completed.GetInt32()}");
            }
            if (input.TryGetProperty("steps_failed", out var failed) && failed.GetInt32() > 0)
            {
                sb.AppendLine($"- Steps failed: {failed.GetInt32()}");
            }
            if (input.TryGetProperty("steps_skipped", out var skipped) && skipped.GetInt32() > 0)
            {
                sb.AppendLine($"- Steps skipped: {skipped.GetInt32()}");
            }

            // Issues
            if (input.TryGetProperty("issues_encountered", out var issues) &&
                issues.GetArrayLength() > 0)
            {
                sb.AppendLine();
                sb.AppendLine("**Issues Encountered**:");
                foreach (var issue in issues.EnumerateArray())
                {
                    sb.AppendLine($"  - {issue.GetString()}");
                }
            }

            // Created elements
            if (input.TryGetProperty("elements_created", out var created) &&
                created.GetArrayLength() > 0)
            {
                sb.AppendLine();
                var ids = created.EnumerateArray().Select(e => e.GetInt64()).ToList();
                sb.AppendLine($"**Elements Created**: {ids.Count} elements");
                sb.AppendLine($"  IDs: {string.Join(", ", ids.Take(10))}");
                if (ids.Count > 10)
                {
                    sb.AppendLine($"  ... and {ids.Count - 10} more");
                }
            }

            // Modified elements
            if (input.TryGetProperty("elements_modified", out var modified) &&
                modified.GetArrayLength() > 0)
            {
                var ids = modified.EnumerateArray().Select(e => e.GetInt64()).ToList();
                sb.AppendLine($"**Elements Modified**: {ids.Count} elements");
            }

            // Recommendations
            if (input.TryGetProperty("recommendations", out var recs) &&
                !string.IsNullOrEmpty(recs.GetString()))
            {
                sb.AppendLine();
                sb.AppendLine($"**Recommendations**: {recs.GetString()}");
            }

            sb.AppendLine();
            sb.AppendLine("---");

            return Task.FromResult(ToolResult.Ok(sb.ToString()));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to complete plan: {ex.Message}"));
        }
    }
}
```

### 4. Tool Registration

Register agentic tools conditionally:

```csharp
// In App.cs, update RegisterTools()

private static void RegisterTools()
{
    var registry = ToolRegistry.Instance;

    // ... existing tool registrations ...

    // Agentic tools (always registered, but only included in requests when enabled)
    registry.Register(new CreatePlanTool(), isAgenticTool: true);
    registry.Register(new UpdatePlanTool(), isAgenticTool: true);
    registry.Register(new CompletePlanTool(), isAgenticTool: true);
}

// In ToolRegistry.cs

public class ToolRegistry
{
    private readonly Dictionary<string, IRevitTool> _tools = new();
    private readonly HashSet<string> _agenticTools = new();

    public void Register(IRevitTool tool, bool isAgenticTool = false)
    {
        _tools[tool.Name] = tool;
        if (isAgenticTool)
        {
            _agenticTools.Add(tool.Name);
        }
    }

    public IEnumerable<ToolDefinition> GetDefinitions(bool includeAgenticTools = false)
    {
        return _tools
            .Where(kvp => includeAgenticTools || !_agenticTools.Contains(kvp.Key))
            .Select(kvp => new ToolDefinition
            {
                Name = kvp.Value.Name,
                Description = kvp.Value.Description,
                InputSchema = kvp.Value.InputSchema
            });
    }
}
```

---

## System Prompt Additions

When agentic mode is enabled, add planning instructions to the system prompt:

```csharp
// In ContextEngine.cs

private string GetAgenticModeInstructions()
{
    if (!_configService.AgenticModeEnabled)
        return string.Empty;

    return """

        ## Agentic Mode

        You are operating in autonomous mode. For complex multi-step requests:

        1. **PLAN FIRST**: Use `create_plan` to outline your approach before executing
        2. **UPDATE PROGRESS**: Use `update_plan` to mark steps as started/completed/failed
        3. **VERIFY RESULTS**: After modifications, verify success (screenshots, queries)
        4. **COMPLETE PLAN**: Use `complete_plan` to summarize what was accomplished

        ### When to Create a Plan

        Create a plan when the request involves:
        - More than 3 tool calls
        - Multiple related element creations
        - Sequential dependencies (B requires A to complete first)
        - Verification or QC requirements

        Do NOT create a plan for:
        - Simple queries (get_levels, get_selected_elements)
        - Single element operations
        - Quick lookups

        ### Plan Best Practices

        - Include verification steps (screenshots after modifications)
        - Note dependencies between steps
        - Estimate tool call count for user expectations
        - Define success criteria for each step
        - Have a rollback strategy for failures

        """;
}
```

---

## Verification (Manual)

1. **Build and deploy** with agentic mode enabled
2. **Send a complex request**: "Create 3 levels: Basement at -10', Ground at 0', Level 2 at 12'"
3. **Verify**:
   - Claude calls `create_plan` first
   - Plan is displayed with numbered steps
   - Claude calls `update_plan` as it works
   - `complete_plan` is called at the end with summary
4. **Test failure handling**:
   - Request something that will partially fail
   - Verify `update_plan` records the failure
   - Verify `complete_plan` reports partial success

---

## Example Interaction

**User**: "Create a structural grid with 3 gridlines in each direction at 25' spacing"

**Claude** (with extended thinking):
```
[Thinking through the approach...]
```

**Claude** (tool call):
```json
{
    "name": "create_plan",
    "input": {
        "goal": "Create 3x3 structural grid at 25' spacing",
        "steps": [
            {
                "step_number": 1,
                "description": "Create vertical grid lines A, B, C at 25' spacing",
                "tools_to_use": ["place_grid"],
                "success_criteria": "3 vertical grids created"
            },
            {
                "step_number": 2,
                "description": "Create horizontal grid lines 1, 2, 3 at 25' spacing",
                "tools_to_use": ["place_grid"],
                "success_criteria": "3 horizontal grids created"
            },
            {
                "step_number": 3,
                "description": "Verify grid layout",
                "tools_to_use": ["capture_screenshot"],
                "success_criteria": "Screenshot shows 3x3 grid pattern",
                "is_verification": true
            }
        ],
        "verification_approach": "Screenshot capture and visual inspection",
        "estimated_tool_calls": 7
    }
}
```

---

## Next Steps

After completing P4-02, proceed to **[P4-03: Agentic Session State](P4-03-session-state.md)** to implement persistent plan state management within conversations.
