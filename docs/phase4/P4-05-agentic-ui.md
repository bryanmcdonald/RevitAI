# P4-05: Agentic UI

**Status**: Pending

**Goal**: Create a visual progress panel showing plan execution status, step-by-step progress, and real-time updates.

**Prerequisites**: P4-04 complete.

**Files Created**:
- `src/RevitAI/UI/PlanProgressPanel.xaml` - Plan visualization control
- `src/RevitAI/UI/PlanProgressPanel.xaml.cs` - Code-behind
- `src/RevitAI/UI/PlanProgressViewModel.cs` - ViewModel for plan display
- `src/RevitAI/UI/PlanStepViewModel.cs` - ViewModel for individual steps

**Files Modified**:
- `src/RevitAI/UI/ChatPane.xaml` - Embed plan progress panel
- `src/RevitAI/UI/ChatViewModel.cs` - Expose plan state to UI

---

## Implementation Details

### 1. PlanStepViewModel

```csharp
// src/RevitAI/UI/PlanStepViewModel.cs

/// <summary>
/// ViewModel for displaying a plan step in the UI.
/// </summary>
public partial class PlanStepViewModel : ObservableObject
{
    [ObservableProperty]
    private int _stepNumber;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private StepStatus _status = StepStatus.Pending;

    [ObservableProperty]
    private string _statusIcon = "○";

    [ObservableProperty]
    private string _statusColor = "#888888";

    [ObservableProperty]
    private bool _isVerificationStep;

    [ObservableProperty]
    private string? _result;

    [ObservableProperty]
    private string? _failureReason;

    [ObservableProperty]
    private TimeSpan? _duration;

    public PlanStepViewModel(PlanStep step)
    {
        UpdateFrom(step);
    }

    public void UpdateFrom(PlanStep step)
    {
        StepNumber = step.StepNumber;
        Description = step.Description;
        Status = step.Status;
        IsVerificationStep = step.IsVerification;
        Result = step.Result;
        FailureReason = step.FailureReason;

        // Calculate duration if available
        if (step.StartedAt.HasValue && step.CompletedAt.HasValue)
        {
            Duration = step.CompletedAt.Value - step.StartedAt.Value;
        }

        // Update visual status
        UpdateStatusVisuals();
    }

    private void UpdateStatusVisuals()
    {
        (StatusIcon, StatusColor) = Status switch
        {
            StepStatus.Pending => ("○", "#888888"),      // Gray circle
            StepStatus.InProgress => ("◐", "#FFA500"),   // Orange half-circle
            StepStatus.Completed => ("●", "#28A745"),    // Green filled
            StepStatus.Failed => ("✕", "#DC3545"),       // Red X
            StepStatus.Skipped => ("○", "#6C757D"),      // Gray (dimmed)
            _ => ("○", "#888888")
        };
    }
}
```

### 2. PlanProgressViewModel

```csharp
// src/RevitAI/UI/PlanProgressViewModel.cs

/// <summary>
/// ViewModel for the plan progress panel.
/// </summary>
public partial class PlanProgressViewModel : ObservableObject
{
    private readonly AgenticModeService _agenticService;

    [ObservableProperty]
    private bool _hasActivePlan;

    [ObservableProperty]
    private string _planGoal = string.Empty;

    [ObservableProperty]
    private int _progressPercent;

    [ObservableProperty]
    private string _progressText = "0 / 0 steps";

    [ObservableProperty]
    private string _statusText = "No active plan";

    [ObservableProperty]
    private ObservableCollection<PlanStepViewModel> _steps = new();

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private string _elapsedTime = "0:00";

    private DateTime? _planStartTime;
    private DispatcherTimer? _elapsedTimer;

    public PlanProgressViewModel(AgenticModeService agenticService)
    {
        _agenticService = agenticService;

        // Subscribe to events
        _agenticService.OnPlanCreated += HandlePlanCreated;
        _agenticService.OnStepUpdated += HandleStepUpdated;
        _agenticService.OnPlanCompleted += HandlePlanCompleted;
        _agenticService.OnPlanModified += HandlePlanModified;
    }

    private void HandlePlanCreated(object? sender, ExecutionPlan plan)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            HasActivePlan = true;
            PlanGoal = plan.Goal;
            ProgressPercent = 0;
            StatusText = "Plan created - starting execution...";

            Steps.Clear();
            foreach (var step in plan.Steps)
            {
                Steps.Add(new PlanStepViewModel(step));
            }

            UpdateProgressText(plan);
            StartElapsedTimer();
        });
    }

    private void HandleStepUpdated(object? sender, PlanStep step)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var vm = Steps.FirstOrDefault(s => s.StepNumber == step.StepNumber);
            vm?.UpdateFrom(step);

            var plan = _agenticService.GetOrCreateSession().CurrentPlan;
            if (plan != null)
            {
                UpdateProgress(plan);
                UpdateStatusText(step);
            }
        });
    }

    private void HandlePlanCompleted(object? sender, ExecutionPlan plan)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            ProgressPercent = 100;
            StopElapsedTimer();

            StatusText = plan.CompletionStatus switch
            {
                PlanCompletionStatus.Success => "✓ Plan completed successfully",
                PlanCompletionStatus.PartialSuccess => "◐ Plan partially completed",
                PlanCompletionStatus.Failed => "✕ Plan failed",
                PlanCompletionStatus.Cancelled => "○ Plan cancelled",
                _ => "Plan finished"
            };

            // Clear after delay
            Task.Delay(5000).ContinueWith(_ =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    HasActivePlan = false;
                    Steps.Clear();
                    PlanGoal = string.Empty;
                });
            });
        });
    }

    private void HandlePlanModified(object? sender, ExecutionPlan plan)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            // Rebuild steps list (in case steps were added/removed)
            Steps.Clear();
            foreach (var step in plan.Steps)
            {
                Steps.Add(new PlanStepViewModel(step));
            }

            UpdateProgressText(plan);
        });
    }

    private void UpdateProgress(ExecutionPlan plan)
    {
        var completed = plan.CompletedStepCount + plan.FailedStepCount + plan.SkippedStepCount;
        var total = plan.Steps.Count;

        ProgressPercent = total > 0 ? (int)((double)completed / total * 100) : 0;
        UpdateProgressText(plan);
    }

    private void UpdateProgressText(ExecutionPlan plan)
    {
        var completed = plan.CompletedStepCount;
        var total = plan.Steps.Count;
        var failed = plan.FailedStepCount;

        ProgressText = failed > 0
            ? $"{completed} / {total} steps ({failed} failed)"
            : $"{completed} / {total} steps";
    }

    private void UpdateStatusText(PlanStep step)
    {
        StatusText = step.Status switch
        {
            StepStatus.InProgress => $"Step {step.StepNumber}: {step.Description}",
            StepStatus.Completed => $"✓ Step {step.StepNumber} completed",
            StepStatus.Failed => $"✕ Step {step.StepNumber} failed: {step.FailureReason}",
            StepStatus.Skipped => $"○ Step {step.StepNumber} skipped",
            _ => StatusText
        };
    }

    private void StartElapsedTimer()
    {
        _planStartTime = DateTime.Now;
        _elapsedTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _elapsedTimer.Tick += (s, e) =>
        {
            if (_planStartTime.HasValue)
            {
                var elapsed = DateTime.Now - _planStartTime.Value;
                ElapsedTime = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:D2}";
            }
        };
        _elapsedTimer.Start();
    }

    private void StopElapsedTimer()
    {
        _elapsedTimer?.Stop();
        _elapsedTimer = null;
    }

    [RelayCommand]
    private void ToggleExpanded()
    {
        IsExpanded = !IsExpanded;
    }
}
```

### 3. PlanProgressPanel XAML

```xml
<!-- src/RevitAI/UI/PlanProgressPanel.xaml -->
<UserControl x:Class="RevitAI.UI.PlanProgressPanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d">

    <UserControl.Resources>
        <BooleanToVisibilityConverter x:Key="BoolToVis"/>

        <Style x:Key="StepItemStyle" TargetType="Border">
            <Setter Property="Padding" Value="8,4"/>
            <Setter Property="Margin" Value="0,2"/>
            <Setter Property="CornerRadius" Value="4"/>
            <Setter Property="Background" Value="#2A2A2A"/>
        </Style>
    </UserControl.Resources>

    <!-- Main Container - Only visible when plan is active -->
    <Border Background="#1E1E1E"
            BorderBrush="#3C3C3C"
            BorderThickness="0,1,0,0"
            Visibility="{Binding HasActivePlan, Converter={StaticResource BoolToVis}}">
        <Expander IsExpanded="{Binding IsExpanded}"
                  Background="Transparent"
                  BorderThickness="0">

            <!-- Header -->
            <Expander.Header>
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>

                    <!-- Note: Using text instead of emoji for WPF compatibility in Revit -->
                    <TextBlock Grid.Column="0"
                               Text="[Plan]"
                               FontWeight="SemiBold"
                               Foreground="#CCCCCC"
                               VerticalAlignment="Center"/>

                    <!-- Progress Bar -->
                    <ProgressBar Grid.Column="1"
                                 Value="{Binding ProgressPercent}"
                                 Maximum="100"
                                 Height="6"
                                 Margin="12,0"
                                 Background="#3C3C3C"
                                 Foreground="#28A745"/>

                    <!-- Elapsed Time -->
                    <TextBlock Grid.Column="2"
                               Text="{Binding ElapsedTime}"
                               Foreground="#888888"
                               FontSize="11"
                               VerticalAlignment="Center"/>
                </Grid>
            </Expander.Header>

            <!-- Content -->
            <StackPanel Margin="8">
                <!-- Goal -->
                <TextBlock Text="{Binding PlanGoal}"
                           Foreground="#AAAAAA"
                           FontStyle="Italic"
                           TextWrapping="Wrap"
                           Margin="0,0,0,8"/>

                <!-- Status -->
                <TextBlock Text="{Binding StatusText}"
                           Foreground="#888888"
                           FontSize="11"
                           Margin="0,0,0,8"/>

                <!-- Steps List -->
                <ItemsControl ItemsSource="{Binding Steps}"
                              MaxHeight="200">
                    <ItemsControl.ItemsPanel>
                        <ItemsPanelTemplate>
                            <StackPanel/>
                        </ItemsPanelTemplate>
                    </ItemsControl.ItemsPanel>

                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <Border Style="{StaticResource StepItemStyle}">
                                <Grid>
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="24"/>
                                        <ColumnDefinition Width="*"/>
                                        <ColumnDefinition Width="Auto"/>
                                    </Grid.ColumnDefinitions>

                                    <!-- Status Icon -->
                                    <TextBlock Grid.Column="0"
                                               Text="{Binding StatusIcon}"
                                               Foreground="{Binding StatusColor}"
                                               FontSize="14"
                                               VerticalAlignment="Center"
                                               HorizontalAlignment="Center"/>

                                    <!-- Description -->
                                    <StackPanel Grid.Column="1" VerticalAlignment="Center">
                                        <TextBlock>
                                            <Run Text="{Binding StepNumber, StringFormat='Step {0}: '}"
                                                 Foreground="#888888"/>
                                            <Run Text="{Binding Description}"
                                                 Foreground="#CCCCCC"/>
                                        </TextBlock>

                                        <!-- Verification badge (text for WPF compatibility) -->
                                        <TextBlock Text="[QC]"
                                                   FontSize="10"
                                                   Foreground="#6C757D"
                                                   Visibility="{Binding IsVerificationStep, Converter={StaticResource BoolToVis}}"/>

                                        <!-- Failure reason -->
                                        <TextBlock Text="{Binding FailureReason}"
                                                   FontSize="10"
                                                   Foreground="#DC3545"
                                                   TextWrapping="Wrap"
                                                   Visibility="{Binding FailureReason, Converter={StaticResource BoolToVis}}"/>
                                    </StackPanel>

                                    <!-- Duration -->
                                    <TextBlock Grid.Column="2"
                                               Text="{Binding Duration, StringFormat='{}{0:mm\\:ss}'}"
                                               Foreground="#6C757D"
                                               FontSize="10"
                                               VerticalAlignment="Center"
                                               Visibility="{Binding Duration, Converter={StaticResource BoolToVis}}"/>
                                </Grid>
                            </Border>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>

                <!-- Progress Summary -->
                <TextBlock Text="{Binding ProgressText}"
                           Foreground="#6C757D"
                           FontSize="11"
                           HorizontalAlignment="Right"
                           Margin="0,8,0,0"/>
            </StackPanel>
        </Expander>
    </Border>
</UserControl>
```

### 4. Code-Behind

```csharp
// src/RevitAI/UI/PlanProgressPanel.xaml.cs

public partial class PlanProgressPanel : UserControl
{
    public PlanProgressPanel()
    {
        InitializeComponent();
    }
}
```

### 5. Integrate into ChatPane

```xml
<!-- In ChatPane.xaml, add above the input area -->

<Grid>
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto"/>      <!-- Header -->
        <RowDefinition Height="*"/>          <!-- Messages -->
        <RowDefinition Height="Auto"/>      <!-- Plan Progress (NEW) -->
        <RowDefinition Height="Auto"/>      <!-- Input -->
    </Grid.RowDefinitions>

    <!-- ... existing header and messages ... -->

    <!-- Plan Progress Panel -->
    <local:PlanProgressPanel Grid.Row="2"
                             DataContext="{Binding PlanProgressViewModel}"/>

    <!-- ... existing input area ... -->
</Grid>
```

### 6. ChatViewModel Integration

```csharp
// In ChatViewModel.cs

public partial class ChatViewModel : ObservableObject
{
    [ObservableProperty]
    private PlanProgressViewModel _planProgressViewModel;

    public ChatViewModel(
        // ... existing parameters ...,
        AgenticModeService agenticService)
    {
        // Create plan progress viewmodel
        PlanProgressViewModel = new PlanProgressViewModel(agenticService);

        // ... existing initialization ...
    }
}
```

---

## Visual Design Specifications

### Color Scheme (Dark Theme)

| Element | Color | Purpose |
|---------|-------|---------|
| Background | `#1E1E1E` | Panel background |
| Border | `#3C3C3C` | Subtle separator |
| Step Background | `#2A2A2A` | Individual step cards |
| Primary Text | `#CCCCCC` | Main content |
| Secondary Text | `#888888` | Labels, timestamps |
| Success | `#28A745` | Completed steps, progress bar |
| Warning | `#FFA500` | In-progress |
| Error | `#DC3545` | Failed steps |
| Muted | `#6C757D` | Skipped, disabled |

### Status Icons

| Status | Icon | Description |
|--------|------|-------------|
| Pending | ○ | Empty circle |
| In Progress | ◐ | Half-filled circle (animated) |
| Completed | ● | Filled circle |
| Failed | ✕ | X mark |
| Skipped | ○ | Empty circle (dimmed) |

> **WPF Compatibility Note**: The status icons above use Unicode characters (○ ◐ ● ✕) which render reliably in WPF. Emojis should be avoided in Revit-hosted WPF as they may render as empty boxes or incorrect glyphs depending on the system font configuration. Use text alternatives like `[Plan]`, `[QC]`, or Unicode symbols instead.

### Animations

```xml
<!-- Pulsing animation for in-progress steps -->
<Style TargetType="TextBlock" x:Key="InProgressIcon">
    <Style.Triggers>
        <DataTrigger Binding="{Binding Status}" Value="InProgress">
            <DataTrigger.EnterActions>
                <BeginStoryboard>
                    <Storyboard RepeatBehavior="Forever">
                        <DoubleAnimation Storyboard.TargetProperty="Opacity"
                                         From="1.0" To="0.5"
                                         Duration="0:0:0.5"
                                         AutoReverse="True"/>
                    </Storyboard>
                </BeginStoryboard>
            </DataTrigger.EnterActions>
        </DataTrigger>
    </Style.Triggers>
</Style>
```

---

## Interaction Patterns

### Collapse/Expand

- Panel is expanded by default when plan starts
- User can collapse to minimize UI space
- Collapsed view still shows progress bar

### Auto-Scroll

- New steps scroll into view when added
- Current (in-progress) step stays visible

### Completion Behavior

- Panel stays visible for 5 seconds after completion
- Shows final status (success/partial/failed)
- Then fades out and resets

---

## Verification (Manual)

1. **Build and deploy** with agentic mode enabled
2. **Request a multi-step operation**: "Create 3 levels and a floor plan for each"
3. **Verify UI**:
   - Plan panel appears with goal
   - Steps list shows all steps
   - Progress bar advances
   - Status icons update in real-time
   - Elapsed time counts up
   - Panel dismisses after completion
4. **Test collapse/expand**
5. **Test failure display**

---

## Next Steps

After completing P4-05, proceed to **[P4-06: Error Recovery & Adaptation](P4-06-error-recovery.md)** to implement retry strategies and error handling.
