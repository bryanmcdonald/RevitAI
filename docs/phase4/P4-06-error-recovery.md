# P4-06: Error Recovery & Adaptation

**Status**: Pending

**Goal**: Implement retry strategies, rollback capabilities, and user escalation for handling failures during agentic execution.

**Prerequisites**: P4-05 complete.

**Files Created**:
- `src/RevitAI/Services/RecoveryService.cs` - Error recovery logic

**Files Modified**:
- `src/RevitAI/Services/AgenticModeService.cs` - Integrate recovery strategies
- `src/RevitAI/Tools/ToolDispatcher.cs` - Add retry logic
- `src/RevitAI/UI/ChatViewModel.cs` - Handle escalation UI
- `src/RevitAI/Services/ContextEngine.cs` - Add recovery guidance to prompts

---

## Implementation Details

### 1. RecoveryService

```csharp
// src/RevitAI/Services/RecoveryService.cs

/// <summary>
/// Handles error recovery and adaptation during agentic execution.
/// </summary>
public class RecoveryService
{
    private readonly ConfigurationService _configService;
    private readonly AgenticModeService _agenticService;
    private readonly TransactionManager _transactionManager;

    public RecoveryService(
        ConfigurationService configService,
        AgenticModeService agenticService,
        TransactionManager transactionManager)
    {
        _configService = configService;
        _agenticService = agenticService;
        _transactionManager = transactionManager;
    }

    /// <summary>
    /// Analyzes a failure and determines recovery strategy.
    /// </summary>
    public RecoveryStrategy AnalyzeFailure(
        int stepNumber,
        string errorMessage,
        IRevitTool tool,
        JsonElement toolInput)
    {
        var session = _agenticService.GetOrCreateSession();
        var retryCount = session.RetryHistory.Count(r => r.StepNumber == stepNumber);

        // Check retry limit
        if (retryCount >= _configService.MaxRetries)
        {
            return new RecoveryStrategy
            {
                Action = RecoveryAction.EscalateToUser,
                Reason = $"Maximum retry attempts ({_configService.MaxRetries}) exceeded",
                Message = FormatEscalationMessage(stepNumber, errorMessage, retryCount)
            };
        }

        // Classify error and suggest strategy
        var errorType = ClassifyError(errorMessage);

        return errorType switch
        {
            ErrorType.InvalidParameter => new RecoveryStrategy
            {
                Action = RecoveryAction.RetryWithModification,
                Reason = "Invalid parameter detected",
                SuggestedModification = SuggestParameterFix(errorMessage, toolInput),
                RetryDelay = TimeSpan.Zero
            },

            ErrorType.ElementNotFound => new RecoveryStrategy
            {
                Action = RecoveryAction.RefreshContextAndRetry,
                Reason = "Referenced element not found",
                SuggestedModification = "Query for available elements before retrying",
                RetryDelay = TimeSpan.FromMilliseconds(500)
            },

            ErrorType.TypeNotAvailable => new RecoveryStrategy
            {
                Action = RecoveryAction.RetryWithAlternative,
                Reason = "Requested type not available",
                SuggestedModification = "Use get_available_types to find alternatives",
                RetryDelay = TimeSpan.Zero
            },

            ErrorType.GeometryConflict => new RecoveryStrategy
            {
                Action = RecoveryAction.SkipAndContinue,
                Reason = "Geometry conflict detected",
                Message = "Element conflicts with existing geometry. Skipping to avoid model corruption."
            },

            ErrorType.TransactionFailed => new RecoveryStrategy
            {
                Action = RecoveryAction.RollbackAndRetry,
                Reason = "Transaction failed",
                SuggestedModification = "Simplify operation or split into smaller steps",
                RetryDelay = TimeSpan.FromSeconds(1)
            },

            ErrorType.Timeout => new RecoveryStrategy
            {
                Action = RecoveryAction.RetryWithModification,
                Reason = "Operation timed out",
                SuggestedModification = "Reduce scope or simplify geometry",
                RetryDelay = TimeSpan.FromSeconds(2)
            },

            _ => new RecoveryStrategy
            {
                Action = RecoveryAction.EscalateToUser,
                Reason = "Unknown error type",
                Message = FormatEscalationMessage(stepNumber, errorMessage, retryCount)
            }
        };
    }

    /// <summary>
    /// Classifies an error message into a known category.
    /// </summary>
    private ErrorType ClassifyError(string errorMessage)
    {
        var msg = errorMessage.ToLowerInvariant();

        if (msg.Contains("parameter") || msg.Contains("value") || msg.Contains("invalid"))
            return ErrorType.InvalidParameter;

        if (msg.Contains("not found") || msg.Contains("does not exist") || msg.Contains("no element"))
            return ErrorType.ElementNotFound;

        if (msg.Contains("type") && (msg.Contains("not available") || msg.Contains("not loaded")))
            return ErrorType.TypeNotAvailable;

        if (msg.Contains("geometry") || msg.Contains("overlap") || msg.Contains("conflict") || msg.Contains("intersect"))
            return ErrorType.GeometryConflict;

        if (msg.Contains("transaction") || msg.Contains("rollback") || msg.Contains("commit"))
            return ErrorType.TransactionFailed;

        if (msg.Contains("timeout") || msg.Contains("timed out"))
            return ErrorType.Timeout;

        return ErrorType.Unknown;
    }

    /// <summary>
    /// Suggests a fix for parameter-related errors.
    /// </summary>
    private string SuggestParameterFix(string errorMessage, JsonElement input)
    {
        var suggestions = new List<string>();

        // Check for common issues
        if (errorMessage.Contains("level"))
        {
            suggestions.Add("Verify level exists using get_levels");
            suggestions.Add("Use exact level name from model");
        }

        if (errorMessage.Contains("coordinate") || errorMessage.Contains("point"))
        {
            suggestions.Add("Ensure coordinates are within model extents");
            suggestions.Add("Check units (should be in feet)");
        }

        if (errorMessage.Contains("type") || errorMessage.Contains("family"))
        {
            suggestions.Add("Query available types using get_available_types");
            suggestions.Add("Use exact type name from query result");
        }

        return suggestions.Any()
            ? string.Join("; ", suggestions)
            : "Review input parameters and try again";
    }

    /// <summary>
    /// Formats an escalation message for the user.
    /// </summary>
    private string FormatEscalationMessage(int stepNumber, string errorMessage, int retryCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"**Step {stepNumber} requires your attention**");
        sb.AppendLine();
        sb.AppendLine($"After {retryCount} attempt(s), I was unable to complete this step.");
        sb.AppendLine();
        sb.AppendLine("**Error:**");
        sb.AppendLine($"```");
        sb.AppendLine(errorMessage);
        sb.AppendLine($"```");
        sb.AppendLine();
        sb.AppendLine("**Options:**");
        sb.AppendLine("1. Provide guidance on how to proceed");
        sb.AppendLine("2. Skip this step and continue");
        sb.AppendLine("3. Abort the current plan");

        return sb.ToString();
    }

    /// <summary>
    /// Executes a recovery action.
    /// </summary>
    public async Task<RecoveryResult> ExecuteRecoveryAsync(
        RecoveryStrategy strategy,
        int stepNumber,
        CancellationToken ct)
    {
        _agenticService.AddNote($"Recovery: {strategy.Reason}", stepNumber);

        switch (strategy.Action)
        {
            case RecoveryAction.RetryWithModification:
            case RecoveryAction.RefreshContextAndRetry:
            case RecoveryAction.RetryWithAlternative:
                if (strategy.RetryDelay > TimeSpan.Zero)
                {
                    await Task.Delay(strategy.RetryDelay, ct);
                }
                return new RecoveryResult
                {
                    ShouldRetry = true,
                    ModificationHint = strategy.SuggestedModification
                };

            case RecoveryAction.RollbackAndRetry:
                // Rollback current transaction if active
                _transactionManager.RollbackIfActive();
                if (strategy.RetryDelay > TimeSpan.Zero)
                {
                    await Task.Delay(strategy.RetryDelay, ct);
                }
                return new RecoveryResult
                {
                    ShouldRetry = true,
                    RolledBack = true,
                    ModificationHint = strategy.SuggestedModification
                };

            case RecoveryAction.SkipAndContinue:
                _agenticService.UpdateStep(stepNumber, StepStatus.Skipped, failureReason: strategy.Reason);
                return new RecoveryResult
                {
                    ShouldRetry = false,
                    Skipped = true
                };

            case RecoveryAction.EscalateToUser:
                return new RecoveryResult
                {
                    ShouldRetry = false,
                    RequiresUserInput = true,
                    EscalationMessage = strategy.Message
                };

            default:
                return new RecoveryResult { ShouldRetry = false };
        }
    }

    /// <summary>
    /// Gets recovery guidance for the system prompt.
    /// </summary>
    public string GetRecoveryGuidance()
    {
        return """

            ## Error Recovery Guidelines

            When a tool call fails, analyze the error and adapt:

            ### Common Errors and Fixes

            | Error Type | Recovery Approach |
            |------------|-------------------|
            | "Element not found" | Query for valid elements before retrying |
            | "Type not available" | Use `get_available_types` to find alternatives |
            | "Invalid parameter" | Review parameter requirements and constraints |
            | "Geometry conflict" | Check for overlaps, adjust placement |
            | "Transaction failed" | Simplify operation, try smaller steps |

            ### Recovery Workflow

            1. **Analyze** the error message for root cause
            2. **Gather Info** using query tools if needed
            3. **Adapt** parameters or approach
            4. **Retry** with modified input
            5. **Escalate** if retries fail (use update_plan with fail_step)

            ### When to Skip vs. Retry

            - **Skip**: Geometry conflicts, optional elements, cosmetic issues
            - **Retry**: Parameter errors, missing references, type mismatches
            - **Escalate**: Unknown errors, repeated failures, critical steps

            ### Recording Failures

            Always use `update_plan` to record:
            - What was attempted
            - Why it failed
            - What was tried to fix it
            - Final outcome

            """;
    }
}

public class RecoveryStrategy
{
    public RecoveryAction Action { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? SuggestedModification { get; set; }
    public string? Message { get; set; }
    public TimeSpan RetryDelay { get; set; } = TimeSpan.Zero;
}

public class RecoveryResult
{
    public bool ShouldRetry { get; set; }
    public bool RolledBack { get; set; }
    public bool Skipped { get; set; }
    public bool RequiresUserInput { get; set; }
    public string? ModificationHint { get; set; }
    public string? EscalationMessage { get; set; }
}

public enum RecoveryAction
{
    RetryWithModification,
    RefreshContextAndRetry,
    RetryWithAlternative,
    RollbackAndRetry,
    SkipAndContinue,
    EscalateToUser
}

public enum ErrorType
{
    InvalidParameter,
    ElementNotFound,
    TypeNotAvailable,
    GeometryConflict,
    TransactionFailed,
    Timeout,
    Unknown
}
```

### 2. ToolDispatcher Integration

```csharp
// In ToolDispatcher.cs, add retry logic

public class ToolDispatcher
{
    private readonly RecoveryService _recoveryService;

    public async Task<ToolResultBlock> DispatchWithRecoveryAsync(
        ToolUseBlock toolUse,
        int stepNumber,
        CancellationToken ct)
    {
        var tool = _registry.Get(toolUse.Name);
        if (tool == null)
        {
            return CreateErrorResult(toolUse.Id, $"Unknown tool: {toolUse.Name}");
        }

        int attempt = 0;
        JsonElement currentInput = toolUse.Input;

        while (true)
        {
            attempt++;

            try
            {
                var result = await ExecuteToolAsync(tool, currentInput, ct);

                if (result.Success)
                {
                    return CreateSuccessResult(toolUse.Id, result);
                }

                // Tool returned error - analyze and potentially recover
                var strategy = _recoveryService.AnalyzeFailure(
                    stepNumber,
                    result.Error ?? "Unknown error",
                    tool,
                    currentInput
                );

                _agenticService.RecordRetry(
                    stepNumber,
                    attempt,
                    result.Error ?? "Unknown error",
                    strategy.SuggestedModification ?? "Retry"
                );

                var recovery = await _recoveryService.ExecuteRecoveryAsync(strategy, stepNumber, ct);

                if (!recovery.ShouldRetry)
                {
                    if (recovery.RequiresUserInput)
                    {
                        // Return special result that triggers escalation
                        return CreateEscalationResult(toolUse.Id, recovery.EscalationMessage!);
                    }

                    // Skipped or other terminal state
                    return CreateErrorResult(toolUse.Id, $"Recovery failed: {strategy.Reason}");
                }

                // Will retry - modification hint can be used by Claude
                // For now, just retry with same input after delay
            }
            catch (Exception ex)
            {
                return CreateErrorResult(toolUse.Id, $"Exception: {ex.Message}");
            }
        }
    }

    private ToolResultBlock CreateEscalationResult(string toolUseId, string message)
    {
        return new ToolResultBlock
        {
            ToolUseId = toolUseId,
            Content = $"[ESCALATION REQUIRED]\n\n{message}",
            IsError = true
        };
    }
}
```

### 3. User Escalation UI

```csharp
// In ChatViewModel.cs

private async Task HandleEscalation(string escalationMessage)
{
    // Add escalation message to chat
    var escalationChatMessage = new ChatMessage
    {
        Role = "assistant",
        Content = escalationMessage,
        Timestamp = DateTime.Now,
        IsEscalation = true
    };

    Messages.Add(escalationChatMessage);

    // Pause execution and wait for user response
    _isWaitingForEscalationResponse = true;

    // The next user message will be treated as escalation response
}

private async Task ProcessEscalationResponse(string userResponse)
{
    _isWaitingForEscalationResponse = false;

    var response = userResponse.ToLowerInvariant().Trim();

    if (response.Contains("skip") || response.Contains("continue"))
    {
        // Skip the failed step and continue
        var currentStep = _agenticService.GetOrCreateSession().CurrentPlan?.CurrentStepIndex;
        if (currentStep.HasValue)
        {
            _agenticService.UpdateStep(currentStep.Value + 1, StepStatus.Skipped,
                failureReason: "Skipped by user");
        }

        // Resume execution
        await ResumeAgenticExecutionAsync();
    }
    else if (response.Contains("abort") || response.Contains("stop") || response.Contains("cancel"))
    {
        // Abort the plan
        _agenticService.CompletePlan(PlanCompletionStatus.Cancelled, "Aborted by user");
        AddSystemMessage("Plan aborted. Let me know if you'd like to try a different approach.");
    }
    else
    {
        // User provided guidance - include in context and retry
        var guidanceMessage = $"[USER GUIDANCE]\n{userResponse}\n\nPlease retry the failed step using this guidance.";
        _conversationMessages.Add(ClaudeMessage.User(guidanceMessage));
        await ResumeAgenticExecutionAsync();
    }
}
```

### 4. System Prompt Recovery Section

```csharp
// In ContextEngine.cs

public string BuildSystemPrompt()
{
    var sb = new StringBuilder();

    // ... existing sections ...

    if (_configService.AgenticModeEnabled)
    {
        sb.AppendLine(_recoveryService.GetRecoveryGuidance());
    }

    return sb.ToString();
}
```

---

## Recovery Patterns

### Pattern 1: Parameter Retry

```
Tool call: place_wall with type="Basic Wall"
Error: "Type 'Basic Wall' not found"
       │
       ▼
Recovery: Query available types
       │
       ▼
Retry: place_wall with type="Generic - 8\""
       │
       ▼
Success
```

### Pattern 2: Context Refresh

```
Tool call: move_element with element_id=12345
Error: "Element 12345 does not exist"
       │
       ▼
Recovery: Selection may have changed
       │
       ▼
Query: get_selected_elements
       │
       ▼
Retry: move_element with element_id=67890 (current selection)
       │
       ▼
Success
```

### Pattern 3: Escalation

```
Tool call: place_column at grid intersection
Error: "Column conflicts with existing element"
Retry 1: Adjust position by 6 inches
Error: "Still conflicts"
Retry 2: Try different column type
Error: "Still conflicts"
       │
       ▼
Max retries exceeded
       │
       ▼
Escalate to user:
"I'm unable to place a column at grid A-1 due to
conflicts. Options:
1. Provide guidance
2. Skip this location
3. Abort plan"
       │
       ▼
User: "Skip this column and note it for later"
       │
       ▼
Skip step, add note, continue with next intersection
```

---

## Escalation Message Format

```markdown
**Step 3 requires your attention**

After 2 attempt(s), I was unable to complete this step.

**Error:**
```
Column conflicts with existing beam at elevation 12'-0"
```

**What I tried:**
1. Original placement at grid intersection
2. Offset by 6 inches east
3. Smaller column type (W10x33)

**Options:**
1. Provide guidance on how to proceed
2. Skip this step and continue
3. Abort the current plan

What would you like me to do?
```

---

## Configuration Options

```csharp
// In ApiSettings.cs

/// <summary>
/// Maximum retry attempts before escalating to user.
/// </summary>
public int MaxRetries { get; set; } = 2;

/// <summary>
/// Delay between retry attempts (milliseconds).
/// </summary>
public int RetryDelayMs { get; set; } = 500;

/// <summary>
/// Automatically skip non-critical steps on failure.
/// </summary>
public bool AutoSkipNonCritical { get; set; } = false;

/// <summary>
/// Types of errors that can be auto-skipped.
/// </summary>
public List<string> AutoSkipErrorTypes { get; set; } = new()
{
    "GeometryConflict"
};
```

---

## Verification (Manual)

1. **Build and deploy** with agentic mode enabled
2. **Trigger a recoverable error**:
   - Request: "Place a wall with type 'NonExistentType'"
   - Verify: Retry occurs with alternative type query
3. **Trigger max retries**:
   - Create a scenario that will fail repeatedly
   - Verify: Escalation message appears after max retries
4. **Test user responses**:
   - Respond with "skip" → verify step is skipped
   - Respond with "abort" → verify plan is cancelled
   - Respond with guidance → verify retry with guidance

---

## Logging and Diagnostics

```csharp
// Recovery attempts are logged for debugging

public class RecoveryLog
{
    public DateTime Timestamp { get; set; }
    public int StepNumber { get; set; }
    public string ToolName { get; set; }
    public string ErrorMessage { get; set; }
    public RecoveryAction ActionTaken { get; set; }
    public bool Succeeded { get; set; }
    public TimeSpan Duration { get; set; }
}

// Access via: _agenticService.GetOrCreateSession().RetryHistory
```

---

## Phase 4 Complete

Upon completing P4-06, the Agentic Mode feature is complete. Users can:

1. **Enable agentic mode** in settings
2. **Request complex operations** using natural language
3. **Watch Claude plan** the approach with structured steps
4. **Monitor progress** via the plan progress panel
5. **See automatic verification** after modifications
6. **Receive intelligent error recovery** with retries
7. **Provide guidance** when escalation is needed
8. **Review summary** of what was accomplished

---

## Future Enhancements

After Phase 4, consider:

- **Learning from failures**: Remember what didn't work across sessions
- **Plan templates**: Save successful plans as reusable templates
- **Parallel execution**: Execute independent steps concurrently
- **Confidence scoring**: Rate likelihood of success before execution
- **Undo integration**: "Undo last plan" as a single operation
