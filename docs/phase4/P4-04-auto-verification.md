# P4-04: Auto-Verification Loop

**Status**: Pending

**Goal**: Implement automatic verification after modifications, enabling Claude to "see" results and assess success before proceeding.

**Prerequisites**: P4-03 complete.

**Files Created**:
- `src/RevitAI/Services/VerificationService.cs` - Verification logic and triggers

**Files Modified**:
- `src/RevitAI/UI/ChatViewModel.cs` - Integrate verification loop
- `src/RevitAI/Services/ContextEngine.cs` - Add verification prompts
- `src/RevitAI/Models/ApiSettings.cs` - Add verification settings

---

## Implementation Details

### 1. Verification Service

```csharp
// src/RevitAI/Services/VerificationService.cs

/// <summary>
/// Handles automatic verification after model modifications.
/// </summary>
public class VerificationService
{
    private readonly ConfigurationService _configService;
    private readonly AgenticModeService _agenticService;

    public VerificationService(
        ConfigurationService configService,
        AgenticModeService agenticService)
    {
        _configService = configService;
        _agenticService = agenticService;
    }

    /// <summary>
    /// Determines if verification should occur after tool execution.
    /// </summary>
    public bool ShouldVerifyAfterTools(IEnumerable<ToolResult> results, IEnumerable<IRevitTool> tools)
    {
        // Only verify in agentic mode with auto-verification enabled
        if (!_configService.AgenticModeEnabled || !_configService.AutoVerification)
            return false;

        // Only verify if any tools modified the model
        var hasModifications = tools.Any(t => t.RequiresTransaction);
        if (!hasModifications)
            return false;

        // Only verify if tools succeeded
        var hasSuccess = results.Any(r => r.Success);
        if (!hasSuccess)
            return false;

        // Check if current step is NOT a verification step (avoid infinite loop)
        var currentStep = _agenticService.GetOrCreateSession().CurrentPlan?.Steps
            .FirstOrDefault(s => s.Status == StepStatus.InProgress);

        if (currentStep?.IsVerification == true)
            return false;

        return true;
    }

    /// <summary>
    /// Gets the verification prompt to append to the conversation.
    /// </summary>
    public string GetVerificationPrompt(
        List<string> toolNames,
        List<long> affectedElementIds)
    {
        var sb = new StringBuilder();

        sb.AppendLine();
        sb.AppendLine("[VERIFICATION CHECKPOINT]");
        sb.AppendLine();
        sb.AppendLine($"Tools just executed: {string.Join(", ", toolNames)}");

        if (affectedElementIds.Any())
        {
            sb.AppendLine($"Elements affected: {affectedElementIds.Count} (IDs: {string.Join(", ", affectedElementIds.Take(5))}{(affectedElementIds.Count > 5 ? "..." : "")})");
        }

        sb.AppendLine();
        sb.AppendLine("**Verification Instructions:**");
        sb.AppendLine("1. Capture a screenshot of the current state using `capture_screenshot`");
        sb.AppendLine("2. Analyze whether the result matches the intended outcome");
        sb.AppendLine("3. If issues are found:");
        sb.AppendLine("   - Minor issues: Proceed with a fix attempt");
        sb.AppendLine("   - Major issues: Update the plan and potentially retry");
        sb.AppendLine("   - Blocking issues: Escalate to user");
        sb.AppendLine("4. If successful, update the plan and proceed to next step");

        return sb.ToString();
    }

    /// <summary>
    /// Analyzes a verification result and suggests next action.
    /// </summary>
    public VerificationResult AnalyzeVerification(
        bool userApproved,
        string? issueDescription,
        int retryAttempts)
    {
        if (userApproved)
        {
            return new VerificationResult
            {
                Status = VerificationStatus.Passed,
                Action = VerificationAction.Proceed
            };
        }

        if (string.IsNullOrEmpty(issueDescription))
        {
            return new VerificationResult
            {
                Status = VerificationStatus.Pending,
                Action = VerificationAction.WaitForAnalysis
            };
        }

        // Determine action based on retry count
        if (retryAttempts >= _configService.MaxRetries)
        {
            return new VerificationResult
            {
                Status = VerificationStatus.Failed,
                Action = VerificationAction.EscalateToUser,
                Message = $"Max retries ({_configService.MaxRetries}) reached. Escalating to user."
            };
        }

        return new VerificationResult
        {
            Status = VerificationStatus.Issues,
            Action = VerificationAction.Retry,
            Message = issueDescription
        };
    }

    /// <summary>
    /// Gets tools commonly used for verification.
    /// </summary>
    public List<string> GetVerificationTools()
    {
        return new List<string>
        {
            "capture_screenshot",
            "get_element_properties",
            "get_selected_elements",
            "get_view_info"
        };
    }
}

public class VerificationResult
{
    public VerificationStatus Status { get; set; }
    public VerificationAction Action { get; set; }
    public string? Message { get; set; }
}

public enum VerificationStatus
{
    Pending,
    Passed,
    Issues,
    Failed
}

public enum VerificationAction
{
    WaitForAnalysis,
    Proceed,
    Retry,
    EscalateToUser
}
```

### 2. Configuration Settings

```csharp
// In ApiSettings.cs, add:

/// <summary>
/// Automatically capture and verify after modifications.
/// </summary>
public bool AutoVerification { get; set; } = true;

/// <summary>
/// Maximum retry attempts before escalating to user.
/// </summary>
public int MaxRetries { get; set; } = 2;

/// <summary>
/// Verification strictness level.
/// </summary>
public VerificationStrictness VerificationStrictness { get; set; } = VerificationStrictness.Standard;

public enum VerificationStrictness
{
    /// <summary>
    /// Only verify on explicit verification steps.
    /// </summary>
    Minimal,

    /// <summary>
    /// Verify after each modification step.
    /// </summary>
    Standard,

    /// <summary>
    /// Verify after every tool call that modifies the model.
    /// </summary>
    Strict
}
```

### 3. ChatViewModel Integration

```csharp
// In ChatViewModel.cs

private readonly VerificationService _verificationService;

private async Task StreamClaudeResponseAsync(CancellationToken ct)
{
    while (true)
    {
        // Stream response
        var accumulator = await StreamResponseWithToolsAsync(ct);

        if (accumulator.StopReason != "tool_use" || !accumulator.ToolUseBlocks.Any())
            break;

        // Execute tools
        var toolBlocks = accumulator.ToolUseBlocks;
        var toolResults = await _toolDispatcher.DispatchAllAsync(toolBlocks, ct);

        // Add results to conversation
        _conversationMessages.Add(ClaudeMessage.ToolResult(toolResults));

        // Check if verification is needed
        var executedTools = toolBlocks
            .Select(t => _toolRegistry.Get(t.Name))
            .Where(t => t != null)
            .ToList();

        if (_verificationService.ShouldVerifyAfterTools(toolResults, executedTools!))
        {
            await TriggerVerificationAsync(toolBlocks, toolResults, ct);
        }

        // Continue conversation loop
    }
}

private async Task TriggerVerificationAsync(
    List<ToolUseBlock> tools,
    List<ToolResultBlock> results,
    CancellationToken ct)
{
    // Collect affected element IDs from results
    var elementIds = ExtractElementIdsFromResults(results);

    // Get verification prompt
    var verificationPrompt = _verificationService.GetVerificationPrompt(
        tools.Select(t => t.Name).ToList(),
        elementIds
    );

    // Add as internal system message (not user-visible)
    AddSystemMessage(verificationPrompt);

    // Log for debugging
    System.Diagnostics.Debug.WriteLine($"[Verification] Triggered after: {string.Join(", ", tools.Select(t => t.Name))}");
}

private List<long> ExtractElementIdsFromResults(List<ToolResultBlock> results)
{
    var ids = new List<long>();

    foreach (var result in results)
    {
        // Parse result content for element IDs
        // Common patterns: "Created element 12345", "Element ID: 12345"
        var content = result.Content?.ToString() ?? "";
        var matches = Regex.Matches(content, @"element[s]?\s*(?:id[s]?:?\s*)?(\d+)", RegexOptions.IgnoreCase);

        foreach (Match match in matches)
        {
            if (long.TryParse(match.Groups[1].Value, out var id))
            {
                ids.Add(id);
            }
        }
    }

    return ids.Distinct().ToList();
}

private void AddSystemMessage(string content)
{
    // Add as user message with system tag (Claude convention)
    var systemMessage = new ClaudeMessage
    {
        Role = "user",
        Content = new List<ContentBlock>
        {
            new TextContentBlock { Text = content }
        }
    };

    _conversationMessages.Add(systemMessage);
}
```

### 4. Verification Prompt for System

```csharp
// In ContextEngine.cs

private string GetVerificationGuidance()
{
    if (!_configService.AutoVerification)
        return string.Empty;

    return """

        ## Verification Guidelines

        After model modifications, verify results by:

        1. **Visual Check**: Capture a screenshot and analyze:
           - Are elements in correct positions?
           - Are there any overlaps or clashes?
           - Do annotations appear correctly?

        2. **Data Check**: Query modified elements:
           - Are parameters set correctly?
           - Are relationships maintained (levels, hosts)?

        3. **Issue Classification**:
           - **Minor**: Can be fixed without changing approach
           - **Major**: Requires plan adjustment
           - **Blocking**: Needs user input

        4. **Reporting**:
           - Always update plan status after verification
           - Record issues even if proceeding
           - Include element IDs for traceability

        """;
}
```

### 5. Verification in Plan Steps

Update plan step handling to track verification:

```csharp
// In AgenticModeService.cs

/// <summary>
/// Records verification result for a step.
/// </summary>
public void RecordVerification(
    int stepNumber,
    VerificationStatus status,
    string? observations = null,
    List<string>? issues = null)
{
    var step = _currentSession?.CurrentPlan?.GetStep(stepNumber);
    if (step == null) return;

    step.VerificationStatus = status;
    step.VerificationObservations = observations;
    step.VerificationIssues = issues ?? new List<string>();
    step.VerifiedAt = DateTime.Now;

    OnStepVerified?.Invoke(this, step);
}

// Add to PlanStep model:
public VerificationStatus? VerificationStatus { get; set; }
public string? VerificationObservations { get; set; }
public List<string> VerificationIssues { get; set; } = new();
public DateTime? VerifiedAt { get; set; }
```

---

## Verification Flow

```
Tool Execution Complete
         │
         ▼
┌─────────────────────────────┐
│ ShouldVerifyAfterTools()?   │
│ - Agentic mode enabled?     │
│ - Auto-verification on?     │
│ - Tools modified model?     │
│ - Not already verifying?    │
└─────────────────────────────┘
         │
         │ Yes
         ▼
┌─────────────────────────────┐
│ Generate Verification       │
│ Prompt & Add to Messages    │
└─────────────────────────────┘
         │
         │ Continue loop
         ▼
┌─────────────────────────────┐
│ Claude calls:               │
│ - capture_screenshot        │
│ - Analyzes result           │
│ - update_plan with status   │
└─────────────────────────────┘
         │
    ┌────┴────┐
    │         │
   Pass      Fail
    │         │
    ▼         ▼
Proceed    Retry or
to next    Escalate
step
```

---

## Quality Control Patterns

### Screenshot Analysis Prompt

When Claude receives verification instructions, it should analyze screenshots for:

```markdown
## Visual Verification Checklist

### Element Placement
- [ ] Elements appear at expected locations
- [ ] Spacing is consistent with specifications
- [ ] Alignment matches design intent

### Connections & Relationships
- [ ] Elements connect properly (walls, beams, columns)
- [ ] No visible gaps or overlaps
- [ ] Host relationships are correct

### Annotations & Documentation
- [ ] Tags are placed if required
- [ ] Dimensions show correct values
- [ ] Views capture intended scope

### Issues to Flag
- Misaligned elements (off by visible amount)
- Missing elements from the request
- Unexpected elements created
- Visual clashes or overlaps
```

### Verification Decision Tree

```
Analyze Screenshot
        │
        ├── Looks correct → Mark step complete, proceed
        │
        ├── Minor issue (< 1' misalignment) → Note and proceed
        │
        ├── Moderate issue (missing element) → Retry current step
        │
        ├── Major issue (wrong approach) → Revise plan
        │
        └── Critical issue (can't proceed) → Escalate to user
```

---

## Settings UI

```xml
<!-- In SettingsPane.xaml, under Agentic Mode section -->

<StackPanel Margin="0,10,0,0" IsEnabled="{Binding AgenticModeEnabled}">
    <CheckBox Content="Auto-verify after modifications"
              IsChecked="{Binding AutoVerification}"
              ToolTip="Automatically capture screenshots and verify results"/>

    <StackPanel Orientation="Horizontal" Margin="0,5,0,0"
                IsEnabled="{Binding AutoVerification}">
        <TextBlock Text="Max retries:" VerticalAlignment="Center"/>
        <ComboBox SelectedValue="{Binding MaxRetries}"
                  SelectedValuePath="Content" Margin="10,0">
            <ComboBoxItem Content="1"/>
            <ComboBoxItem Content="2"/>
            <ComboBoxItem Content="3"/>
        </ComboBox>
    </StackPanel>

    <StackPanel Orientation="Horizontal" Margin="0,5,0,0">
        <TextBlock Text="Verification level:" VerticalAlignment="Center"/>
        <ComboBox SelectedValue="{Binding VerificationStrictness}"
                  Margin="10,0">
            <ComboBoxItem Content="Minimal" Tag="Minimal"/>
            <ComboBoxItem Content="Standard" Tag="Standard"/>
            <ComboBoxItem Content="Strict" Tag="Strict"/>
        </ComboBox>
    </StackPanel>
</StackPanel>
```

---

## Verification (Manual)

1. **Enable agentic mode** with auto-verification
2. **Request a visible modification**: "Place a wall from (0,0) to (20,0) on Level 1"
3. **Verify**:
   - After tool execution, verification prompt is injected
   - Claude calls `capture_screenshot`
   - Claude analyzes and reports result
   - Plan status is updated
4. **Test failure case**:
   - Request something that will look wrong
   - Verify retry is triggered
   - Verify escalation after max retries

---

## Next Steps

After completing P4-04, proceed to **[P4-05: Agentic UI](P4-05-agentic-ui.md)** to implement the visual progress panel for plan execution.
