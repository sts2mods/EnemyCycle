// Injects an Enable/Disable button into the mod info panel that
// shows up when the user opens Settings → Mods → Enemy Cycle.
// The same SetEnabled path the F9 hotkey uses, so persistence +
// overlay cleanup/reattach behaves identically.
using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Screens.ModdingScreen;
using MegaCrit.Sts2.addons.mega_text;

namespace EnemyCycle;

[HarmonyPatch(typeof(NModInfoContainer), nameof(NModInfoContainer.Fill))]
public static class NModInfoContainer_Fill_Postfix
{
    private const string ToggleButtonName = "EC_ToggleBtn";
    private const string StatusLabelName = "EC_StatusLbl";
    private const string ModeButtonName = "EC_ModeBtn";

    static void Postfix(NModInfoContainer __instance, Mod mod)
    {
        try
        {
            // Remove any leftover toggle UI from a previous selection
            // — Fill is called each time the user clicks a different
            // mod row, so we re-render fresh.
            __instance.GetNodeOrNull<Control>(ToggleButtonName)?.QueueFree();
            __instance.GetNodeOrNull<Control>(StatusLabelName)?.QueueFree();
            __instance.GetNodeOrNull<Control>(ModeButtonName)?.QueueFree();

            if (mod?.manifest?.id != "EnemyCycle") return;

            // Status label: visible "ON"/"OFF" indicator.
            var status = new Label
            {
                Name = StatusLabelName,
                Text = StatusText(),
                AnchorLeft = 0, AnchorTop = 1, AnchorRight = 0, AnchorBottom = 1,
                OffsetLeft = 20, OffsetTop = -70, OffsetRight = 220, OffsetBottom = -30,
                GrowVertical = Control.GrowDirection.Begin,
            };
            status.AddThemeFontSizeOverride("font_size", 18);
            status.AddThemeColorOverride("font_color", StsColors.cream);
            __instance.AddChild(status);

            // Toggle button — flips Enabled, saves to disk, and
            // reattaches/cleans overlays in-place.
            var btn = new Button
            {
                Name = ToggleButtonName,
                Text = EnemyCycleMod.Enabled ? "Disable" : "Enable",
                AnchorLeft = 1, AnchorTop = 1, AnchorRight = 1, AnchorBottom = 1,
                OffsetLeft = -180, OffsetTop = -70, OffsetRight = -20, OffsetBottom = -20,
                GrowHorizontal = Control.GrowDirection.Begin,
                GrowVertical = Control.GrowDirection.Begin,
            };
            btn.AddThemeFontSizeOverride("font_size", 18);
            var capturedStatus = status;
            var capturedBtn = btn;
            btn.Pressed += () =>
            {
                EnemyCycleSettings.SetEnabled(!EnemyCycleMod.Enabled);
                if (GodotObject.IsInstanceValid(capturedBtn))
                    capturedBtn.Text = EnemyCycleMod.Enabled ? "Disable" : "Enable";
                if (GodotObject.IsInstanceValid(capturedStatus))
                    capturedStatus.Text = StatusText();
            };
            __instance.AddChild(btn);

            // 3-cycle button: Always → On hover → Never. Sits just
            // above the Enable/Disable so the layout stays tidy.
            var modeBtn = new Button
            {
                Name = ModeButtonName,
                Text = "Preview: " + ModeText(EnemyCycleMod.Mode),
                AnchorLeft = 1, AnchorTop = 1, AnchorRight = 1, AnchorBottom = 1,
                OffsetLeft = -260, OffsetTop = -130, OffsetRight = -20, OffsetBottom = -80,
                GrowHorizontal = Control.GrowDirection.Begin,
                GrowVertical = Control.GrowDirection.Begin,
            };
            modeBtn.AddThemeFontSizeOverride("font_size", 18);
            var capturedModeBtn = modeBtn;
            var capturedStatus2 = status;
            modeBtn.Pressed += () =>
            {
                EnemyCycleSettings.CycleMode();
                if (GodotObject.IsInstanceValid(capturedModeBtn))
                    capturedModeBtn.Text = "Preview: " + ModeText(EnemyCycleMod.Mode);
                if (GodotObject.IsInstanceValid(capturedStatus2))
                    capturedStatus2.Text = StatusText();
            };
            __instance.AddChild(modeBtn);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{EnemyCycleMod.LogPrefix}mod info patch: {ex.Message}");
        }
    }

    private static string StatusText() =>
        $"Status: {(EnemyCycleMod.Enabled ? "ON" : "OFF")}   (Hotkey: {EnemyCycleSettings.ToggleKey})   Preview: {ModeText(EnemyCycleMod.Mode)}";

    private static string ModeText(EnemyCycleMod.PreviewMode m) => m switch
    {
        EnemyCycleMod.PreviewMode.Always  => "Always",
        EnemyCycleMod.PreviewMode.OnHover => "On hover",
        EnemyCycleMod.PreviewMode.Never   => "Never",
        _ => m.ToString(),
    };
}
