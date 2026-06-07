using Godot;
using ImGuiFSharp;

public static class GodotImGuiInputBridge
{
    private static int GodotToImGuiKey(Key key)
    {
        switch (key)
        {
            case Key.Tab: return 512;
            case Key.Left: return 513;
            case Key.Right: return 514;
            case Key.Up: return 515;
            case Key.Down: return 516;
            case Key.Pageup: return 517;
            case Key.Pagedown: return 518;
            case Key.Home: return 519;
            case Key.End: return 520;
            case Key.Insert: return 521;
            case Key.Delete: return 522;
            case Key.Backspace: return 523;
            case Key.Space: return 524;
            case Key.Enter: return 525;
            case Key.Escape: return 526;
            case Key.Apostrophe: return 527;
            case Key.Comma: return 528;
            case Key.Minus: return 529;
            case Key.Period: return 530;
            case Key.Slash: return 531;
            case Key.Key0: return 532;
            case Key.Key1: return 533;
            case Key.Key2: return 534;
            case Key.Key3: return 535;
            case Key.Key4: return 536;
            case Key.Key5: return 537;
            case Key.Key6: return 538;
            case Key.Key7: return 539;
            case Key.Key8: return 540;
            case Key.Key9: return 541;
            case Key.Semicolon: return 542;
            case Key.Equal: return 543;
            case Key.A: return 546;
            case Key.B: return 547;
            case Key.C: return 548;
            case Key.D: return 549;
            case Key.E: return 550;
            case Key.F: return 551;
            case Key.G: return 552;
            case Key.H: return 553;
            case Key.I: return 554;
            case Key.J: return 555;
            case Key.K: return 556;
            case Key.L: return 557;
            case Key.M: return 558;
            case Key.N: return 559;
            case Key.O: return 560;
            case Key.P: return 561;
            case Key.Q: return 562;
            case Key.R: return 563;
            case Key.S: return 564;
            case Key.T: return 565;
            case Key.U: return 566;
            case Key.V: return 567;
            case Key.W: return 568;
            case Key.X: return 569;
            case Key.Y: return 570;
            case Key.Z: return 571;
            case Key.F1: return 590;
            case Key.F2: return 591;
            case Key.F3: return 592;
            case Key.F4: return 593;
            case Key.F5: return 594;
            case Key.F6: return 595;
            case Key.F7: return 596;
            case Key.F8: return 597;
            case Key.F9: return 598;
            case Key.F10: return 599;
            case Key.F11: return 600;
            case Key.F12: return 601;
            default: return 0;
        }
    }

    public static void Translate(InputEvent @event)
    {
        if (@event is InputEventWithModifiers m)
        {
            var mods = m.GetModifiersMask();
            ImGuiNative.IGN_Input_SetModifiers(
                mods.HasFlag(KeyModifierMask.MaskCtrl),
                mods.HasFlag(KeyModifierMask.MaskShift),
                mods.HasFlag(KeyModifierMask.MaskAlt),
                mods.HasFlag(KeyModifierMask.MaskMeta)
            );
        }

        if (@event is InputEventMouseMotion mm)
        {
            ImGuiNative.IGN_Input_SetMousePos(mm.Position.X, mm.Position.Y);
        }
        else if (@event is InputEventMouseButton mb)
        {
            ImGuiNative.IGN_Input_SetMousePos(mb.Position.X, mb.Position.Y);
            int btn = -1;
            switch (mb.ButtonIndex)
            {
                case MouseButton.Left: btn = 0; break;
                case MouseButton.Right: btn = 1; break;
                case MouseButton.Middle: btn = 2; break;
            }
            if (btn >= 0)
            {
                ImGuiNative.IGN_Input_SetMouseButton(btn, mb.Pressed);
            }

            if (mb.ButtonIndex == MouseButton.WheelUp)
            {
                ImGuiNative.IGN_Input_SetMouseWheel(0f, 1f);
            }
            else if (mb.ButtonIndex == MouseButton.WheelDown)
            {
                ImGuiNative.IGN_Input_SetMouseWheel(0f, -1f);
            }
        }
        else if (@event is InputEventKey k)
        {
            int ik = GodotToImGuiKey(k.Keycode);
            if (ik > 0)
            {
                ImGuiNative.IGN_Input_AddKey(ik, k.Pressed);
            }
            if (k.Pressed && k.Unicode > 0)
            {
                ImGuiNative.IGN_Input_AddChar((uint)k.Unicode);
            }
        }
    }

    public static void TranslateGlobal(InputEvent @event)
    {
        if (@event is InputEventKey k)
        {
            Translate(k);
        }
    }
}
