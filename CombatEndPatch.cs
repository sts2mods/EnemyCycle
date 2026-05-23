// Clean every preview anchor / chip strip off the NCreatures the
// moment the combat room leaves the tree. Without this, overlays
// linger on monsters that didn't die when the player ends the
// fight some other way (boss-kill while a minion lives, retreat,
// game-end), since UpdateIntent never fires again to refresh them.
using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace EnemyCycle;

[HarmonyPatch(typeof(NCombatRoom), "_ExitTree")]
public static class NCombatRoom_ExitTree_Prefix
{
    static void Prefix(NCombatRoom __instance)
    {
        try
        {
            // Walk the combat room and strip our overlays off every
            // creature inside. Doing it via __instance instead of the
            // root SceneTree avoids touching anything that lives
            // beyond the combat scope.
            foreach (var nc in EnumerateCreatures(__instance))
            {
                try
                {
                    nc.GetNodeOrNull<Control>(CyclePreview.NodeName)?.QueueFree();
                    nc.GetNodeOrNull<Control>(LiveIntentChips.NodeName)?.QueueFree();
                }
                catch { /* per-creature failures shouldn't block cleanup of the others */ }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{EnemyCycleMod.LogPrefix}combat-end cleanup: {ex.Message}");
        }
    }

    private static System.Collections.Generic.IEnumerable<NCreature> EnumerateCreatures(Node root)
    {
        if (root is NCreature nc) yield return nc;
        foreach (var child in root.GetChildren())
            foreach (var c in EnumerateCreatures(child)) yield return c;
    }
}
