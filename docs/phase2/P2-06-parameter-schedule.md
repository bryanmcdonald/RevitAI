# P2-06: Parameter & Schedule Tools

**Goal**: Add bulk parameter modification and schedule operations.

**Prerequisites**: P2-05 complete.

**Status**: Complete

**Key Files Created**:
- `src/RevitAI/Tools/ReadTools/ReadScheduleDataTool.cs`
- `src/RevitAI/Tools/ReadTools/ExportElementDataTool.cs`
- `src/RevitAI/Tools/ModifyTools/BulkModifyParametersTool.cs`

**Key Files Modified**:
- `src/RevitAI/App.cs` (registered 3 new tools)

> **Note**: `create_schedule_view` is implemented in Phase 1.5 (P1.5-02) under ViewTools.

> **Spec deviation**: The spec placed `ExportElementDataTool.cs` in `ModifyTools/` but the tool is read-only (no transaction). Placed in `ReadTools/` to follow established convention.

---

## Tools Implemented

### 1. `read_schedule_data` (ReadTools, read-only)

- **Input**: `schedule_name` (string, required), `max_rows` (int, optional, default/max 200)
- **Logic**: Finds ViewSchedule by name (case-insensitive, exact match first then partial match), extracts headers from field definitions (skips hidden fields), reads body rows via `GetCellText` using visible column indexes only
- **Output**: JSON with `schedule_name`, `headers` array, `rows` (list of list of strings), `total_rows`, `returned_rows`, `truncated`, `truncated_message`
- **RequiresTransaction**: false
- **RequiresConfirmation**: false
- **Caps**: Max 200 rows
- Internal schedules (name starts with `<`) are excluded from search

### 2. `export_element_data` (ReadTools, read-only)

- **Input**: `category` (string), `format` ("csv" or "json"), optional `parameters` (string array, defaults to Family/Type/Level/Mark), optional `level` (string)
- **Logic**: Collects elements by category (via `CategoryHelper`), applies optional level filter (multi-strategy BuiltInParameter checks), extracts requested parameters (instance then type lookup), formats as CSV (RFC 4180) or JSON
- **Output**: JSON wrapper with `data` (CSV string) or `json_data` (structured array), `columns`, element counts, truncation info
- **RequiresTransaction**: false
- **RequiresConfirmation**: false
- **Caps**: Max 500 elements
- Special parameter names: "Family" (uses ALL_MODEL_FAMILY_NAME), "Type" (type element name), "Level" (multi-strategy resolution), "Mark" (ALL_MODEL_MARK), "Id" (element ID)
- CSV escaping follows RFC 4180 (commas, quotes, newlines trigger quoting, internal quotes doubled)

### 3. `bulk_modify_parameters` (ModifyTools, transaction + confirmation)

- **Input**: `category` (string), optional `filter` (object: `parameter` + `value`), optional `level` (string), `modify` (object: `parameter` + `value`)
- **Logic**: FilteredElementCollector by category, apply level filter (multi-strategy BuiltInParameter checks), apply parameter value filter (case-insensitive display value match), set modify parameter with `{index}` / `{index:N}` support
- **Output**: JSON with counts (matched, modified, skipped, failed) + affected element IDs for auto-selection
- **RequiresTransaction**: true
- **RequiresConfirmation**: true (with `GetDryRunDescription`)
- **Caps**: Max 1000 elements per operation
- Uses `[GeneratedRegex]` source generator for `{index}` placeholder pattern
- Validates parameter exists/writable on first element before iterating; lists writable parameters on error
- Returns `OkWithElements()` for P2-05 auto-selection integration

---

## `{index}` Placeholder System

- `{index}` -> sequential numbering: 1, 2, 3...
- `{index:N}` -> zero-padded: `{index:3}` produces 001, 002, 003
- Regex: `\{index(?::(\d+))?\}` (case-insensitive)
- Applied per-element using 1-based index, before setting on element

---

## Parameter Discovery

The AI discovers parameter names through existing tools:

1. **Standard parameters** (Mark, Comments, Length, Area, Level, etc.) - known by default
2. **Custom/shared parameters** - call `get_element_properties` on a representative element
3. **Error feedback** - if `bulk_modify_parameters` receives an invalid parameter name, it returns an error listing all writable parameters on the first matched element

---

## Implementation Notes

- **Inline duplication**: Helper methods (`FindLevelByName`, `IsElementOnLevel`, `GetParameterValueString`, etc.) are copied inline per tool, following the established codebase pattern (no shared helpers beyond `CategoryHelper`)
- **Hidden column handling**: `ReadScheduleDataTool` uses field definitions to build a visible column index map, ensuring headers and body data stay aligned when schedules have hidden fields
- **JSON export avoids double-serialization**: When `format == "json"`, the export tool stores row data as a `JsonData` structured property rather than a serialized-JSON string inside another JSON string
- **ElementId parameter support**: `BulkModifyParametersTool.SetParameterStringValue` handles all four storage types including ElementId (parsed from string as long)

---

## Verification (Manual)

1. "What's in the Column Schedule?" -> verify headers match visible fields, data rows align
2. "Set the Mark to 'COL-L1-{index:3}' for all structural columns on Level 1" -> verify COL-L1-001, COL-L1-002... and confirmation dialog appears
3. "Export all wall data to CSV with Type, Length, Area" -> verify CSV output with RFC 4180 escaping
4. "Export door data as JSON" -> verify structured JSON with default parameters (Family, Type, Level, Mark)
5. Confirm modified elements are auto-selected in viewport (P2-05 integration)
6. Verify error messages list available schedules/levels/parameters when not found

> **Note**: Schedule creation is tested in Phase 1.5 (P1.5-02).
