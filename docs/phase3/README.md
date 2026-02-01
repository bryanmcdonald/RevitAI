# Phase 3: Advanced & Multi-Discipline

> Phase 3 chunks are outlined for future planning sessions.
> Each will be detailed when ready for implementation.
> Phase 2 must be complete before starting Phase 3.

---

## Overview

Phase 3 adds discipline-specific tool packs, export/reporting capabilities, model health analysis, and custom workflow templates. Upon completion, the plugin supports specialized workflows for Structural, MEP, Fire Protection, and Architecture disciplines.

---

## Chunk Index

| Chunk | Name | Description | Prerequisites |
|-------|------|-------------|---------------|
| [P3-01](P3-01-structural-tools.md) | Structural Tool Pack | Foundations, braces, trusses, connections, load path | Phase 2 |
| [P3-02](P3-02-mep-tools.md) | MEP Tool Pack | Duct/pipe/conduit routing, equipment, systems, clashes | P3-01 |
| [P3-03](P3-03-fire-protection-tools.md) | Fire Protection Tool Pack | Sprinklers, fire-rated assemblies, egress, hazmat | P3-02 |
| [P3-04](P3-04-architecture-tools.md) | Architecture Tool Pack | Rooms, doors, windows, curtain walls, area plans | P3-03 |
| [P3-05](P3-05-export-tools.md) | Export & Reporting Tools | Image, PDF, IFC, DWG export, custom reports | P3-04 |
| [P3-06](P3-06-model-health.md) | Model Health Tools | Warnings, overlaps, unhosted elements, duplicates | P3-05 |
| [P3-07](P3-07-prompt-templates.md) | Custom Prompt Templates | Saveable workflow templates, team sharing | P3-06 |

---

## Key Files Created in Phase 3

```
src/RevitAI/
├── Tools/
│   ├── StructuralTools/              # P3-01
│   │   ├── PlaceFoundationTool.cs
│   │   ├── PlaceBraceTool.cs
│   │   ├── PlaceTrussTool.cs
│   │   ├── CreateStructuralConnectionTool.cs
│   │   └── AnalyzeLoadPathTool.cs
│   ├── MEPTools/                     # P3-02
│   │   ├── RouteDuctTool.cs
│   │   ├── RoutePipeTool.cs
│   │   ├── RouteConduitTool.cs
│   │   ├── PlaceEquipmentTool.cs
│   │   ├── CreateSystemTool.cs
│   │   └── CheckClashesTool.cs
│   ├── FireProtectionTools/          # P3-03
│   │   ├── LayoutSprinklersTool.cs
│   │   ├── IdentifyFireRatedTool.cs
│   │   ├── AnalyzeEgressTool.cs
│   │   ├── IdentifyHazardousAreasTool.cs
│   │   └── CalculateCoverageTool.cs
│   ├── ArchitectureTools/            # P3-04
│   │   ├── CreateRoomTool.cs
│   │   ├── PlaceDoorTool.cs
│   │   ├── PlaceWindowTool.cs
│   │   ├── CreateCurtainWallTool.cs
│   │   ├── ModifyCurtainPanelTool.cs
│   │   └── CreateAreaPlanTool.cs
│   ├── ExportTools/                  # P3-05
│   │   ├── ExportViewToImageTool.cs
│   │   ├── PrintSheetsTool.cs
│   │   ├── ExportToIfcTool.cs
│   │   ├── ExportToDwgTool.cs
│   │   └── GenerateReportTool.cs
│   └── ModelHealthTools/             # P3-06
│       ├── GetAllWarningsTool.cs
│       ├── FindOverlappingElementsTool.cs
│       ├── FindUnhostedElementsTool.cs
│       ├── FindElementsWithoutLevelTool.cs
│       ├── FindDuplicateInstancesTool.cs
│       └── AnalyzeModelSizeTool.cs
├── Templates/                        # P3-07
│   ├── PromptTemplate.cs
│   └── TemplateManager.cs
├── UI/
│   ├── TemplateSelector.xaml         # P3-07
│   └── TemplateSelectorViewModel.cs  # P3-07
└── templates/                        # P3-07
    ├── column-layout.json
    ├── qc-checker.json
    └── drawing-setup.json
```

---

## Phase 3 Completion Criteria

- [ ] Structural tools place foundations, braces, trusses
- [ ] MEP tools route ducts/pipes/conduits
- [ ] Fire protection tools layout sprinklers, analyze egress
- [ ] Architecture tools create rooms, place doors/windows
- [ ] Export tools generate images, PDFs, IFC, DWG
- [ ] Model health tools identify warnings, overlaps, issues
- [ ] Custom templates can be created, saved, and shared
- [ ] Discipline-specific system prompts activate correctly
