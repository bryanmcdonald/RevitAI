# P4-03: Agentic Session State

**Status**: Pending

**Goal**: Implement state management for tracking plan execution, step progress, and conversation-scoped persistence.

**Prerequisites**: P4-02 complete.

**Files Created**:
- `src/RevitAI/Models/AgenticSession.cs` - Session state model
- `src/RevitAI/Models/PlanStep.cs` - Individual step model
- `src/RevitAI/Services/AgenticModeService.cs` - Session state management

**Files Modified**:
- `src/RevitAI/UI/ChatViewModel.cs` - Integrate agentic session tracking
- `src/RevitAI/Tools/AgenticTools/CreatePlanTool.cs` - Store plan in session
- `src/RevitAI/Tools/AgenticTools/UpdatePlanTool.cs` - Update session state
- `src/RevitAI/Tools/AgenticTools/CompletePlanTool.cs` - Finalize session

---

## Implementation Details

### 1. PlanStep Model

```csharp
// src/RevitAI/Models/PlanStep.cs

/// <summary>
/// Represents a single step in an execution plan.
/// </summary>
public class PlanStep
{
    /// <summary>
    /// Step number (1-based).
    /// </summary>
    public int StepNumber { get; set; }

    /// <summary>
    /// Description of what this step accomplishes.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Current status of this step.
    /// </summary>
    public StepStatus Status { get; set; } = StepStatus.Pending;

    /// <summary>
    /// Tools that will be/were used in this step.
    /// </summary>
    public List<string> ToolsUsed { get; set; } = new();

    /// <summary>
    /// Success criteria for verification.
    /// </summary>
    public string? SuccessCriteria { get; set; }

    /// <summary>
    /// Steps that must complete before this one.
    /// </summary>
    public List<int> DependsOn { get; set; } = new();

    /// <summary>
    /// Whether this is a verification/QC step.
    /// </summary>
    public bool IsVerification { get; set; }

    /// <summary>
    /// Result or outcome after completion.
    /// </summary>
    public string? Result { get; set; }

    /// <summary>
    /// Reason for failure or skip.
    /// </summary>
    public string? FailureReason { get; set; }

    /// <summary>
    /// Element IDs created or modified in this step.
    /// </summary>
    public List<long> AffectedElementIds { get; set; } = new();

    /// <summary>
    /// When this step started execution.
    /// </summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// When this step completed/failed.
    /// </summary>
    public DateTime? CompletedAt { get; set; }
}

/// <summary>
/// Status of a plan step.
/// </summary>
public enum StepStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    Skipped
}
```

### 2. AgenticSession Model

```csharp
// src/RevitAI/Models/AgenticSession.cs

/// <summary>
/// Tracks the state of an agentic execution session.
/// Persists within a single conversation.
/// </summary>
public class AgenticSession
{
    /// <summary>
    /// Unique ID for this session.
    /// </summary>
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// Whether a plan is currently active.
    /// </summary>
    public bool HasActivePlan => CurrentPlan != null && !CurrentPlan.IsComplete;

    /// <summary>
    /// The current execution plan.
    /// </summary>
    public ExecutionPlan? CurrentPlan { get; set; }

    /// <summary>
    /// History of completed plans in this session.
    /// </summary>
    public List<ExecutionPlan> CompletedPlans { get; set; } = new();

    /// <summary>
    /// Notes and observations recorded during execution.
    /// </summary>
    public List<SessionNote> Notes { get; set; } = new();

    /// <summary>
    /// Retry attempts for failed operations.
    /// </summary>
    public List<RetryAttempt> RetryHistory { get; set; } = new();

    /// <summary>
    /// When this session started.
    /// </summary>
    public DateTime StartedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// Total elements created across all plans.
    /// </summary>
    public List<long> AllCreatedElementIds { get; set; } = new();

    /// <summary>
    /// Total elements modified across all plans.
    /// </summary>
    public List<long> AllModifiedElementIds { get; set; } = new();
}

/// <summary>
/// An execution plan with steps.
/// </summary>
public class ExecutionPlan
{
    public string PlanId { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Goal { get; set; } = string.Empty;
    public List<PlanStep> Steps { get; set; } = new();
    public string? VerificationApproach { get; set; }
    public string? RollbackStrategy { get; set; }
    public int EstimatedToolCalls { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? CompletedAt { get; set; }

    public PlanCompletionStatus? CompletionStatus { get; set; }
    public string? CompletionSummary { get; set; }

    public bool IsComplete => CompletionStatus.HasValue;

    public int CurrentStepIndex => Steps.FindIndex(s => s.Status == StepStatus.InProgress);
    public int CompletedStepCount => Steps.Count(s => s.Status == StepStatus.Completed);
    public int FailedStepCount => Steps.Count(s => s.Status == StepStatus.Failed);
    public int SkippedStepCount => Steps.Count(s => s.Status == StepStatus.Skipped);
    public int PendingStepCount => Steps.Count(s => s.Status == StepStatus.Pending);

    public PlanStep? GetStep(int stepNumber) => Steps.FirstOrDefault(s => s.StepNumber == stepNumber);
}

public enum PlanCompletionStatus
{
    Success,
    PartialSuccess,
    Failed,
    Cancelled
}

public class SessionNote
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Note { get; set; } = string.Empty;
    public int? RelatedStepNumber { get; set; }
}

public class RetryAttempt
{
    public int StepNumber { get; set; }
    public int AttemptNumber { get; set; }
    public string OriginalError { get; set; } = string.Empty;
    public string RetryApproach { get; set; } = string.Empty;
    public bool Succeeded { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
```

### 3. AgenticModeService

```csharp
// src/RevitAI/Services/AgenticModeService.cs

/// <summary>
/// Manages agentic mode session state.
/// </summary>
public class AgenticModeService
{
    private readonly ConfigurationService _configService;
    private AgenticSession? _currentSession;

    public AgenticModeService(ConfigurationService configService)
    {
        _configService = configService;
    }

    /// <summary>
    /// Gets or creates the current agentic session.
    /// </summary>
    public AgenticSession GetOrCreateSession()
    {
        _currentSession ??= new AgenticSession();
        return _currentSession;
    }

    /// <summary>
    /// Whether agentic mode is currently active.
    /// </summary>
    public bool IsAgenticModeActive =>
        _configService.AgenticModeEnabled && _currentSession?.HasActivePlan == true;

    /// <summary>
    /// Creates a new execution plan.
    /// </summary>
    public ExecutionPlan CreatePlan(
        string goal,
        List<PlanStep> steps,
        string? verificationApproach = null,
        string? rollbackStrategy = null,
        int estimatedToolCalls = 0)
    {
        var session = GetOrCreateSession();

        // Complete any existing plan first
        if (session.CurrentPlan != null && !session.CurrentPlan.IsComplete)
        {
            CompletePlan(PlanCompletionStatus.Cancelled, "Replaced by new plan");
        }

        var plan = new ExecutionPlan
        {
            Goal = goal,
            Steps = steps,
            VerificationApproach = verificationApproach,
            RollbackStrategy = rollbackStrategy,
            EstimatedToolCalls = estimatedToolCalls
        };

        session.CurrentPlan = plan;

        OnPlanCreated?.Invoke(this, plan);

        return plan;
    }

    /// <summary>
    /// Updates a step's status.
    /// </summary>
    public void UpdateStep(int stepNumber, StepStatus status, string? result = null, string? failureReason = null)
    {
        var plan = _currentSession?.CurrentPlan;
        var step = plan?.GetStep(stepNumber);

        if (step == null) return;

        step.Status = status;

        switch (status)
        {
            case StepStatus.InProgress:
                step.StartedAt = DateTime.Now;
                break;

            case StepStatus.Completed:
                step.CompletedAt = DateTime.Now;
                step.Result = result;
                break;

            case StepStatus.Failed:
                step.CompletedAt = DateTime.Now;
                step.FailureReason = failureReason;
                break;

            case StepStatus.Skipped:
                step.FailureReason = failureReason; // Reason for skip
                break;
        }

        OnStepUpdated?.Invoke(this, step);
    }

    /// <summary>
    /// Adds a new step to the current plan.
    /// </summary>
    public void AddStep(int afterStepNumber, string description)
    {
        var plan = _currentSession?.CurrentPlan;
        if (plan == null) return;

        var insertIndex = plan.Steps.FindIndex(s => s.StepNumber == afterStepNumber) + 1;

        // Renumber subsequent steps
        for (int i = insertIndex; i < plan.Steps.Count; i++)
        {
            plan.Steps[i].StepNumber++;
        }

        var newStep = new PlanStep
        {
            StepNumber = afterStepNumber + 1,
            Description = description,
            Status = StepStatus.Pending
        };

        plan.Steps.Insert(insertIndex, newStep);

        OnPlanModified?.Invoke(this, plan);
    }

    /// <summary>
    /// Records element IDs created in a step.
    /// </summary>
    public void RecordCreatedElements(int stepNumber, IEnumerable<long> elementIds)
    {
        var step = _currentSession?.CurrentPlan?.GetStep(stepNumber);
        if (step == null) return;

        step.AffectedElementIds.AddRange(elementIds);
        _currentSession!.AllCreatedElementIds.AddRange(elementIds);
    }

    /// <summary>
    /// Adds a note to the session.
    /// </summary>
    public void AddNote(string note, int? relatedStep = null)
    {
        var session = GetOrCreateSession();
        session.Notes.Add(new SessionNote
        {
            Note = note,
            RelatedStepNumber = relatedStep
        });
    }

    /// <summary>
    /// Records a retry attempt.
    /// </summary>
    public void RecordRetry(int stepNumber, int attemptNumber, string originalError, string retryApproach)
    {
        var session = GetOrCreateSession();
        session.RetryHistory.Add(new RetryAttempt
        {
            StepNumber = stepNumber,
            AttemptNumber = attemptNumber,
            OriginalError = originalError,
            RetryApproach = retryApproach
        });
    }

    /// <summary>
    /// Marks the retry as succeeded/failed.
    /// </summary>
    public void MarkRetryResult(int stepNumber, int attemptNumber, bool succeeded)
    {
        var attempt = _currentSession?.RetryHistory
            .FirstOrDefault(r => r.StepNumber == stepNumber && r.AttemptNumber == attemptNumber);

        if (attempt != null)
        {
            attempt.Succeeded = succeeded;
        }
    }

    /// <summary>
    /// Completes the current plan.
    /// </summary>
    public void CompletePlan(PlanCompletionStatus status, string summary)
    {
        var session = _currentSession;
        var plan = session?.CurrentPlan;

        if (plan == null) return;

        plan.CompletionStatus = status;
        plan.CompletionSummary = summary;
        plan.CompletedAt = DateTime.Now;

        session!.CompletedPlans.Add(plan);
        session.CurrentPlan = null;

        OnPlanCompleted?.Invoke(this, plan);
    }

    /// <summary>
    /// Clears the current session (new conversation).
    /// </summary>
    public void ResetSession()
    {
        _currentSession = null;
    }

    /// <summary>
    /// Gets current plan progress as a percentage.
    /// </summary>
    public double GetPlanProgress()
    {
        var plan = _currentSession?.CurrentPlan;
        if (plan == null || plan.Steps.Count == 0) return 0;

        var completed = plan.CompletedStepCount + plan.FailedStepCount + plan.SkippedStepCount;
        return (double)completed / plan.Steps.Count * 100;
    }

    // Events for UI binding
    public event EventHandler<ExecutionPlan>? OnPlanCreated;
    public event EventHandler<PlanStep>? OnStepUpdated;
    public event EventHandler<ExecutionPlan>? OnPlanModified;
    public event EventHandler<ExecutionPlan>? OnPlanCompleted;
}
```

### 4. Integration with ChatViewModel

```csharp
// In ChatViewModel.cs

public partial class ChatViewModel : ObservableObject
{
    private readonly AgenticModeService _agenticService;

    // Observable properties for UI
    [ObservableProperty]
    private bool _hasActivePlan;

    [ObservableProperty]
    private string _currentPlanGoal = string.Empty;

    [ObservableProperty]
    private int _planProgress;

    [ObservableProperty]
    private ObservableCollection<PlanStepViewModel> _planSteps = new();

    public ChatViewModel(
        // ... existing parameters ...,
        AgenticModeService agenticService)
    {
        _agenticService = agenticService;

        // Subscribe to agentic events
        _agenticService.OnPlanCreated += HandlePlanCreated;
        _agenticService.OnStepUpdated += HandleStepUpdated;
        _agenticService.OnPlanCompleted += HandlePlanCompleted;
    }

    private void HandlePlanCreated(object? sender, ExecutionPlan plan)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            HasActivePlan = true;
            CurrentPlanGoal = plan.Goal;
            PlanProgress = 0;

            PlanSteps.Clear();
            foreach (var step in plan.Steps)
            {
                PlanSteps.Add(new PlanStepViewModel(step));
            }
        });
    }

    private void HandleStepUpdated(object? sender, PlanStep step)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var vm = PlanSteps.FirstOrDefault(s => s.StepNumber == step.StepNumber);
            if (vm != null)
            {
                vm.UpdateFrom(step);
            }

            PlanProgress = (int)_agenticService.GetPlanProgress();
        });
    }

    private void HandlePlanCompleted(object? sender, ExecutionPlan plan)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            HasActivePlan = false;
            PlanProgress = 100;

            // Keep steps visible briefly, then clear
            Task.Delay(3000).ContinueWith(_ =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    PlanSteps.Clear();
                    CurrentPlanGoal = string.Empty;
                });
            });
        });
    }

    // Clear session when conversation is cleared
    [RelayCommand]
    private void ClearConversation()
    {
        // ... existing clear logic ...

        _agenticService.ResetSession();
        HasActivePlan = false;
        PlanSteps.Clear();
    }
}
```

### 5. Update Planning Tools to Use Service

```csharp
// In CreatePlanTool.cs

public class CreatePlanTool : IRevitTool
{
    private readonly AgenticModeService _agenticService;

    public CreatePlanTool(AgenticModeService agenticService)
    {
        _agenticService = agenticService;
    }

    public Task<ToolResult> ExecuteAsync(
        JsonElement input,
        UIApplication app,
        CancellationToken ct)
    {
        try
        {
            var goal = input.GetProperty("goal").GetString() ?? "Unspecified goal";
            var stepsJson = input.GetProperty("steps");

            // Parse steps
            var steps = new List<PlanStep>();
            foreach (var stepJson in stepsJson.EnumerateArray())
            {
                var step = new PlanStep
                {
                    StepNumber = stepJson.GetProperty("step_number").GetInt32(),
                    Description = stepJson.GetProperty("description").GetString() ?? "",
                };

                if (stepJson.TryGetProperty("tools_to_use", out var tools))
                {
                    step.ToolsUsed = tools.EnumerateArray()
                        .Select(t => t.GetString() ?? "")
                        .ToList();
                }

                if (stepJson.TryGetProperty("success_criteria", out var criteria))
                {
                    step.SuccessCriteria = criteria.GetString();
                }

                if (stepJson.TryGetProperty("is_verification", out var isVerify))
                {
                    step.IsVerification = isVerify.GetBoolean();
                }

                steps.Add(step);
            }

            // Create plan in service
            var plan = _agenticService.CreatePlan(
                goal,
                steps,
                input.TryGetProperty("verification_approach", out var v) ? v.GetString() : null,
                input.TryGetProperty("rollback_strategy", out var r) ? r.GetString() : null,
                input.TryGetProperty("estimated_tool_calls", out var e) ? e.GetInt32() : 0
            );

            // ... build response ...

            return Task.FromResult(ToolResult.Ok(response));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to create plan: {ex.Message}"));
        }
    }
}
```

---

## State Lifecycle

```
Conversation Start
        │
        ▼
┌───────────────────┐
│  No Active Plan   │ ◄─────────────────────────┐
│  Session Created  │                           │
└───────────────────┘                           │
        │                                       │
        │ create_plan                           │
        ▼                                       │
┌───────────────────┐                           │
│   Plan Active     │                           │
│   Steps: Pending  │                           │
└───────────────────┘                           │
        │                                       │
        │ update_plan (start_step)              │
        ▼                                       │
┌───────────────────┐                           │
│  Step In Progress │                           │
│   (1 at a time)   │                           │
└───────────────────┘                           │
        │                                       │
        │ update_plan (complete/fail)           │
        ▼                                       │
┌───────────────────┐                           │
│ Step Complete/Fail│                           │
│  Next Step Ready  │ ──────┐                   │
└───────────────────┘       │                   │
        │                   │ More steps        │
        │ All steps done    │                   │
        ▼                   │                   │
┌───────────────────┐       │                   │
│   complete_plan   │ ◄─────┘                   │
│   Summary Created │                           │
└───────────────────┘                           │
        │                                       │
        │ Plan archived                         │
        └───────────────────────────────────────┘
                  Ready for new plan
```

---

## Verification (Manual)

1. **Build and deploy** with agentic mode enabled
2. **Start a complex operation** that creates a plan
3. **Verify**:
   - Session is created
   - Plan state is tracked
   - Step updates reflect correctly
   - Plan completes and archives
4. **Clear conversation** and verify session resets
5. **Start a new plan** and verify old plan is in history

---

## Next Steps

After completing P4-03, proceed to **[P4-04: Auto-Verification Loop](P4-04-auto-verification.md)** to implement automatic result verification after modifications.
