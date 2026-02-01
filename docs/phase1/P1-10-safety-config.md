# P1-10: Safety & Configuration

**Goal**: Add confirmation dialogs for destructive operations, settings UI, and safety features.

**Prerequisites**: P1-09 complete.

**Key Files to Create/Modify**:
- `src/RevitAI/UI/SettingsPane.xaml`
- `src/RevitAI/UI/SettingsViewModel.cs`
- `src/RevitAI/UI/ConfirmationDialog.xaml`
- `src/RevitAI/Services/SafetyService.cs`
- Modify `ToolDispatcher.cs` for confirmation flow

---

## Implementation Details

### 1. SafetyService

Categorize tools and enforce confirmation.

```csharp
public class SafetyService
{
    private readonly HashSet<string> _destructiveTools = new()
    {
        "delete_elements",
        "bulk_modify_parameters"
    };

    public bool RequiresConfirmation(string toolName) =>
        _destructiveTools.Contains(toolName) && !_config.SkipConfirmations;

    public async Task<bool> RequestConfirmationAsync(string toolName, string description)
    {
        // Show WPF confirmation dialog
        var dialog = new ConfirmationDialog(toolName, description);
        return dialog.ShowDialog() == true;
    }
}
```

### 2. ConfirmationDialog

```xaml
<Window Title="Confirm Action">
  <StackPanel>
    <TextBlock Text="RevitAI wants to perform the following action:"/>
    <TextBlock Text="{Binding Description}" FontWeight="Bold"/>
    <StackPanel Orientation="Horizontal">
      <Button Content="Allow" Command="{Binding AllowCommand}"/>
      <Button Content="Deny" Command="{Binding DenyCommand}"/>
    </StackPanel>
  </StackPanel>
</Window>
```

### 3. Integrate Confirmation into ToolDispatcher

```csharp
public async Task<ToolResult> DispatchAsync(...)
{
    if (_safetyService.RequiresConfirmation(toolName))
    {
        var description = FormatToolCallDescription(toolName, input);
        var confirmed = await _safetyService.RequestConfirmationAsync(toolName, description);
        if (!confirmed)
            return ToolResult.Error("User denied the operation");
    }
    // ... proceed with execution
}
```

### 4. Settings Panel

Complete configuration UI.

```xaml
<UserControl>
  <ScrollViewer VerticalScrollBarVisibility="Auto">
    <StackPanel Margin="10">
      <GroupBox Header="API Configuration">
        <StackPanel>
          <Label Content="API Key:"/>
          <PasswordBox Password="{Binding ApiKey}"/>

          <Label Content="Model:" Margin="0,10,0,0"/>
          <ComboBox SelectedItem="{Binding Model}">
            <ComboBoxItem>claude-sonnet-4-5-20250929</ComboBoxItem>
            <ComboBoxItem>claude-opus-4-5-20250929</ComboBoxItem>
          </ComboBox>

          <Label Content="Temperature:" Margin="0,10,0,0"/>
          <StackPanel Orientation="Horizontal">
            <Slider Value="{Binding Temperature}" Minimum="0" Maximum="1"
                    Width="150" TickFrequency="0.1" IsSnapToTickEnabled="True"/>
            <TextBlock Text="{Binding Temperature, StringFormat=F1}" Margin="10,0,0,0"/>
          </StackPanel>
          <TextBlock Text="Lower = more focused, Higher = more creative"
                     FontSize="10" Foreground="Gray"/>

          <Label Content="Max Tokens per Request:" Margin="0,10,0,0"/>
          <ComboBox SelectedItem="{Binding MaxTokens}">
            <ComboBoxItem>1024</ComboBoxItem>
            <ComboBoxItem>2048</ComboBoxItem>
            <ComboBoxItem>4096</ComboBoxItem>
            <ComboBoxItem>8192</ComboBoxItem>
          </ComboBox>
        </StackPanel>
      </GroupBox>

      <GroupBox Header="Context Settings" Margin="0,10,0,0">
        <StackPanel>
          <Label Content="Context Verbosity:"/>
          <ComboBox SelectedItem="{Binding ContextVerbosity}">
            <ComboBoxItem>Minimal</ComboBoxItem>
            <ComboBoxItem>Standard</ComboBoxItem>
            <ComboBoxItem>Detailed</ComboBoxItem>
          </ComboBox>
          <TextBlock Text="Minimal: View + selection count only
Standard: View + selection details + level
Detailed: Full element properties + available types"
                     FontSize="10" Foreground="Gray" TextWrapping="Wrap"/>
        </StackPanel>
      </GroupBox>

      <GroupBox Header="Safety" Margin="0,10,0,0">
        <StackPanel>
          <CheckBox Content="Skip confirmation dialogs for destructive operations"
                    IsChecked="{Binding SkipConfirmations}"/>
          <CheckBox Content="Dry-run mode (describe actions but don't execute)"
                    IsChecked="{Binding DryRunMode}" Margin="0,5,0,0"/>
        </StackPanel>
      </GroupBox>

      <GroupBox Header="Token Usage (This Session)" Margin="0,10,0,0">
        <Grid>
          <Grid.ColumnDefinitions>
            <ColumnDefinition Width="Auto"/>
            <ColumnDefinition Width="*"/>
          </Grid.ColumnDefinitions>
          <Grid.RowDefinitions>
            <RowDefinition/>
            <RowDefinition/>
            <RowDefinition/>
          </Grid.RowDefinitions>

          <TextBlock Text="Input Tokens:" Grid.Row="0"/>
          <TextBlock Text="{Binding InputTokens, StringFormat=N0}" Grid.Row="0" Grid.Column="1"
                     HorizontalAlignment="Right"/>

          <TextBlock Text="Output Tokens:" Grid.Row="1"/>
          <TextBlock Text="{Binding OutputTokens, StringFormat=N0}" Grid.Row="1" Grid.Column="1"
                     HorizontalAlignment="Right"/>

          <TextBlock Text="Estimated Cost:" Grid.Row="2" FontWeight="Bold"/>
          <TextBlock Text="{Binding EstimatedCost, StringFormat=C2}" Grid.Row="2" Grid.Column="1"
                     HorizontalAlignment="Right" FontWeight="Bold"/>
        </Grid>
      </GroupBox>

      <StackPanel Orientation="Horizontal" Margin="0,15,0,0" HorizontalAlignment="Right">
        <Button Content="Reset Usage" Command="{Binding ResetUsageCommand}" Margin="0,0,10,0"/>
        <Button Content="Save" Command="{Binding SaveCommand}"/>
      </StackPanel>
    </StackPanel>
  </ScrollViewer>
</UserControl>
```

### 5. Dry-Run Mode

```csharp
if (_config.DryRunMode && tool.RequiresTransaction)
{
    return ToolResult.Ok($"[DRY RUN] Would execute: {toolName} with {input}");
}
```

### 6. Token Usage Tracking

```csharp
public class UsageTracker
{
    public int TotalInputTokens { get; private set; }
    public int TotalOutputTokens { get; private set; }

    public void RecordUsage(Usage usage)
    {
        TotalInputTokens += usage.InputTokens;
        TotalOutputTokens += usage.OutputTokens;
    }
}
```

---

## Verification (Manual)

1. Build and deploy
2. Open settings, configure API key
3. Ask Claude to delete an element
4. Verify confirmation dialog appears
5. Click "Deny", verify operation is cancelled
6. Click "Allow", verify operation proceeds
7. Enable dry-run mode
8. Ask Claude to place a wall
9. Verify Claude describes the action but doesn't execute it
10. Verify token usage is displayed in settings
