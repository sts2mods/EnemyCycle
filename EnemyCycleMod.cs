using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace EnemyCycle;

[ModInitializer("Initialize")]
public static class EnemyCycleMod
{
    public const string Version = "0.1.0";
    public const string LogPrefix = "[Enemy Cycle] ";

    // Master toggle — wire up to settings later. Starts on so we can
    // verify behavior; can flip via a hotkey or a settings menu entry.
    public static bool Enabled = true;

    // Where (if anywhere) the preview rows render above the enemy.
    // Always   — rows always visible (the original behavior).
    // OnHover  — rows hidden until the cursor enters the hitbox above
    //            the enemy; the hitbox itself stays put so clicking
    //            still opens the modal.
    // Never    — no rows, no hitbox; the only way to see move info is
    //            to open the modal via the live intent. In this mode
    //            the modal collapses to just the Pattern section.
    public enum PreviewMode { Always, OnHover, Never }
    public static PreviewMode Mode = PreviewMode.Always;

    private static Harmony? _harmony;

    public static void Initialize()
    {
        GD.Print($"{LogPrefix}v{Version} initializing...");
        try
        {
            _harmony = new Harmony("austin.enemycycle");
            ModHelpers.TryPatchAll(_harmony, typeof(EnemyCycleMod).Assembly, LogPrefix);
            EnemyCycleSettings.Load();
            GD.Print($"{LogPrefix}initial Enabled={Enabled} (press {EnemyCycleSettings.ToggleKey} to toggle)");
            // SceneTree isn't ready inside Initialize — defer hookup.
            Callable.From(EnemyCycleSettings.HookProcessFrame).CallDeferred();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{LogPrefix}Init failed: {ex}");
        }
    }
}
