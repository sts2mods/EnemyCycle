// Persistence + runtime toggle for the mod's master Enabled flag.
//
// Storage: a one-line cfg at user://enemy_cycle.cfg. We deliberately
// avoid Godot's ConfigFile API to keep the file human-readable and
// trivially editable in case anyone wants to flip the flag without
// running the game.
//
// Hotkey: F9 — toggles Enabled, saves immediately, and cleans up any
// existing on-screen overlays when turning the mod off so the
// indicator changes are visible right away.
using System;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace EnemyCycle;

public static class EnemyCycleSettings
{
    private const string ConfigPath = "user://enemy_cycle.cfg";
    private const string EnabledLine = "enabled=";
    private const string ModeLine = "mode=";
    public static readonly Key ToggleKey = Key.F9;

    public static void Load()
    {
        try
        {
            if (!FileAccess.FileExists(ConfigPath)) return;
            using var f = FileAccess.Open(ConfigPath, FileAccess.ModeFlags.Read);
            if (f == null) return;
            while (!f.EofReached())
            {
                var line = f.GetLine();
                if (line.StartsWith(EnabledLine, StringComparison.Ordinal))
                {
                    var v = line.Substring(EnabledLine.Length).Trim().ToLowerInvariant();
                    EnemyCycleMod.Enabled = v == "true" || v == "1" || v == "yes";
                }
                else if (line.StartsWith(ModeLine, StringComparison.Ordinal))
                {
                    var v = line.Substring(ModeLine.Length).Trim();
                    if (Enum.TryParse<EnemyCycleMod.PreviewMode>(v, true, out var m))
                        EnemyCycleMod.Mode = m;
                }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{EnemyCycleMod.LogPrefix}settings load: {ex.Message}");
        }
    }

    public static void Save()
    {
        try
        {
            using var f = FileAccess.Open(ConfigPath, FileAccess.ModeFlags.Write);
            if (f == null) return;
            f.StoreLine(EnabledLine + (EnemyCycleMod.Enabled ? "true" : "false"));
            f.StoreLine(ModeLine + EnemyCycleMod.Mode.ToString());
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{EnemyCycleMod.LogPrefix}settings save: {ex.Message}");
        }
    }

    public static void SetEnabled(bool enabled)
    {
        if (EnemyCycleMod.Enabled == enabled) return;
        EnemyCycleMod.Enabled = enabled;
        Save();
        GD.Print($"{EnemyCycleMod.LogPrefix}toggled {(enabled ? "ON" : "OFF")}");
        if (enabled) ReattachOverlays();
        else CleanupOverlays();
    }

    // Cycle through Always → OnHover → Never → Always. Tear down the
    // existing overlays unconditionally so stale rows from the
    // previous mode don't linger, then reattach (the new mode's
    // Attach is a no-op in Never).
    public static void CycleMode() => SetMode(NextMode(EnemyCycleMod.Mode, +1));
    public static void CycleModePrev() => SetMode(NextMode(EnemyCycleMod.Mode, -1));

    private static EnemyCycleMod.PreviewMode NextMode(EnemyCycleMod.PreviewMode m, int dir)
    {
        var values = (EnemyCycleMod.PreviewMode[])Enum.GetValues(typeof(EnemyCycleMod.PreviewMode));
        int idx = Array.IndexOf(values, m);
        if (idx < 0) idx = 0;
        idx = (idx + dir + values.Length) % values.Length;
        return values[idx];
    }

    public static void SetMode(EnemyCycleMod.PreviewMode mode)
    {
        if (EnemyCycleMod.Mode == mode) return;
        EnemyCycleMod.Mode = mode;
        Save();
        GD.Print($"{EnemyCycleMod.LogPrefix}preview mode → {mode}");
        CleanupOverlays();
        if (EnemyCycleMod.Enabled) ReattachOverlays();
    }

    // Re-build preview + live chip overlays on every visible NCreature.
    // IntentPatch only fires on UpdateIntent, so without this the mod
    // appears not to come back on until the player advances a turn.
    private static void ReattachOverlays()
    {
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null) return;
            foreach (var nc in FindAllOfType<NCreature>(tree.Root))
            {
                var monster = nc.Entity?.Monster;
                if (monster == null) continue;
                try
                {
                    var peeked = MovePredictor.PeekDeterministic(monster, 2);
                    var targets = nc.Entity!.CombatState?.Players?
                        .Select(p => p.Creature).ToList() ?? new System.Collections.Generic.List<Creature>();
                    CyclePreview.Attach(nc, monster, peeked, targets);
                    var sm = MovePredictor.GetStateMachine(monster);
                    var currentMove = sm != null
                        ? MovePredictor.GetCurrentState(sm) as MoveState
                        : null;
                    LiveIntentChips.Attach(nc, monster, currentMove);
                }
                catch (Exception inner)
                {
                    GD.PrintErr($"{EnemyCycleMod.LogPrefix}reattach single: {inner.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{EnemyCycleMod.LogPrefix}reattach: {ex.Message}");
        }
    }

    // Strip our preview anchor and live chip strip off every NCreature
    // in the scene so the change is visible immediately. New intents
    // post-toggle won't get attached because IntentPatch's gate skips
    // attach when Enabled is false.
    private static void CleanupOverlays()
    {
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null) return;
            foreach (var nc in FindAllOfType<NCreature>(tree.Root))
            {
                nc.GetNodeOrNull<Control>(CyclePreview.NodeName)?.QueueFree();
                nc.GetNodeOrNull<Control>(LiveIntentChips.NodeName)?.QueueFree();
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{EnemyCycleMod.LogPrefix}cleanup: {ex.Message}");
        }
    }

    private static System.Collections.Generic.IEnumerable<T> FindAllOfType<T>(Node root) where T : Node
    {
        if (root is T t) yield return t;
        foreach (var c in root.GetChildren())
            foreach (var r in FindAllOfType<T>(c))
                yield return r;
    }

    private static bool _hotkeyDownLast;
    private static float _watchdogAccum;
    // Sample only ~2× per second — cheap enough to run forever in
    // the background, frequent enough that the user notices the
    // overlay coming back within a beat if it ever vanishes.
    private const float WatchdogInterval = 0.5f;

    public static void HookProcessFrame()
    {
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null) return;
            tree.ProcessFrame += OnProcessFrame;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{EnemyCycleMod.LogPrefix}hook ProcessFrame: {ex.Message}");
        }
    }

    private static void OnProcessFrame()
    {
        bool down = Input.IsKeyPressed(ToggleKey);
        if (down && !_hotkeyDownLast)
        {
            SetEnabled(!EnemyCycleMod.Enabled);
        }
        _hotkeyDownLast = down;

        // Watchdog: every WatchdogInterval seconds, scan visible
        // NCreatures. If the mod is on and any of them lost their
        // preview anchor (an upstream exception kicked our overlays
        // off without UpdateIntent firing again), re-attach. Cheap
        // enough at 2 Hz to stay on permanently.
        _watchdogAccum += (float)(1.0 / 60.0); // approx since ProcessFrame fires per frame
        if (_watchdogAccum < WatchdogInterval) return;
        _watchdogAccum = 0f;
        try
        {
            if (!EnemyCycleMod.Enabled) return;
            if (EnemyCycleMod.Mode == EnemyCycleMod.PreviewMode.Never) return;
            ReattachMissingOverlays();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{EnemyCycleMod.LogPrefix}watchdog: {ex.Message}");
        }
    }

    private static void ReattachMissingOverlays()
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree == null) return;
        foreach (var nc in FindAllOfType<NCreature>(tree.Root))
        {
            if (nc == null || !GodotObject.IsInstanceValid(nc)) continue;
            // Only re-attach when there's a monster and no anchor.
            // Cloned modal-preview creatures are explicitly skipped
            // by ModalCreatureRegistry — match that behavior.
            var monster = nc.Entity?.Monster;
            if (monster == null) continue;
            if (ModalCreatureRegistry.IsModalCreatureNode(nc)) continue;
            if (nc.GetNodeOrNull<Control>(CyclePreview.NodeName) != null) continue;
            try
            {
                var peeked = MovePredictor.PeekDeterministic(monster, 2);
                var targets = nc.Entity!.CombatState?.Players?
                    .Select(p => p.Creature).ToList() ?? new System.Collections.Generic.List<Creature>();
                CyclePreview.Attach(nc, monster, peeked, targets);
                var sm = MovePredictor.GetStateMachine(monster);
                var currentMove = sm != null
                    ? MovePredictor.GetCurrentState(sm) as MoveState
                    : null;
                LiveIntentChips.Attach(nc, monster, currentMove);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"{EnemyCycleMod.LogPrefix}watchdog re-attach: {ex.Message}");
            }
        }
    }
}
