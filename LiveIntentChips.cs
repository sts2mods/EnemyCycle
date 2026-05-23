// Power/card chips for the live current intent (the big intent above
// the enemy's head). The preview rows already render chips for
// upcoming moves; this attaches the same chip strip to the move the
// player is about to see played.
//
// The strip is parented to NCreature (sibling of IntentContainer) and
// bound via the "ec_chip_strip" meta to the last NIntent in
// IntentContainer so the chips bob in time with the intent icon.
using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace EnemyCycle;

public static class LiveIntentChips
{
    public const string NodeName = "EnemyCycleLiveChips";

    // Live intent uses full-size icons (NIntent at scale 1), so chips
    // are larger here than in the preview rows.
    private const float ChipSize = 36f;
    private const float IntraGap = 4f;
    private const float StripLeftGap = 6f;
    // Fallback intent icon size if NIntent.Size isn't yet populated
    // when we run (Container layout schedules to next frame).
    private const float FallbackIntentSize = 64f;

    private const string StripMetaKey = "ec_live_strip";

    public static void Attach(NCreature creatureNode, MonsterModel monster, MoveState? move)
    {
        if (creatureNode == null) return;
        var container = creatureNode.IntentContainer;
        if (container == null) return;

        // Robust clear: GetNodeOrNull(NodeName) only finds the first
        // sibling with that exact name, but Godot auto-renames
        // duplicates ("Name2", "Name3"…) when AddChild is called with
        // a colliding name. Belt-and-suspenders: drop the strip
        // tracked via meta, AND scan siblings for any leftover that
        // share our name prefix.
        if (creatureNode.HasMeta(StripMetaKey))
        {
            var prev = creatureNode.GetMeta(StripMetaKey).As<Control>();
            if (prev != null && GodotObject.IsInstanceValid(prev)) prev.QueueFree();
            creatureNode.RemoveMeta(StripMetaKey);
        }
        foreach (var child in creatureNode.GetChildren())
        {
            if (child is Control c && c.Name.ToString().StartsWith(NodeName, System.StringComparison.Ordinal))
                c.QueueFree();
        }
        if (move == null) return;

        var powers = PowerResolver.ResolveAppliedPowers(monster, move);
        var cards = CardResolver.ResolveAddedCards(monster, move);
        var afflictions = AfflictionResolver.Resolve(monster, move);
        if (powers.Count == 0 && cards.Count == 0 && afflictions.Count == 0) return;

        // Find the last NIntent we'll bind to for bob sync.
        NIntent? lastNi = null;
        var children = container.GetChildren();
        for (int i = children.Count - 1; i >= 0; i--)
        {
            if (children[i] is NIntent ni) { lastNi = ni; break; }
        }

        var strip = new Control
        {
            Name = NodeName,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        creatureNode.AddChild(strip);
        creatureNode.SetMeta(StripMetaKey, strip);

        float cx = 0f;
        foreach (var ap in powers)
        {
            var chip = PowerChip.Create(ap, ChipSize);
            strip.AddChild(chip);
            chip.Position = new Vector2(cx, 0);
            chip.MouseFilter = Control.MouseFilterEnum.Pass;
            cx += ChipSize + IntraGap;
        }
        foreach (var ac in cards)
        {
            var chip = CardChip.Create(ac, ChipSize);
            strip.AddChild(chip);
            chip.Position = new Vector2(cx, 0);
            chip.MouseFilter = Control.MouseFilterEnum.Pass;
            cx += ChipSize + IntraGap;
        }
        foreach (var aa in afflictions)
        {
            var chip = AfflictionChip.Create(aa, ChipSize);
            strip.AddChild(chip);
            chip.Position = new Vector2(cx, 0);
            chip.MouseFilter = Control.MouseFilterEnum.Pass;
            cx += ChipSize + IntraGap;
        }

        strip.ZIndex = 5;
        PositionStrip(strip, lastNi, container);

        // Container layout (HBox alignment of the actual NIntent
        // children) doesn't finish until after this postfix returns,
        // so the first PositionStrip call uses stale niPos/niSize.
        // Re-run on the next idle frame to settle into the correct
        // location next to the last visible intent.
        var capturedStrip = strip;
        var capturedLastNi = lastNi;
        var capturedContainer = container;
        Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(capturedStrip) &&
                GodotObject.IsInstanceValid(capturedContainer))
                PositionStrip(capturedStrip, capturedLastNi, capturedContainer);
        }).CallDeferred();
    }

    private static void PositionStrip(Control strip, NIntent? lastNi, Control container)
    {
        var contPos = container.Position;
        Vector2 basePos;

        if (lastNi != null && GodotObject.IsInstanceValid(lastNi))
        {
            var niPos = lastNi.Position;
            var niSize = lastNi.Size;
            if (niSize.X <= 0f) niSize = new Vector2(FallbackIntentSize, FallbackIntentSize);
            // NIntent's 64x64 rect has empty padding around the
            // visible icon. Pulling the chip left by half its width
            // makes it sit next to the icon's actual visible edge
            // rather than the rect's edge (matches preview rows).
            basePos = new Vector2(
                contPos.X + niPos.X + niSize.X + StripLeftGap - ChipSize * 0.5f,
                contPos.Y + niPos.Y + niSize.Y * 0.5f - ChipSize * 0.5f);
        }
        else
        {
            var contSize = container.Size;
            basePos = new Vector2(
                contPos.X + contSize.X + StripLeftGap,
                contPos.Y + contSize.Y * 0.5f - ChipSize * 0.5f);
        }

        strip.Position = basePos;
        lastNi?.SetMeta("ec_chip_strip", strip);
        lastNi?.SetMeta("ec_chip_strip_base", basePos);
    }
}
