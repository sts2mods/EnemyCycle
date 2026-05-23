// Two Harmony patches on NIntent for our preview/modal NIntents.
//
//  - Postfix on _Process: if Meta "ec_nobob" is set (used by the modal),
//    zero out _intentHolder.Position. Frame animation still runs.
//  - Prefix on OnHovered: if Meta "ec_hover_anchor" is set (modal only),
//    redirect the hover tip to that anchor — otherwise the default
//    path tries to attach the tip to the creature behind the modal dim.
//    Calls NHoverTipSet.CreateAndShow with a proper alignment so the
//    tip doesn't land in the top-left corner.
using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.HoverTips;

namespace EnemyCycle;

[HarmonyPatch(typeof(NIntent), "_Process")]
public static class NIntent_Process_Postfix
{
    private static readonly FieldInfo? IntentHolderField =
        typeof(NIntent).GetField("_intentHolder",
            BindingFlags.Instance | BindingFlags.NonPublic);

    static void Postfix(NIntent __instance)
    {
        // Modal NIntents — freeze the bob entirely.
        if (__instance.HasMeta("ec_nobob"))
        {
            if (IntentHolderField?.GetValue(__instance) is Control h0)
                h0.Position = Vector2.Zero;
            return;
        }

        // Preview rows + live current intent — sync an external chip
        // strip's Y to the intent's bob so power/card chips visually
        // follow the icon they belong to. Also copy the intent's
        // modulate alpha so when the game fades the intent out during
        // a performed attack, the chips fade alongside it instead of
        // popping out of existence.
        if (!__instance.HasMeta("ec_chip_strip")) return;
        try
        {
            var strip = __instance.GetMeta("ec_chip_strip").As<Control>();
            if (strip == null || !GodotObject.IsInstanceValid(strip)) return;
            if (IntentHolderField?.GetValue(__instance) is not Control h) return;
            var basePos = __instance.HasMeta("ec_chip_strip_base")
                ? __instance.GetMeta("ec_chip_strip_base").AsVector2()
                : strip.Position;
            // _intentHolder.Position is in NIntent-local space; the
            // visual offset is scaled by NIntent.Scale.
            float scaledY = h.Position.Y * __instance.Scale.Y;
            strip.Position = basePos + new Vector2(0, scaledY);

            // Combined alpha. The game fades intents by tweening
            // IntentContainer.Modulate.A (NIntent's parent) during
            // attack animations — without folding that in, the chip
            // would just blink off when the intent is destroyed
            // rather than fading out alongside it.
            float alpha = __instance.Modulate.A * h.Modulate.A;
            if (__instance.GetParent() is Control parentCtl)
                alpha *= parentCtl.Modulate.A;
            var m = strip.Modulate;
            if (Mathf.Abs(m.A - alpha) > 0.001f)
                strip.Modulate = new Color(m.R, m.G, m.B, alpha);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{EnemyCycleMod.LogPrefix}chip strip sync: {ex.Message}");
        }
    }
}

[HarmonyPatch(typeof(NIntent), "OnHovered")]
public static class NIntent_OnHovered_AnchorOverride
{
    private static readonly FieldInfo? IntentField =
        typeof(NIntent).GetField("_intent",
            BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? TargetsField =
        typeof(NIntent).GetField("_targets",
            BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? OwnerField =
        typeof(NIntent).GetField("_owner",
            BindingFlags.Instance | BindingFlags.NonPublic);

    static bool Prefix(NIntent __instance)
    {
        if (!EnemyCycleMod.Enabled) return true;
        if (!__instance.HasMeta("ec_hover_anchor")) return true;

        try
        {
            var intent = IntentField?.GetValue(__instance) as AbstractIntent;
            if (intent == null || !intent.HasIntentTip) return false;

            var owner = OwnerField?.GetValue(__instance) as Creature;
            var targets = TargetsField?.GetValue(__instance) as IEnumerable<Creature>;
            if (owner == null) return false;

            var anchor = __instance.GetMeta("ec_hover_anchor").As<Control>();
            if (anchor == null) return true;

            HoverTip tip;
            try { tip = intent.GetHoverTip(targets ?? Array.Empty<Creature>(), owner); }
            catch { return true; }

            var align = HoverTip.GetHoverTipAlignment(anchor, 0.5f);
            NHoverTipSet.Remove(anchor);
            NHoverTipSet.CreateAndShow(anchor, new List<IHoverTip> { tip }, align);
            return false;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{EnemyCycleMod.LogPrefix}OnHovered patch: {ex.Message}");
            return true;
        }
    }
}

[HarmonyPatch(typeof(NIntent), "OnUnhovered")]
public static class NIntent_OnUnhovered_HideAnchored
{
    static void Prefix(NIntent __instance)
    {
        if (!__instance.HasMeta("ec_hover_anchor")) return;
        try
        {
            var anchor = __instance.GetMeta("ec_hover_anchor").As<Control>();
            if (anchor != null) NHoverTipSet.Remove(anchor);
        }
        catch { /* ignore */ }
    }
}
