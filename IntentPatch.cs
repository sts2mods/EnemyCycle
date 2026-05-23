// Hook NCreature.UpdateIntent so every time a monster refreshes its
// intent (start of turn / after move performed), we attach our cycle
// preview stack showing the next 2 moves.
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace EnemyCycle;

[HarmonyPatch(typeof(NCreature), nameof(NCreature.UpdateIntent))]
public static class NCreature_UpdateIntent_Postfix
{
    static void Postfix(NCreature __instance, IEnumerable<Creature> targets)
    {
        if (!EnemyCycleMod.Enabled) return;
        var monster = __instance?.Entity?.Monster;
        if (monster == null) return;
        // Skip the modal's cloned preview NCreature — it shouldn't
        // grow its own preview stack above its head when its move
        // animation triggers an intent refresh.
        if (ModalCreatureRegistry.IsModalCreatureNode(__instance!)) return;
        try
        {
            // Only show moves whose appearance is forced — never leak
            // RNG outcomes (e.g. on TwigSlimeM, the Pokey/Sticky roll).
            var peeked = MovePredictor.PeekDeterministic(monster, 2);
            CyclePreview.Attach(__instance, monster, peeked, targets);

            // Power/card chips for the move the live intent represents,
            // bobbed in sync with that intent.
            var sm = MovePredictor.GetStateMachine(monster);
            var currentMove = sm != null
                ? MovePredictor.GetCurrentState(sm) as MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine.MoveState
                : null;
            LiveIntentChips.Attach(__instance, monster, currentMove);

            // Wire click → open modal directly on each NIntent so the
            // live intent area stays click-to-open without the preview
            // anchor needing to extend over (and block hover of) the
            // game's intent icons.
            WireIntentClicks(__instance, monster);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{EnemyCycleMod.LogPrefix}UpdateIntent postfix: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private static void WireIntentClicks(NCreature creatureNode, MonsterModel monster)
    {
        var container = creatureNode.IntentContainer;
        if (container == null) return;
        foreach (var child in container.GetChildren())
        {
            if (child is not NIntent ni) continue;
            if (ni.HasMeta("ec_click_wired")) continue;
            ni.SetMeta("ec_click_wired", true);
            var capMonster = monster;
            var capCreatureNode = creatureNode;
            ni.Connect(Control.SignalName.GuiInput,
                Callable.From<InputEvent>(ev => OnLiveIntentClick(ev, capMonster, capCreatureNode)));
            // OnHover preview mode: hovering the live intent counts
            // toward the same hover state the anchor uses, so the
            // rows reveal whether the cursor is over the preview area
            // or the current attack itself — matching the click
            // hitbox the modal already exposes.
            ni.MouseEntered += () => SetIntentHover(capCreatureNode, true);
            ni.MouseExited  += () => SetIntentHover(capCreatureNode, false);
        }
    }

    private static void SetIntentHover(NCreature creatureNode, bool over)
    {
        try
        {
            if (!GodotObject.IsInstanceValid(creatureNode)) return;
            creatureNode.SetMeta(CyclePreview.HoverIntentMeta, over);
            CyclePreview.RefreshHoverVisibility(creatureNode);
        }
        catch { /* cosmetic — eat */ }
    }

    private static void OnLiveIntentClick(InputEvent ev, MonsterModel monster, NCreature creatureNode)
    {
        if (ev is not InputEventMouseButton mb) return;
        if (mb.ButtonIndex != MouseButton.Left || !mb.Pressed) return;
        // Skip when the player is mid-card-play — clicking an enemy
        // there should target it, not open the cycle modal.
        if (ModalGuard.SuppressClicksForCardPlay()) return;
        try { CycleModal.Show(monster, creatureNode); }
        catch (Exception ex)
        {
            GD.PrintErr($"{EnemyCycleMod.LogPrefix}live intent click: {ex.Message}");
        }
    }
}
