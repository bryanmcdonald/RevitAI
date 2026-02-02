# P4-01: Extended Thinking

**Status**: Pending

**Goal**: Integrate Claude's extended thinking feature to enable deeper reasoning before responding, forming the foundation for agentic planning.

**Prerequisites**: Phase 3 complete.

**Files Created**:
- `src/RevitAI/Models/ThinkingConfig.cs` - Thinking configuration model
- `src/RevitAI/Models/ThinkingContentBlock.cs` - Thinking block in responses

**Files Modified**:
- `src/RevitAI/Models/ClaudeModels.cs` - Add thinking to request/response models
- `src/RevitAI/Models/StreamEvents.cs` - Parse thinking events in streaming
- `src/RevitAI/Services/ClaudeApiService.cs` - Include thinking in requests, parse in responses
- `src/RevitAI/Services/ConfigurationService.cs` - Add agentic mode settings
- `src/RevitAI/Models/ApiSettings.cs` - Add thinking budget setting
- `src/RevitAI/UI/SettingsPane.xaml` - Add agentic mode toggle and thinking budget

---

## Implementation Details

### 1. Thinking Configuration Model

```csharp
// src/RevitAI/Models/ThinkingConfig.cs

/// <summary>
/// Configuration for Claude's extended thinking feature.
/// When enabled, Claude can reason for longer before responding.
/// </summary>
public class ThinkingConfig
{
    /// <summary>
    /// Type of thinking. Currently only "enabled" is supported.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "enabled";

    /// <summary>
    /// Maximum tokens Claude can use for thinking (not counted against max_tokens).
    /// Range: 1,024 to 100,000. Recommended: 10,000 for moderate tasks.
    /// </summary>
    [JsonPropertyName("budget_tokens")]
    public int BudgetTokens { get; set; } = 10000;
}
```

### 2. Thinking Content Block

```csharp
// src/RevitAI/Models/ThinkingContentBlock.cs

/// <summary>
/// Represents Claude's thinking process in a response.
/// Thinking blocks contain Claude's internal reasoning and are
/// not typically shown to users.
/// </summary>
public class ThinkingContentBlock : ContentBlock
{
    [JsonPropertyName("type")]
    public override string Type => "thinking";

    /// <summary>
    /// Claude's internal reasoning text.
    /// </summary>
    [JsonPropertyName("thinking")]
    public string Thinking { get; set; } = string.Empty;

    /// <summary>
    /// Signature for extended thinking verification.
    /// </summary>
    [JsonPropertyName("signature")]
    public string? Signature { get; set; }
}
```

### 3. Update ClaudeRequest

Add thinking configuration to the API request model:

```csharp
// In ClaudeModels.cs, update ClaudeRequest class

public class ClaudeRequest
{
    // ... existing properties ...

    /// <summary>
    /// Extended thinking configuration. When set, Claude will reason
    /// before responding, enabling more complex planning.
    /// </summary>
    [JsonPropertyName("thinking")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThinkingConfig? Thinking { get; set; }
}
```

### 4. Stream Event Parsing

Handle thinking events in the streaming response:

```csharp
// In StreamEvents.cs, add thinking event handling

public class ContentBlockDeltaEvent : StreamEvent
{
    // ... existing ...

    /// <summary>
    /// Thinking delta text when in a thinking block.
    /// </summary>
    [JsonPropertyName("thinking")]
    public string? Thinking { get; set; }
}

// Update ResponseAccumulator to handle thinking blocks
private void ProcessContentBlockStart(ContentBlockStartEvent evt)
{
    if (evt.ContentBlock?.Type == "thinking")
    {
        _currentThinkingBlock = new ThinkingContentBlock();
        _isInThinkingBlock = true;
    }
    // ... existing text/tool_use handling ...
}
```

### 5. API Service Updates

Modify `ClaudeApiService` to include thinking:

```csharp
public async Task<ClaudeResponse> SendMessageAsync(
    List<ClaudeMessage> messages,
    string systemPrompt,
    CancellationToken cancellationToken = default)
{
    var request = new ClaudeRequest
    {
        Model = _settings.ModelId,
        MaxTokens = _settings.MaxTokens,
        Temperature = _settings.Temperature,
        System = systemPrompt,
        Messages = messages,
        Tools = GetEnabledToolDefinitions()
    };

    // Add thinking configuration if agentic mode is enabled
    if (_configService.AgenticModeEnabled)
    {
        request.Thinking = new ThinkingConfig
        {
            Type = "enabled",
            BudgetTokens = _configService.ThinkingBudgetTokens
        };

        // Extended thinking may take longer - adjust timeout
        // Note: thinking tokens don't count against max_tokens
    }

    // ... rest of method ...
}
```

### 6. API Settings Updates

Add agentic mode settings:

```csharp
// In ApiSettings.cs

public class ApiSettings
{
    // ... existing properties ...

    /// <summary>
    /// Enable agentic mode with planning and auto-verification.
    /// </summary>
    public bool AgenticModeEnabled { get; set; } = false;

    /// <summary>
    /// Maximum tokens for extended thinking (1,024 - 100,000).
    /// </summary>
    public int ThinkingBudgetTokens { get; set; } = 10000;

    /// <summary>
    /// Require user approval of plan before execution.
    /// </summary>
    public bool RequirePlanApproval { get; set; } = false;
}
```

### 7. Configuration Service Updates

Add getters/setters for new settings:

```csharp
// In ConfigurationService.cs

public bool AgenticModeEnabled
{
    get => _settings.AgenticModeEnabled;
    set
    {
        _settings.AgenticModeEnabled = value;
        SaveSettings();
    }
}

public int ThinkingBudgetTokens
{
    get => _settings.ThinkingBudgetTokens;
    set
    {
        // Clamp to valid range
        _settings.ThinkingBudgetTokens = Math.Clamp(value, 1024, 100000);
        SaveSettings();
    }
}
```

### 8. Settings UI

Add agentic mode section to settings:

```xml
<!-- In SettingsPane.xaml -->
<GroupBox Header="Agentic Mode" Margin="0,10,0,0">
    <StackPanel>
        <CheckBox Content="Enable Agentic Mode"
                  IsChecked="{Binding AgenticModeEnabled}"
                  ToolTip="Allow Claude to plan and execute multi-step operations autonomously"/>

        <StackPanel Orientation="Horizontal" Margin="0,10,0,0"
                    IsEnabled="{Binding AgenticModeEnabled}">
            <TextBlock Text="Thinking Budget:" VerticalAlignment="Center"/>
            <Slider Minimum="1024" Maximum="50000"
                    Value="{Binding ThinkingBudgetTokens}"
                    Width="150" Margin="10,0"/>
            <TextBlock Text="{Binding ThinkingBudgetTokens, StringFormat={}{0:N0} tokens}"
                       VerticalAlignment="Center" Width="80"/>
        </StackPanel>

        <CheckBox Content="Require plan approval before execution"
                  IsChecked="{Binding RequirePlanApproval}"
                  IsEnabled="{Binding AgenticModeEnabled}"
                  Margin="0,10,0,0"/>

        <TextBlock Text="Agentic mode allows Claude to create execution plans and work through complex multi-step tasks autonomously."
                   TextWrapping="Wrap" Opacity="0.7" Margin="0,10,0,0"/>
    </StackPanel>
</GroupBox>
```

---

## Response Accumulator Changes

The `ResponseAccumulator` in `ChatViewModel` needs to handle thinking blocks:

```csharp
public class ResponseAccumulator
{
    // ... existing fields ...

    private readonly StringBuilder _thinkingContent = new();
    private bool _isInThinkingBlock;

    public string ThinkingContent => _thinkingContent.ToString();
    public bool HasThinking => _thinkingContent.Length > 0;

    public void ProcessEvent(StreamEvent evt)
    {
        switch (evt)
        {
            case ContentBlockStartEvent start:
                if (start.ContentBlock?.Type == "thinking")
                {
                    _isInThinkingBlock = true;
                }
                // ... existing handling ...
                break;

            case ContentBlockDeltaEvent delta:
                if (_isInThinkingBlock && delta.Delta?.Thinking != null)
                {
                    _thinkingContent.Append(delta.Delta.Thinking);
                }
                // ... existing handling ...
                break;

            case ContentBlockStopEvent stop:
                if (_isInThinkingBlock)
                {
                    _isInThinkingBlock = false;
                    // Optionally log thinking for debugging
                    System.Diagnostics.Debug.WriteLine(
                        $"[Thinking] {_thinkingContent.Length} chars");
                }
                // ... existing handling ...
                break;
        }
    }
}
```

---

## API Compatibility Notes

### Extended Thinking Requirements

1. **Model Support**: Extended thinking works with Claude claude-sonnet-4-5-20250929 and later models
2. **Streaming Required**: Extended thinking responses should use streaming
3. **Token Budget**: `budget_tokens` is separate from `max_tokens` - thinking doesn't reduce output capacity
4. **Response Time**: Expect 10-60 seconds for complex planning with extended thinking

### Token Budget Guidelines

| Task Complexity | Recommended Budget |
|-----------------|-------------------|
| Simple (1-3 steps) | 5,000 tokens |
| Moderate (4-8 steps) | 10,000 tokens |
| Complex (9+ steps) | 20,000-50,000 tokens |

### Error Handling

```csharp
// Extended thinking may return specific errors
if (error.Type == "invalid_request_error" &&
    error.Message.Contains("thinking"))
{
    // Fall back to non-thinking mode
    request.Thinking = null;
    return await SendMessageAsync(request, cancellationToken);
}
```

---

## Verification (Manual)

1. **Build and deploy** the updated plugin
2. **Enable agentic mode** in Settings
3. **Adjust thinking budget** using the slider
4. **Send a complex request** like "What would be the steps to create a structural grid?"
5. **Verify**:
   - Response takes longer (thinking is happening)
   - Claude provides more structured, well-thought-out responses
   - Thinking content is logged but not shown in chat
   - Settings persist between sessions

---

## Implementation Notes

### Thinking Visibility

By default, thinking content is NOT shown to users because:
- It can be very long (up to 50K tokens)
- It contains internal reasoning that may be confusing
- Users care about results, not the reasoning process

However, for debugging:
- Thinking is logged to Debug output
- Future: Add optional "Show Thinking" toggle for power users

### Timeout Considerations

Extended thinking can take 30-60 seconds for complex tasks. Update UI feedback:

```csharp
// Show "Claude is thinking..." status
if (_configService.AgenticModeEnabled)
{
    StatusMessage = "Claude is thinking through this...";
}
```

### Streaming Thinking

Thinking is streamed like regular content, allowing:
- Progress indication (thinking is happening)
- Cancellation mid-thinking
- Timeout detection

---

## Next Steps

After completing P4-01, proceed to **[P4-02: Planning Tools](P4-02-planning-tools.md)** to create the tools Claude uses to structure its execution plans.
