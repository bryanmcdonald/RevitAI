# P1-01: Project Setup & Hello World

**Goal**: Create the solution structure and a minimal plugin that loads successfully in Revit 2026.

**Prerequisites**: Development environment setup complete.

**Key Files to Create**:
- `RevitAI.sln`
- `src/RevitAI/RevitAI.csproj`
- `src/RevitAI/App.cs`
- `RevitAI.addin`

---

## Implementation Details

### 1. Create Solution and Project

```xml
<!-- RevitAI.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <EnableDefaultItems>true</EnableDefaultItems>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include="RevitAPI">
      <HintPath>C:\Program Files\Autodesk\Revit 2026\RevitAPI.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="RevitAPIUI">
      <HintPath>C:\Program Files\Autodesk\Revit 2026\RevitAPIUI.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

### 2. Create IExternalApplication Entry Point

```csharp
// App.cs
using Autodesk.Revit.UI;
using Autodesk.Revit.DB;

namespace RevitAI;

public class App : IExternalApplication
{
    public Result OnStartup(UIControlledApplication application)
    {
        TaskDialog.Show("RevitAI", "RevitAI plugin loaded successfully!");
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        return Result.Succeeded;
    }
}
```

### 3. Create Addin Manifest

```xml
<!-- RevitAI.addin -->
<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>RevitAI</Name>
    <Assembly>RevitAI.dll</Assembly>
    <FullClassName>RevitAI.App</FullClassName>
    <AddInId>YOUR-GUID-HERE</AddInId>
    <VendorId>RevitAI</VendorId>
    <VendorDescription>RevitAI Plugin</VendorDescription>
  </AddIn>
</RevitAddIns>
```

---

## Verification (Manual)

1. Build the solution in Release mode
2. Copy `RevitAI.dll` and `RevitAI.addin` to `%AppData%\Autodesk\Revit\Addins\2026\`
3. Launch Revit 2026
4. Confirm "RevitAI plugin loaded successfully!" dialog appears
