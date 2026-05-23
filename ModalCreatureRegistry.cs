// The cloned monster shown in the modal isn't registered in
// NCombatRoom (the live combat monster is) and we don't want to fake
// being NBestiary either. But the game's animation/Cmd plumbing all
// goes through Creature.GetCreatureNode() to find the NCreature to
// animate. So we maintain our own (Creature → NCreature) map and
// Harmony-patch GetCreatureNode to consult it first.
//
// Backed by ConditionalWeakTable (the same primitive BaseLib's
// SpireField wraps) so entries vanish automatically once the
// Creature key is unreachable — no dangling references to dead
// monsters between fights. We still keep the explicit Unregister +
// TreeExiting hook so the live combat path immediately stops
// resolving to a freed NCreature.
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace EnemyCycle;

public static class ModalCreatureRegistry
{
    private static readonly ConditionalWeakTable<Creature, NCreature> _entries = new();

    public static void Register(Creature creature, NCreature nc)
    {
        if (creature == null || nc == null) return;
        _entries.Remove(creature);
        _entries.Add(creature, nc);
        // Self-clean when the node leaves the tree (modal closed). The
        // weak table would eventually drop dead entries on its own,
        // but the live combat path can't wait for the GC sweep.
        nc.Connect(Node.SignalName.TreeExiting,
            Callable.From(() => Unregister(creature)));
    }

    public static void Unregister(Creature creature)
    {
        if (creature == null) return;
        _entries.Remove(creature);
    }

    public static NCreature? Lookup(Creature creature)
    {
        if (creature == null) return null;
        if (!_entries.TryGetValue(creature, out var nc)) return null;
        if (!GodotObject.IsInstanceValid(nc)) { _entries.Remove(creature); return null; }
        return nc;
    }

    public static bool IsModalCreatureNode(NCreature nc)
    {
        if (nc == null || nc.Entity == null) return false;
        return Lookup(nc.Entity) == nc;
    }
}

[HarmonyPatch(typeof(Creature), nameof(Creature.GetCreatureNode))]
public static class Creature_GetCreatureNode_ModalLookup
{
    static bool Prefix(Creature __instance, ref NCreature? __result)
    {
        var modal = ModalCreatureRegistry.Lookup(__instance);
        if (modal == null) return true; // fall through to original
        __result = modal;
        return false;
    }
}
