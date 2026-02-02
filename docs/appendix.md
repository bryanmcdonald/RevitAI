# Appendix: API Patterns & Reference

> Reference documentation for Revit API patterns, threading, unit conversion, and troubleshooting.

---

## A.1: Revit API Threading Rules

**Critical**: All Revit API calls must execute on the main thread with a valid API context.

```
Background Thread                 Main Thread
      |                                |
      |  1. API call response          |
      |  2. Parse tool_use             |
      |  3. Queue command  --------->  |
      |  4. ExternalEvent.Raise()      |
      |                                |  5. Handler.Execute()
      |                                |  6. Open Transaction
      |                                |  7. Revit API calls
      |                                |  8. Commit Transaction
      |  <---------------------------  |  9. Return result
      |  10. Send tool_result          |
```

### Key Rules
1. Never call Revit API from background threads
2. Use `ExternalEvent` + `IExternalEventHandler` for marshalling
3. Always wrap modifications in transactions
4. Use `TaskCompletionSource` for async/await patterns

---

## A.2: Unit Conversion Reference

Revit internal units are **decimal feet**.

| User Input | Revit Value (feet) |
|------------|-------------------|
| 10' | 10.0 |
| 10'-6" | 10.5 |
| 10' 6" | 10.5 |
| 120" | 10.0 |
| 3048 mm | 10.0 |
| 3.048 m | 10.0 |

### Conversion Helpers
```csharp
public static class UnitConversion
{
    public static double FeetToMm(double feet) => feet * 304.8;
    public static double MmToFeet(double mm) => mm / 304.8;
    public static double FeetToInches(double feet) => feet * 12.0;
    public static double InchesToFeet(double inches) => inches / 12.0;
    public static double FeetToMeters(double feet) => feet * 0.3048;
    public static double MetersToFeet(double meters) => meters / 0.3048;

    public static double ParseUserInput(string input)
    {
        // Handle various formats: 10', 10'-6", 120", 3048mm, etc.
        input = input.Trim().ToLower();

        // Feet and inches: 10'-6"
        var feetInchesMatch = Regex.Match(input, @"(\d+(?:\.\d+)?)'[\s-]*(\d+(?:\.\d+)?)""?");
        if (feetInchesMatch.Success)
        {
            var feet = double.Parse(feetInchesMatch.Groups[1].Value);
            var inches = double.Parse(feetInchesMatch.Groups[2].Value);
            return feet + inches / 12.0;
        }

        // Feet only: 10'
        var feetMatch = Regex.Match(input, @"(\d+(?:\.\d+)?)'");
        if (feetMatch.Success)
            return double.Parse(feetMatch.Groups[1].Value);

        // Inches: 120"
        var inchesMatch = Regex.Match(input, @"(\d+(?:\.\d+)?)""");
        if (inchesMatch.Success)
            return double.Parse(inchesMatch.Groups[1].Value) / 12.0;

        // Millimeters: 3048mm
        var mmMatch = Regex.Match(input, @"(\d+(?:\.\d+)?)\s*mm");
        if (mmMatch.Success)
            return MmToFeet(double.Parse(mmMatch.Groups[1].Value));

        // Meters: 3.048m
        var mMatch = Regex.Match(input, @"(\d+(?:\.\d+)?)\s*m(?!m)");
        if (mMatch.Success)
            return MetersToFeet(double.Parse(mMatch.Groups[1].Value));

        // Default: assume feet
        return double.Parse(input);
    }
}
```

---

## A.3: Common Revit API Patterns

### FilteredElementCollector

```csharp
// Get all walls
var walls = new FilteredElementCollector(doc)
    .OfClass(typeof(Wall))
    .WhereElementIsNotElementType()
    .Cast<Wall>();

// Get walls on a specific level
var level = GetLevelByName(doc, "Level 1");
var wallsOnLevel = new FilteredElementCollector(doc)
    .OfClass(typeof(Wall))
    .WherePasses(new ElementLevelFilter(level.Id))
    .Cast<Wall>();

// Get elements by category
var columns = new FilteredElementCollector(doc)
    .OfCategory(BuiltInCategory.OST_StructuralColumns)
    .WhereElementIsNotElementType()
    .Cast<FamilyInstance>();

// Get family types (symbols)
var wallTypes = new FilteredElementCollector(doc)
    .OfClass(typeof(WallType))
    .Cast<WallType>();

// Combine filters
var filter = new LogicalAndFilter(
    new ElementCategoryFilter(BuiltInCategory.OST_Walls),
    new ElementLevelFilter(levelId));
var filteredWalls = new FilteredElementCollector(doc)
    .WherePasses(filter)
    .Cast<Wall>();
```

### Transaction Pattern

```csharp
using (var trans = new Transaction(doc, "Operation Name"))
{
    trans.Start();
    try
    {
        // Revit API modifications
        trans.Commit();
    }
    catch
    {
        trans.RollBack();
        throw;
    }
}
```

### Transaction Group (for multi-step operations)

```csharp
using (var group = new TransactionGroup(doc, "Multi-Step Operation"))
{
    group.Start();
    try
    {
        // Multiple transactions
        using (var t1 = new Transaction(doc, "Step 1"))
        {
            t1.Start();
            // ...
            t1.Commit();
        }

        using (var t2 = new Transaction(doc, "Step 2"))
        {
            t2.Start();
            // ...
            t2.Commit();
        }

        group.Assimilate(); // Combines into single undo
    }
    catch
    {
        group.RollBack();
        throw;
    }
}
```

### Element Creation

```csharp
// Wall
Wall.Create(doc, curve, wallTypeId, levelId, height, offset, flip, structural);

// Column
doc.Create.NewFamilyInstance(
    location,           // XYZ point
    symbol,            // FamilySymbol
    level,             // Level
    StructuralType.Column);

// Beam
doc.Create.NewFamilyInstance(
    curve,             // Line
    symbol,            // FamilySymbol
    level,             // Level
    StructuralType.Beam);

// Floor
doc.Create.NewFloor(curveArray, floorType, level, structural);

// Grid
Grid.Create(doc, line);

// Level
Level.Create(doc, elevation);
```

### Parameter Access

```csharp
// By name
var param = element.LookupParameter("Mark");
if (param != null && !param.IsReadOnly)
{
    switch (param.StorageType)
    {
        case StorageType.String:
            param.Set("New Value");
            break;
        case StorageType.Double:
            param.Set(10.5); // In feet
            break;
        case StorageType.Integer:
            param.Set(42);
            break;
        case StorageType.ElementId:
            param.Set(otherElementId);
            break;
    }
}

// By BuiltInParameter
var levelParam = element.get_Parameter(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM);
```

---

## A.4: Error Handling Strategy

### 1. Validate inputs before execution

```csharp
public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken ct)
{
    // Validate element exists
    if (!input.TryGetProperty("element_id", out var idElement))
        return Task.FromResult(ToolResult.Error("Missing required parameter: element_id"));

    var elementId = new ElementId(idElement.GetInt64());
    var element = doc.GetElement(elementId);
    if (element == null)
        return Task.FromResult(ToolResult.Error($"Element {elementId.Value} not found"));

    // Validate parameter exists
    var paramName = input.GetProperty("parameter_name").GetString();
    var param = element.LookupParameter(paramName);
    if (param == null)
    {
        var availableParams = element.Parameters
            .Cast<Parameter>()
            .Select(p => p.Definition.Name)
            .Take(10);
        return Task.FromResult(ToolResult.Error(
            $"Parameter '{paramName}' not found. Available: {string.Join(", ", availableParams)}"));
    }

    // Continue with execution...
}
```

### 2. Wrap all transactions in try-catch

```csharp
using var scope = _transactionManager.StartTransaction(doc, tool.Name);
try
{
    var result = await tool.ExecuteAsync(input, app, ct);
    if (result.Success)
        scope.Commit();
    return result;
}
catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
{
    return ToolResult.Error($"Revit operation failed: {ex.Message}");
}
catch (Exception ex)
{
    return ToolResult.Error($"Unexpected error: {ex.Message}");
}
```

### 3. Report errors clearly to Claude

```csharp
// Include suggestions in error messages
return ToolResult.Error($"Wall type '{typeName}' not found. Available types: {availableTypes}");

// Include context for debugging
return ToolResult.Error($"Cannot place column at ({x}, {y}): Location is outside the model bounds");
```

### 4. Let Claude self-correct

Claude can retry with corrected parameters based on error messages that include:
- What went wrong
- What was expected
- Available alternatives

---

## A.5: Security Considerations

### 1. API Key Storage

Use Windows DPAPI for encryption:

```csharp
public class SecureStorage
{
    public static string Encrypt(string plainText)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var encryptedBytes = ProtectedData.Protect(
            plainBytes,
            null,
            DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encryptedBytes);
    }

    public static string Decrypt(string encryptedText)
    {
        var encryptedBytes = Convert.FromBase64String(encryptedText);
        var plainBytes = ProtectedData.Unprotect(
            encryptedBytes,
            null,
            DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plainBytes);
    }
}
```

### 2. Never log API keys

```csharp
// BAD
_logger.LogDebug($"Using API key: {_config.ApiKey}");

// GOOD
_logger.LogDebug("API key configured: {IsConfigured}", !string.IsNullOrEmpty(_config.ApiKey));
```

### 3. Validate all tool inputs

```csharp
// Prevent path traversal
if (filePath.Contains("..") || Path.IsPathRooted(filePath))
    return ToolResult.Error("Invalid file path");

// Sanitize element IDs
if (elementId < 0)
    return ToolResult.Error("Invalid element ID");
```

### 4. Confirm destructive operations

```csharp
private readonly HashSet<string> _destructiveTools = new()
{
    "delete_elements",
    "bulk_modify_parameters",
    "purge_unused"
};

public bool RequiresConfirmation(string toolName) =>
    _destructiveTools.Contains(toolName);
```

---

## A.6: Troubleshooting

| Issue | Cause | Solution |
|-------|-------|----------|
| Plugin doesn't load | Wrong .NET version | Ensure targeting `net8.0-windows` |
| Plugin doesn't load | Missing assembly | Check all dependencies are copied to output |
| "API context" error | Wrong thread | Use `ExternalEvent` marshalling |
| Transaction failed | No transaction open | Always wrap modifications in `Transaction` |
| Transaction failed | Nested transaction | Use `TransactionGroup` for nesting |
| Type not found | Name mismatch | Use `TypeResolver` with fuzzy matching |
| Element creation fails | Invalid geometry | Validate points/curves before creation |
| Element creation fails | Type not active | Call `symbol.Activate()` before placement |
| Parameter read-only | Built-in parameter | Check `param.IsReadOnly` before setting |
| Selection empty | Document not open | Check `uidoc.ActiveView != null` |
| View override fails | View template | Check if view is controlled by template |
| WPF text invisible | System dark mode + Border | Avoid nesting text inside Border; see note below |
| Default interface method not called | DIM dispatch issue | Use reflection to call concrete type's method |

### Common Exceptions

| Exception | Cause | Solution |
|-----------|-------|----------|
| `InvalidOperationException` | Transaction not started | Ensure `trans.Start()` called |
| `ArgumentException` | Invalid curve geometry | Check curve length > 0 |
| `ArgumentNullException` | Element deleted | Verify element exists before use |
| `ModificationForbiddenException` | Read-only document | Check `doc.IsModifiable` |
| `ModificationOutsideTransactionException` | Missing transaction | Wrap in `Transaction` |

### WPF Rendering in Revit with System Dark Mode

When running WPF dialogs inside Revit 2026 with Windows system dark mode enabled, text inside `Border` elements may become invisible. This appears to be a theming conflict where the system overrides text colors.

**Symptoms:**
- Text is set correctly (verified via logging)
- TextBlock has proper dimensions (ActualWidth/ActualHeight > 0)
- Foreground color reports correctly (#FF333333)
- But text is not visible on screen

**Solution:** Avoid wrapping text controls inside `Border` elements. Place TextBlocks directly in the Grid:

```xaml
<!-- PROBLEMATIC: Text may be invisible in Revit with dark mode -->
<Border Background="White" BorderBrush="#E0E0E0" BorderThickness="1">
    <TextBlock Text="Description" Foreground="#333333"/>
</Border>

<!-- WORKING: Text renders correctly -->
<TextBlock Text="Description" Foreground="#1976D2"/>
```

### Default Interface Method (DIM) Dispatch Issues

C# Default Interface Methods have a known quirk: calling through an interface reference may invoke the default implementation instead of the concrete class's override. This affects `IRevitTool.GetDryRunDescription()`.

**Solution:** Use reflection to explicitly call the concrete type's method:

```csharp
var concreteType = tool.GetType();
var method = concreteType.GetMethod("GetDryRunDescription",
    BindingFlags.Public | BindingFlags.Instance,
    null, new[] { typeof(JsonElement) }, null);

if (method != null && method.DeclaringType != typeof(IRevitTool))
{
    var result = method.Invoke(tool, new object[] { input });
    // Use result...
}
```

### Debug Tips

1. **Enable Revit Journal**: Check `%LOCALAPPDATA%\Autodesk\Revit\Autodesk Revit 2026\Journals`

2. **Add diagnostic logging**:
   ```csharp
   Debug.WriteLine($"Tool {toolName} executing with input: {input}");
   ```

3. **Check element validity**:
   ```csharp
   if (element == null || !element.IsValidObject)
       return ToolResult.Error("Element is invalid or deleted");
   ```

4. **Test with minimal input** to isolate issues

5. **Use try-catch with specific exception types** for better error messages

---

## A.7: System Prompt Strategy

The system prompt sent to Claude with every API call is critical for consistent, safe behavior. Include these six key sections:

### 1. Role Definition

```text
You are RevitAI, an AI assistant embedded in Autodesk Revit. You help engineers design, query, and modify BIM models through natural language conversation. You have access to tools that read and modify the Revit model.

You are working with a multi-discipline engineering team (Structural, Civil, Mechanical, Electrical, Fire Protection, Architecture). Adapt your responses to the user's discipline when apparent.
```

### 2. Tool Usage Instructions

```text
## Tool Usage Guidelines

- **Query before acting**: When uncertain about element locations, types, or parameters, use read tools first to gather information before making modifications.
- **Batch operations**: For repetitive tasks (placing multiple columns, modifying many elements), plan the sequence and execute tools efficiently.
- **Handle ambiguity**: If user intent is unclear, ask for clarification rather than guessing. Examples:
  - "Which wall type should I use?" (if multiple match the description)
  - "Should I place this at Level 1 or Level 2?" (if level is ambiguous)
- **Report results**: After executing tools, summarize what was done including element IDs for reference.
- **Chain tools logically**: For complex operations like "place columns at all grid intersections":
  1. First call get_grids to retrieve grid geometry
  2. Calculate intersection points
  3. Call place_column for each location
```

### 3. Safety Rules

```text
## Safety Rules

- **Always confirm before deleting**: Never delete elements without explicit user confirmation.
- **Protect views and sheets**: Do not modify elements on sheets or in detail views without confirmation.
- **Single undo operations**: Wrap related operations in a single transaction group so users can undo with one Ctrl+Z.
- **Query before modify**: When uncertain about element properties, query first to avoid unintended changes.
- **Validate before execution**: Check that element IDs exist and parameters are valid before attempting modifications.
- **Report failures clearly**: If a tool fails, explain what went wrong and suggest alternatives.
```

### 4. Context Injection Point

```text
## Current Revit Context

[CONTEXT INJECTION POINT]

The above context is automatically gathered from the current Revit state. Use this information to:
- Default placements to the active level when not specified
- Reference selected elements when the user says "this" or "these"
- Understand the scope of view-specific queries
```

### 5. Discipline Awareness

```text
## Discipline Context

[If configured, include discipline-specific guidance]

**Structural Engineering:**
- Use structural column/beam/brace families
- Consider load paths and connections
- Default to structural templates for framing

**MEP Engineering:**
- Consider system assignments for ducts/pipes
- Check for routing clearances
- Use appropriate equipment families

**Fire Protection:**
- Reference fire code requirements
- Consider coverage areas for sprinklers
- Identify fire-rated assemblies
```

### 6. Unit Awareness

```text
## Units and Measurements

Revit uses decimal feet internally. Accept user input in common formats and convert appropriately:
- Feet and inches: 10'-6" or 10' 6"
- Feet only: 10' or 10.5
- Inches: 126" (will convert to 10.5')
- Metric: 3048mm or 3.048m (will convert to 10')

When reporting dimensions back to users:
- Default to feet-inches format (10'-6")
- Use the format the user has been using in the conversation
- For metric users, provide metric equivalents

Common conversions:
- 1 foot = 12 inches = 304.8mm = 0.3048m
```

### Complete System Prompt Template

```csharp
public string BuildCompleteSystemPrompt(RevitContext context, string? discipline = null)
{
    var sb = new StringBuilder();

    // 1. Role definition
    sb.AppendLine(RoleDefinition);
    sb.AppendLine();

    // 2. Tool usage instructions
    sb.AppendLine(ToolUsageInstructions);
    sb.AppendLine();

    // 3. Safety rules
    sb.AppendLine(SafetyRules);
    sb.AppendLine();

    // 4. Context injection
    sb.AppendLine("## Current Revit Context");
    sb.AppendLine(FormatContext(context));
    sb.AppendLine();

    // 5. Discipline awareness (if configured)
    if (!string.IsNullOrEmpty(discipline))
    {
        sb.AppendLine(GetDisciplineGuidance(discipline));
        sb.AppendLine();
    }

    // 6. Unit awareness
    sb.AppendLine(UnitAwareness);

    return sb.ToString();
}
```

---

## A.8: API Key Management Options

### Option 1: Per-User Key (Default)

Each user enters their own Anthropic API key in the plugin settings. Key is encrypted using Windows DPAPI.

```csharp
// Storage location: %APPDATA%\RevitAI\config.json
{
  "encryptedApiKey": "AQAAANCMnd8BFdERjHoAwE..."
}
```

**Pros:** Simple, no infrastructure needed
**Cons:** Each user needs an API key, harder to track usage

### Option 2: Shared Team Key

A single API key stored in a team-accessible location.

```csharp
public class TeamKeyProvider
{
    // Option A: Environment variable
    public string? GetApiKey() =>
        Environment.GetEnvironmentVariable("REVITAI_API_KEY");

    // Option B: Network share config file
    public string? GetApiKey()
    {
        var teamConfigPath = @"\\server\share\revitai\config.json";
        if (!File.Exists(teamConfigPath)) return null;

        var config = JsonSerializer.Deserialize<TeamConfig>(File.ReadAllText(teamConfigPath));
        return SecureStorage.Decrypt(config.EncryptedApiKey);
    }
}
```

**Pros:** Single key to manage, easier billing tracking
**Cons:** Key visible in network location (use encryption)

### Option 3: Proxy Server (Most Secure)

Route API calls through an internal proxy that injects the API key server-side.

```csharp
public class ProxyClaudeApiService
{
    private readonly string _proxyUrl = "https://internal-proxy.company.com/claude";

    public async Task<ClaudeResponse> SendMessageAsync(...)
    {
        // Proxy adds API key server-side
        // Can also add: rate limiting, logging, cost tracking, content filtering
        var response = await _client.PostAsync(_proxyUrl, content);
        return JsonSerializer.Deserialize<ClaudeResponse>(await response.Content.ReadAsStringAsync());
    }
}
```

**Proxy Server Features:**
- API key never exposed to workstations
- Per-user usage tracking
- Rate limiting per user/project
- Request/response logging for audit
- Content filtering for sensitive data

**Pros:** Most secure, full control over usage
**Cons:** Requires infrastructure setup and maintenance

---

## A.9: Distribution & Installation

> This section has been consolidated into the main [README.md](../README.md) to avoid duplication. See:
> - [Quick Install](../README.md#quick-install) - End-user installation
> - [Manual Build](../README.md#manual-build) - Building from source
> - [Team Deployment](../README.md#team-deployment) - PowerShell and batch scripts

*The detailed scripts below are kept for reference but the authoritative versions are in README.md.*

### Manual Installation

1. Build the solution in Release mode
2. Copy files to the Revit addins folder:
   ```
   %APPDATA%\Autodesk\Revit\Addins\2026\
   ├── RevitAI.addin
   └── RevitAI\
       ├── RevitAI.dll
       └── (other dependencies)
   ```

### PowerShell Install Script

```powershell
# install-revitai.ps1
param(
    [string]$SourcePath = "\\server\share\RevitAI\latest",
    [string]$RevitYear = "2026"
)

$addinsPath = "$env:APPDATA\Autodesk\Revit\Addins\$RevitYear"
$pluginPath = "$addinsPath\RevitAI"

# Create directory if needed
if (-not (Test-Path $pluginPath)) {
    New-Item -ItemType Directory -Path $pluginPath | Out-Null
}

# Copy files
Copy-Item "$SourcePath\RevitAI.addin" $addinsPath -Force
Copy-Item "$SourcePath\RevitAI\*" $pluginPath -Recurse -Force

Write-Host "RevitAI installed successfully to $pluginPath"
Write-Host "Restart Revit to load the plugin."
```

### Batch File Alternative

```batch
@echo off
REM install-revitai.bat

set SOURCE=\\server\share\RevitAI\latest
set ADDINS=%APPDATA%\Autodesk\Revit\Addins\2026
set PLUGIN=%ADDINS%\RevitAI

if not exist "%PLUGIN%" mkdir "%PLUGIN%"

copy /Y "%SOURCE%\RevitAI.addin" "%ADDINS%\"
xcopy /Y /E "%SOURCE%\RevitAI\*" "%PLUGIN%\"

echo RevitAI installed. Restart Revit to load.
pause
```

### Update Process

1. Close Revit on all workstations
2. Update files on network share
3. Users run install script (or automatic via login script)
4. Revit loads new version on next startup

### Version Checking (Optional)

```csharp
public class UpdateChecker
{
    private readonly string _updateUrl = "\\\\server\\share\\RevitAI\\version.json";

    public async Task<bool> IsUpdateAvailableAsync()
    {
        var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
        var latestVersion = await GetLatestVersionAsync();
        return latestVersion > currentVersion;
    }

    public void NotifyUserIfUpdateAvailable()
    {
        if (IsUpdateAvailableAsync().Result)
        {
            TaskDialog.Show("RevitAI Update",
                "A new version of RevitAI is available. Please run the update script.");
        }
    }
}
