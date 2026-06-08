# ImGuiGodotControl

A high-performance C# GUI, plotting, and 3D visualization control for Godot 4, powered by ImGui and ImPlot via F# bindings.

This repository contains the C# control node (`ImGuiControl`) and backend integration (`GodotImGuiBackend`, `GodotImGuiInputBridge`) designed to be consumed as a NuGet package in Godot .NET projects.

## Architecture

This control sits on top of `ImGuiFSharp` (a low-level C#/F# binding to the native ImGui/ImPlot/ImPlot3D C++ bridge). 

```
+-----------------------------------------------------------+
|                      Your Godot Game                      |
+-----------------------------------------------------------+
                              |
                              v (C# NuGet Dependency)
+-----------------------------------------------------------+
|                    ImGuiGodotControl                      |
| (ImGuiControl.cs, GodotImGuiBackend.cs, Input Bridge)     |
+-----------------------------------------------------------+
                              |
                              v (F# NuGet Dependency)
+-----------------------------------------------------------+
|                       ImGuiFSharp                         |
| (Safe F# Wrappers, DllImport Bindings, P/Invoke)          |
+-----------------------------------------------------------+
                              |
                              v (Bundled Native Library)
+-----------------------------------------------------------+
|                       ImGuiNative                         |
| (C++ compiled bridge for ImGui + ImPlot + ImPlot3D)       |
+-----------------------------------------------------------+
```

## Installation

### 1. Add Local NuGet Source
Ensure your `nuget.config` points to the folder containing the local packages (or configure it to restore from a package registry like GitHub Packages):

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
    <add key="LocalNugets" value="../ImGuiNugets" />
  </packageSources>
</configuration>
```

### 2. Install Package
Add the package reference to your `.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="ImGuiGodotControl" Version="1.1.0" />
</ItemGroup>
```

## Getting Started

1. Add an `ImGuiControl` node to your Godot scene.
2. Implement the `IGuiBuilder` interface in one of your C# classes.
3. Assign the builder instance to your `ImGuiControl` node's `Builder` property.

Example C# script:

```csharp
using Godot;
using BehaviorTree; // Or your custom namespace
using ImGuiFSharp;

public partial class MyGuiNode : Node, IGuiBuilder
{
    private ImGuiControl _imGuiControl;

    public override void _Ready()
    {
        _imGuiControl = GetNode<ImGuiControl>("ImGuiControl");
        _imGuiControl.Builder = this;
    }

    public void OnGui(GuiApi api)
    {
        var gui = api.Gui;
        var plot = api.Plot;

        if (gui.Begin("Control Panel", null, null))
        {
            gui.Text("Hello from ImGuiGodotControl!");
            
            if (gui.Button("Click Me", 100, 25))
            {
                GD.Print("Button clicked!");
            }
            
            gui.End();
        }
    }
}
```

## Building & Packaging Locally

To build and package this library into a NuGet:

```bash
dotnet pack -c Release -o ../ImGuiNugets
```

## License

This project is licensed under the MIT License - see the LICENSE file for details.
