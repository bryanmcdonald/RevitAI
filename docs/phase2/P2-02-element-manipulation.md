# P2-02: Element Manipulation Tools

**Goal**: Add tools for assemblies, groups, copying, mirroring, arrays, alignment, and scope boxes.

**Prerequisites**: P2-01 complete.

**Key Files to Create**:
- `src/RevitAI/Tools/ModifyTools/CreateAssemblyTool.cs`
- `src/RevitAI/Tools/ModifyTools/CreateGroupTool.cs`
- `src/RevitAI/Tools/ModifyTools/CopyElementTool.cs`
- `src/RevitAI/Tools/ModifyTools/MirrorElementTool.cs`
- `src/RevitAI/Tools/ModifyTools/ArrayElementsTool.cs`
- `src/RevitAI/Tools/ModifyTools/AlignElementsTool.cs`
- `src/RevitAI/Tools/ModifyTools/PlaceScopeBoxTool.cs`

---

## Implementation Details

### 1. create_assembly

```csharp
// Input: { "element_ids": [123, 456], "name": "Typical Bay" }
// Use AssemblyInstance.Create
```

### 2. create_group

```csharp
// Input: { "element_ids": [123, 456], "name": "Column Group" }
// Use doc.Create.NewGroup
```

### 3. copy_element

```csharp
// Input: { "element_id": 123, "offset": [10, 0, 0] }
// Use ElementTransformUtils.CopyElement
```

### 4. mirror_element

```csharp
// Input: { "element_ids": [123], "axis_start": [0, 0, 0], "axis_end": [0, 100, 0] }
// Use ElementTransformUtils.MirrorElements
```

### 5. array_elements

```csharp
// Input: { "element_ids": [123], "count": 5, "spacing": [10, 0, 0], "type": "linear" }
// Or: { "element_ids": [123], "count": 8, "center": [0, 0, 0], "type": "radial" }
// Use ElementTransformUtils.CopyElement in a loop
```

### 6. align_elements

```csharp
// Input: { "element_ids": [123, 456, 789], "reference_id": 123, "alignment": "left" }
// Calculate alignment and use MoveElement
```

### 7. place_scope_box

```csharp
// Input: { "name": "Area A", "min": [0, 0, 0], "max": [100, 100, 40] }
// Create scope box element
```

---

## Verification (Manual)

1. Select multiple columns, ask Claude "Create an assembly from these"
2. Ask Claude "Copy this column 5 times at 25' spacing"
3. Ask Claude "Mirror the selected elements about grid A"
4. Ask Claude "Align these beams to the leftmost one"
