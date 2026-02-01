# P1-04: Claude API Integration

**Goal**: Connect to the Claude Messages API and enable basic chat functionality.

**Prerequisites**: P1-03 complete.

**Key Files to Create**:
- `src/RevitAI/Services/ClaudeApiService.cs`
- `src/RevitAI/Services/ConfigurationService.cs`
- `src/RevitAI/Models/ClaudeRequest.cs`
- `src/RevitAI/Models/ClaudeResponse.cs`
- `src/RevitAI/Models/Message.cs`

---

## Implementation Details

### 1. ClaudeRequest/Response Models

```csharp
public class ClaudeRequest
{
    public string Model { get; set; } = "claude-sonnet-4-5-20250929";
    public int MaxTokens { get; set; } = 4096;
    public string? System { get; set; }
    public List<Message> Messages { get; set; } = new();
    public List<ToolDefinition>? Tools { get; set; }
}

public class ClaudeResponse
{
    public string Id { get; set; }
    public List<ContentBlock> Content { get; set; }
    public string StopReason { get; set; }
    public Usage Usage { get; set; }
}
```

### 2. ClaudeApiService

With streaming and cancellation support.

```csharp
public class ClaudeApiService
{
    private readonly HttpClient _client;
    private readonly ConfigurationService _config;
    private CancellationTokenSource? _currentCts;

    // Non-streaming request
    public async Task<ClaudeResponse> SendMessageAsync(
        string systemPrompt,
        List<Message> messages,
        List<ToolDefinition>? tools = null,
        CancellationToken ct = default)
    {
        var request = new ClaudeRequest
        {
            System = systemPrompt,
            Messages = messages,
            Tools = tools,
            MaxTokens = _config.MaxTokens,
            Temperature = _config.Temperature
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        requestMessage.Content = content;
        requestMessage.Headers.Add("x-api-key", _config.ApiKey);
        requestMessage.Headers.Add("anthropic-version", "2023-06-01");

        var response = await _client.SendAsync(requestMessage, ct);
        response.EnsureSuccessStatusCode();
        var responseJson = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<ClaudeResponse>(responseJson)!;
    }

    // Streaming request for real-time responses
    public async IAsyncEnumerable<StreamEvent> SendMessageStreamingAsync(
        string systemPrompt,
        List<Message> messages,
        List<ToolDefinition>? tools = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _currentCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var request = new ClaudeRequest
        {
            System = systemPrompt,
            Messages = messages,
            Tools = tools,
            MaxTokens = _config.MaxTokens,
            Temperature = _config.Temperature,
            Stream = true
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        requestMessage.Content = content;
        requestMessage.Headers.Add("x-api-key", _config.ApiKey);
        requestMessage.Headers.Add("anthropic-version", "2023-06-01");

        using var response = await _client.SendAsync(
            requestMessage,
            HttpCompletionOption.ResponseHeadersRead,
            _currentCts.Token);

        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(_currentCts.Token);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !_currentCts.Token.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrEmpty(line)) continue;
            if (!line.StartsWith("data: ")) continue;

            var eventData = line[6..];
            if (eventData == "[DONE]") break;

            var streamEvent = JsonSerializer.Deserialize<StreamEvent>(eventData);
            if (streamEvent != null)
                yield return streamEvent;
        }
    }

    // Cancel current request
    public void CancelCurrentRequest()
    {
        _currentCts?.Cancel();
    }
}

public class StreamEvent
{
    public string Type { get; set; }
    public Delta? Delta { get; set; }
}

public class Delta
{
    public string? Text { get; set; }
    public string? ToolUse { get; set; }
}
```

### 3. ConfigurationService

Load/save all settings (API key encrypted).

```csharp
public class ConfigurationService
{
    private readonly string _configPath;

    // API Settings
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "claude-sonnet-4-5-20250929";
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 4096;

    // Context Settings
    public ContextVerbosity ContextVerbosity { get; set; } = ContextVerbosity.Standard;

    // Safety Settings
    public bool SkipConfirmations { get; set; } = false;
    public bool DryRunMode { get; set; } = false;

    public void Load()
    {
        if (!File.Exists(_configPath)) return;

        var json = File.ReadAllText(_configPath);
        var data = JsonSerializer.Deserialize<ConfigData>(json);
        if (data == null) return;

        ApiKey = SecureStorage.Decrypt(data.EncryptedApiKey);
        Model = data.Model ?? Model;
        Temperature = data.Temperature ?? Temperature;
        MaxTokens = data.MaxTokens ?? MaxTokens;
        ContextVerbosity = data.ContextVerbosity ?? ContextVerbosity;
        SkipConfirmations = data.SkipConfirmations ?? SkipConfirmations;
        DryRunMode = data.DryRunMode ?? DryRunMode;
    }

    public void Save()
    {
        var data = new ConfigData
        {
            EncryptedApiKey = SecureStorage.Encrypt(ApiKey ?? ""),
            Model = Model,
            Temperature = Temperature,
            MaxTokens = MaxTokens,
            ContextVerbosity = ContextVerbosity,
            SkipConfirmations = SkipConfirmations,
            DryRunMode = DryRunMode
        };

        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_configPath, json);
    }
}

public enum ContextVerbosity { Minimal, Standard, Detailed }
```

### 4. Connect ChatViewModel to ClaudeApiService

- SendCommand calls ClaudeApiService.SendMessageAsync
- Display response in Messages collection
- Handle errors gracefully

---

## Verification (Manual)

1. Build and deploy
2. Open Revit, show chat pane
3. Enter API key in settings (or hardcode temporarily for testing)
4. Type "Hello, what can you help me with?"
5. Verify Claude response appears in chat
