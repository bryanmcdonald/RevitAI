# P2-02: Element Manipulation Tools

**Goal**: Add tools for copying, mirroring, rotating, arraying, aligning elements, and creating groups/assemblies.

**Prerequisites**: P2-01 complete.

**Status**: Complete

**Key Files Created**:
- `src/RevitAI/Tools/ModifyTools/CopyElementTool.cs`
- `src/RevitAI/Tools/ModifyTools/MirrorElementTool.cs`
- `src/RevitAI/Tools/ModifyTools/RotateElementTool.cs`
- `src/RevitAI/Tools/ModifyTools/ArrayElementsTool.cs`
- `src/RevitAI/Tools/ModifyTools/AlignElementsTool.cs`
- `src/RevitAI/Tools/ModifyTools/CreateGroupTool.cs`
- `src/RevitAI/Tools/ModifyTools/CreateAssemblyTool.cs`

**Files Modified**:
- `src/RevitAI/App.cs` — registered 7 new tools
- `src/RevitAI/Services/ContextEngine.cs` — added tool usage notes for element manipulation

---

## Implementation Notes

- `place_scope_box` from the original spec was replaced with `rotate_element` because the Revit API has no public creation method for scope boxes.
- All tools follow the established pattern from `MoveElementTool.cs`: static schema, snake_case JSON, `RequiresTransaction = true`, `RequiresConfirmation = true`, dry-run descriptions.
- All tools accept `element_ids` as an array (multi-element support) and report `invalid_ids` for any IDs not found.
- Pinned elements are checked and reported (rotate, align) rather than silently failing.

---

## Tools (7)

### 1. copy_element

**Input**: `{ element_ids: int[], translation: [x, y, z] }`
**API**: `ElementTransformUtils.CopyElements(doc, elementIds, translationVector)`
**Returns**: `{ copied_count, new_element_ids[], invalid_ids?, translation[], message }`

### 2. mirror_element

**Input**: `{ element_ids: int[], axis_start: [x, y], axis_end: [x, y], copy?: bool(default true) }`
**API**: Creates a vertical mirror plane from the axis line. When `copy=true`, copies in place then mirrors the copies. When `copy=false`, mirrors elements directly.
**Returns**: `{ mirrored_count, is_copy, new_element_ids?, invalid_ids?, axis_start[], axis_end[], message }`

### 3. rotate_element

**Input**: `{ element_ids: int[], angle: number(degrees), center?: [x, y] }`
**API**: `ElementTransformUtils.RotateElements(doc, ids, verticalAxis, angleRadians)`
- Positive angle = counterclockwise when viewed from above
- If no center provided, computes combined bounding box center
**Returns**: `{ rotated_count, angle_degrees, center[], invalid_ids?, pinned_ids?, message }`

### 4. array_elements

**Input**:
```json
{
  "element_ids": [int],
  "count": 1-100,
  "array_type": "linear" | "radial",
  "spacing": [x, y, z],
  "center": [x, y],
  "total_angle": number,
  "angle_between": number
}
```
- Linear: requires `spacing`, loops `count` times with `CopyElements` at offset `spacing * i`
- Radial: requires `center`, loops `count` times copying in place then rotating by `angle_between * i`
- `angle_between` overrides `total_angle`; default `total_angle` is 360 degrees
**Returns**: `{ array_type, copy_count, total_new_elements, new_element_ids[], spacing?, center?, angle_between?, message }`

### 5. align_elements

**Input**: `{ element_ids: int[], reference_id: int, alignment: "left"|"right"|"top"|"bottom"|"center_horizontal"|"center_vertical" }`
**API**: Gets reference bounding box target coordinate, moves each element to match using `MoveElement`.
- left = min X, right = max X, top = max Y, bottom = min Y, center_horizontal = mid X, center_vertical = mid Y
**Returns**: `{ aligned_count, alignment, reference_id, skipped_pinned_ids?, skipped_no_bbox_ids?, message }`

### 6. create_group

**Input**: `{ element_ids: int[], name?: string }`
**API**: `doc.Create.NewGroup(elementIds)`, optionally renames group type
**Validation**: Minimum 2 elements required
**Returns**: `{ group_id, group_type_id, group_name, member_count, invalid_ids?, message }`

### 7. create_assembly

**Input**: `{ element_ids: int[], name?: string, naming_category?: string }`
**API**: `AssemblyInstance.Create(doc, ids, namingCategoryId)` with `doc.Regenerate()` after creation
- Auto-detects naming category from most common element category if not specified
- Pre-validates with `AssemblyInstance.AreElementsValidForAssembly`
**Returns**: `{ assembly_id, assembly_name, naming_category, member_count, invalid_ids?, message }`

---

## Verification (Manual)

1. Select a wall -> "Copy this wall 10 feet east" -> verify copy at correct offset
2. Select elements -> "Mirror these about a line from (0,0) to (0,100)" -> verify mirrored copies
3. Select a column -> "Rotate this 45 degrees" -> verify rotation
4. Select a column -> "Create 5 copies at 25-foot spacing going east" -> verify linear array
5. Select a column -> "Array this 8 times in a circle around (50, 50)" -> verify radial array
6. Select misaligned columns -> "Align these to the leftmost one" -> verify alignment
7. Select multiple elements -> "Group these as 'Typical Bay'" -> verify group in project browser
8. Select structural elements -> "Create an assembly from these" -> verify assembly
9. Ctrl+Z after any tool -> verify single undo operation
10. All tools show confirmation dialog before executing
