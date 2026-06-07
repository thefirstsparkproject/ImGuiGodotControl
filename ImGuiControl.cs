using Godot;
using ImGuiFSharp;
using Microsoft.FSharp.Core;
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.IO;

/// <summary>
/// Main Godot Control node for ImGui rendering.
/// Manages the context lifecycle and delegates frame layout to the IGuiBuilder.
/// </summary>
[GlobalClass]
public partial class ImGuiControl : Control
{
    static ImGuiControl()
    {
        Assembly fsAssembly = typeof(ImGuiFSharp.ImGuiInstance).Assembly;
        NativeLibrary.SetDllImportResolver(fsAssembly, (libraryName, assembly, searchPath) =>
        {
            if (libraryName == "ImGuiNative")
            {
                string baseDir = ProjectSettings.GlobalizePath("res://addons/ImGuiNative");
                string libName = "libImGuiNative.so";
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    libName = "ImGuiNative.dll";
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    libName = "libImGuiNative.dylib";
                }

                string fullPath = Path.Combine(baseDir, libName);
                if (File.Exists(fullPath))
                {
                    try
                     {
                        IntPtr handle = NativeLibrary.Load(fullPath);
                        GD.Print($"ImGuiControl: Successfully loaded native library from {fullPath}");
                        return handle;
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"ImGuiControl: Failed to load native library from {fullPath}. Exception: {ex}");
                    }
                }

                string fallbackPath = Path.Combine(AppContext.BaseDirectory, libName);
                if (File.Exists(fallbackPath))
                {
                    try
                    {
                        IntPtr handleFallback = NativeLibrary.Load(fallbackPath);
                        GD.Print($"ImGuiControl: Successfully loaded native library from fallback {fallbackPath}");
                        return handleFallback;
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"ImGuiControl: Failed to load native library from fallback {fallbackPath}. Exception: {ex}");
                    }
                }
            }
            return IntPtr.Zero;
        });
    }

    private ImGuiInstance _instance;
    private GodotImGuiBackend _backend;
    private Rid _rsTexRid;   // RenderingServer texture backed by the RD offscreen texture

    // Window configuration properties
    [Export] public bool FullScreenWindow { get; set; } = false;
    [Export] public string FullScreenWindowTitle { get; set; } = "ImGuiWindow";
    [Export] public bool FullScreenWindowNoHeader { get; set; } = false;

    // Interface accessors
    public IGuiFunctions    Gui    { get; protected set; }
    public IPlotFunctions   Plot   { get; protected set; }
    public IPlot3DFunctions Plot3D { get; protected set; }
    public IFontFunctions   Fonts  { get; protected set; }

    // Custom layout builder
    public IGuiBuilder Builder { get; set; }

    public override void _Ready()
    {
        var rd = RenderingServer.GetRenderingDevice();
        if (rd == null)
        {
            GD.PrintErr("ImGuiControl: RenderingDevice not available. Use Forward+ or Mobile renderer.");
            return;
        }

        var w = Mathf.Max(1, (int)Size.X);
        var h = Mathf.Max(1, (int)Size.Y);

        _backend = new GodotImGuiBackend(rd, w, h);
        _instance = new ImGuiInstance(_backend);
        
        _instance.FullScreenWindow = FullScreenWindow;
        _instance.FullScreenWindowTitle = FullScreenWindowTitle;
        _instance.FullScreenWindowNoHeader = FullScreenWindowNoHeader;

        _instance.Initialize();

        // Expose interfaces
        Gui    = _instance.GuiImpl;
        Plot   = _instance.PlotImpl;
        Plot3D = _instance.Plot3DImpl;
        Fonts  = _instance.FontsImpl;

        var rdTex = _backend.OffscreenRdTexRid;
        if (rdTex.IsValid)
            _rsTexRid = RenderingServer.TextureRdCreate(rdTex);

        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _Process(double delta)
    {
        if (_instance == null || Builder == null) return;

        _instance.ActivateContext();

        var oldRdTex = _backend.OffscreenRdTexRid;

        _instance.Process(delta, Size.X, Size.Y, Builder);

        var newRdTex = _backend.OffscreenRdTexRid;
        if (newRdTex != oldRdTex)
        {
            if (_rsTexRid.IsValid)
            {
                RenderingServer.FreeRid(_rsTexRid);
                _rsTexRid = default;
            }
            if (newRdTex.IsValid)
            {
                _rsTexRid = RenderingServer.TextureRdCreate(newRdTex);
            }
        }

        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (_instance == null) return;
        _instance.ActivateContext();
        GodotImGuiInputBridge.Translate(@event);
        AcceptEvent();
    }

    public override void _Input(InputEvent @event)
    {
        if (_instance == null) return;
        _instance.ActivateContext();
        GodotImGuiInputBridge.TranslateGlobal(@event);
    }

    public override void _ExitTree()
    {
        if (_rsTexRid.IsValid)
        {
            RenderingServer.FreeRid(_rsTexRid);
            _rsTexRid = default;
        }
        var instance = _instance;
        _instance = null;
        _backend = null;
        if (instance != null)
        {
            RenderingServer.CallOnRenderThread(Callable.From(() =>
            {
                ((System.IDisposable)instance).Dispose();
            }));
        }
    }

    public override void _Draw()
    {
        if (_rsTexRid.IsValid)
            RenderingServer.CanvasItemAddTextureRect(
                GetCanvasItem(), new Rect2(Vector2.Zero, Size), _rsTexRid);
    }

    public virtual void MoveWindowsToVisibleRange()
    {
        if (_instance == null || FullScreenWindow) return;
        _instance.ActivateContext();
        _instance.MoveWindowsToVisibleRange();
    }

    // ── C#-callable helpers for optional bool-ref parameters ─────────────────

    public void ShowImGuiDemoWindow(ref bool pOpen)
    {
        var r = new FSharpRef<bool>(pOpen);
        Gui.ShowDemoWindow(FSharpOption<FSharpRef<bool>>.Some(r));
        pOpen = r.Value;
    }

    public void ShowImGuiDemoWindow()
    {
        Gui.ShowDemoWindow(FSharpOption<FSharpRef<bool>>.None);
    }

    public void ShowImPlotDemoWindow(ref bool pOpen)
    {
        var r = new FSharpRef<bool>(pOpen);
        Plot.ShowDemoWindow(FSharpOption<FSharpRef<bool>>.Some(r));
        pOpen = r.Value;
    }

    public void ShowImPlot3DDemoWindow(ref bool pOpen)
    {
        var r = new FSharpRef<bool>(pOpen);
        Plot3D.ShowDemoWindow(FSharpOption<FSharpRef<bool>>.Some(r));
        pOpen = r.Value;
    }
}
