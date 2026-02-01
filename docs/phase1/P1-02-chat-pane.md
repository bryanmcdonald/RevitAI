# P1-02: Dockable Chat Pane

**Goal**: Create a WPF dockable pane with basic chat UI that registers and displays in Revit.

**Prerequisites**: P1-01 complete.

**Key Files to Create**:
- `src/RevitAI/UI/ChatPane.xaml`
- `src/RevitAI/UI/ChatPane.xaml.cs`
- `src/RevitAI/UI/ChatViewModel.cs`
- `src/RevitAI/UI/ChatMessage.cs`
- `src/RevitAI/Commands/ShowChatPaneCommand.cs`

---

## Implementation Details

### 1. ChatPane XAML

Scrollable message list, text input, send button, status indicator.

```xaml
<!-- Basic structure -->
<UserControl>
  <Grid>
    <Grid.RowDefinitions>
      <RowDefinition Height="*"/>      <!-- Messages -->
      <RowDefinition Height="Auto"/>   <!-- Status -->
      <RowDefinition Height="Auto"/>   <!-- Input -->
    </Grid.RowDefinitions>

    <ListBox ItemsSource="{Binding Messages}" />

    <!-- Status indicator -->
    <StackPanel Grid.Row="1" Orientation="Horizontal"
                Visibility="{Binding IsProcessing, Converter={StaticResource BoolToVisibility}}">
      <ProgressBar IsIndeterminate="True" Width="100" Height="4"/>
      <TextBlock Text="{Binding StatusText}" Margin="8,0,0,0"/>
    </StackPanel>

    <Grid Grid.Row="2">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="Auto"/>
      </Grid.ColumnDefinitions>

      <!-- AcceptsReturn allows Shift+Enter for newlines -->
      <TextBox Text="{Binding InputText, UpdateSourceTrigger=PropertyChanged}"
               AcceptsReturn="True"
               TextWrapping="Wrap"
               MaxHeight="100"
               VerticalScrollBarVisibility="Auto"
               IsEnabled="{Binding IsNotProcessing}">
        <TextBox.InputBindings>
          <!-- Enter to send (without Shift) -->
          <KeyBinding Key="Return" Command="{Binding SendCommand}"
                      Modifiers="" />
        </TextBox.InputBindings>
      </TextBox>

      <StackPanel Grid.Column="1" Orientation="Horizontal">
        <!-- Send button (visible when not processing) -->
        <Button Content="Send" Command="{Binding SendCommand}"
                Visibility="{Binding IsNotProcessing, Converter={StaticResource BoolToVisibility}}"
                Margin="5,0,0,0"/>
        <!-- Cancel button (visible when processing) -->
        <Button Content="Cancel" Command="{Binding CancelCommand}"
                Visibility="{Binding IsProcessing, Converter={StaticResource BoolToVisibility}}"
                Background="#FFE57373" Margin="5,0,0,0"/>
      </StackPanel>
    </Grid>
  </Grid>
</UserControl>
```

### 2. IDockablePaneProvider Implementation

```csharp
public class ChatPane : UserControl, IDockablePaneProvider
{
    public void SetupDockablePane(DockablePaneProviderData data)
    {
        data.FrameworkElement = this;
        data.InitialState = new DockablePaneState
        {
            DockPosition = DockPosition.Right
        };
    }
}
```

### 3. Register Dockable Pane in App.OnStartup

```csharp
public static readonly DockablePaneId ChatPaneId =
    new DockablePaneId(new Guid("YOUR-PANE-GUID"));

public Result OnStartup(UIControlledApplication app)
{
    app.RegisterDockablePane(ChatPaneId, "RevitAI Chat", new ChatPane());
    // Add ribbon button to show/hide pane
    return Result.Succeeded;
}
```

### 4. ChatMessage Model

Role (User/Assistant/System), Content, Timestamp.

### 5. ChatViewModel

ObservableCollection<ChatMessage>, InputText, SendCommand, Status, Cancel.

```csharp
public class ChatViewModel : INotifyPropertyChanged
{
    private readonly ClaudeApiService _claudeService;
    private CancellationTokenSource? _currentCts;

    public ObservableCollection<ChatMessage> Messages { get; } = new();
    public string InputText { get; set; }
    public bool IsProcessing { get; set; }
    public bool IsNotProcessing => !IsProcessing;
    public string StatusText { get; set; } = "Ready";

    public ICommand SendCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand CancelCommand { get; }

    public ChatViewModel(ClaudeApiService claudeService)
    {
        _claudeService = claudeService;
        SendCommand = new AsyncRelayCommand(SendMessageAsync, () => !IsProcessing);
        ClearCommand = new RelayCommand(ClearConversation);
        CancelCommand = new RelayCommand(CancelCurrentOperation, () => IsProcessing);
    }

    private void CancelCurrentOperation()
    {
        _currentCts?.Cancel();
        _claudeService.CancelCurrentRequest();
        StatusText = "Cancelled";
        Messages.Add(new ChatMessage { Role = "system", Content = "Operation cancelled by user." });
        IsProcessing = false;
    }

    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(InputText)) return;

        _currentCts = new CancellationTokenSource();
        IsProcessing = true;
        StatusText = "Thinking...";

        try
        {
            // Add user message
            Messages.Add(new ChatMessage { Role = "user", Content = InputText });
            var userMessage = InputText;
            InputText = string.Empty;

            // Call Claude API with cancellation support
            StatusText = "Waiting for response...";
            var response = await _claudeService.SendMessageAsync(
                BuildSystemPrompt(),
                GetMessageHistory(),
                _toolRegistry.GetDefinitions().ToList(),
                _currentCts.Token);

            // Check for cancellation
            _currentCts.Token.ThrowIfCancellationRequested();

            // Handle tool calls
            if (response.StopReason == "tool_use")
            {
                StatusText = "Executing...";
                await ProcessToolCallsAsync(response, _currentCts.Token);
            }

            // Add assistant message
            var textContent = response.Content
                .Where(c => c.Type == "text")
                .Select(c => c.Text)
                .FirstOrDefault();
            if (!string.IsNullOrEmpty(textContent))
            {
                Messages.Add(new ChatMessage { Role = "assistant", Content = textContent });
            }
        }
        catch (OperationCanceledException)
        {
            // Already handled in CancelCurrentOperation
        }
        catch (Exception ex)
        {
            Messages.Add(new ChatMessage { Role = "system", Content = $"Error: {ex.Message}" });
        }
        finally
        {
            IsProcessing = false;
            StatusText = "Ready";
            _currentCts = null;
        }
    }

    private void ClearConversation()
    {
        Messages.Clear();
        Messages.Add(new ChatMessage
        {
            Role = "system",
            Content = "Conversation cleared. How can I help you?"
        });
    }
}
```

---

## Verification (Manual)

1. Build and deploy to Revit
2. Launch Revit, open a project
3. Find RevitAI ribbon tab, click "Show Chat" button
4. Verify dockable pane appears on the right
5. Type a message, click Send, verify it appears in the message list
