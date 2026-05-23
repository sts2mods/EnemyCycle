// Preview the monster's next two moves stacked VERTICALLY above the
// current intent. The further-out move sits at the top, the next move
// sits just above the current intent. Each row is its own Control with
// a continuous gentle drift-down-then-snap-back animation, so the
// stack visually flows toward the enemy.
//
// One large click hitbox (NCreature-parented Control) wraps the entire
// vertical stack plus the live current intent area, so clicking
// anywhere in that rectangle opens the cycle modal.
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace EnemyCycle;

public static class CyclePreview
{
    public const string NodeName = "EnemyCyclePreview";
    public const string RowsHostName = "EC_RowsHost";

    private const float IntentScale = 0.55f;
    private const float IntentSize = 64f * IntentScale; // 35.2
    private const float ChipSize = 24f;
    private const float IntraGap = 2f;       // between intents/chips in same move
    private const float ChipLeftGap = 6f;    // gap between last intent and chip strip
    private const float RowVerticalGap = 16f; // between stacked move rows
    // Bottom of the vertical stack relative to IntentContainer center
    // (negative = above). Leaves a little headroom for the live current
    // intent below; pulled down so tall elites don't push the stack
    // past the top of the screen.
    private const float StackBottomOffset = -20f;

    // Hit box wraps the preview stack only — must NOT extend over the
    // live intent area or it would swallow the game's hover events
    // and break tooltips. Clicks on the live intent open the modal
    // via NIntent_GuiInput in NIntentPatches.cs.
    private const float HitPadX = 24f;
    private const float HitPadAbove = 14f;
    private const float HitPadBelow = 6f;

    // Transition: when a turn completes and the queue rotates (the old
    // "next move" becomes the current intent), every visible row slides
    // down from above into its new slot, fading in from translucent.
    private const float SlideDuration = 0.35f;

    public static void Attach(NCreature creatureNode, MonsterModel monster,
                              List<MoveState> peeked, IEnumerable<Creature> targets)
    {
        if (creatureNode == null) return;
        // Never mode: no preview rows, no hitbox above the enemy.
        // Strip any existing anchor and bail; the modal stays
        // reachable through the live-intent click in IntentPatch.
        if (EnemyCycleMod.Mode == EnemyCycleMod.PreviewMode.Never)
        {
            creatureNode.GetNodeOrNull<Control>(NodeName)?.QueueFree();
            return;
        }
        var container = creatureNode.IntentContainer;
        if (container == null) return;
        var owner = creatureNode.Entity;
        if (owner == null) return;

        var anchor = creatureNode.GetNodeOrNull<Control>(NodeName);
        if (anchor == null)
        {
            // Clean up any stale child in the old (HBox) parent.
            var stale = container.GetNodeOrNull<Control>(NodeName);
            stale?.QueueFree();
            anchor = new Control
            {
                Name = NodeName,
                MouseFilter = Control.MouseFilterEnum.Stop,
            };
            creatureNode.AddChild(anchor);
        }

        // Detect rotation BEFORE rebuilding so we can animate the
        // transition. A rotation = the last build's "after-next" move
        // is the new build's "next" move (queue advanced by one).
        string[] newIds = (peeked ?? new List<MoveState>())
            .Where(m => m != null).Select(m => m.StateId ?? "").ToArray();
        string[] oldIds = anchor.HasMeta("ec_prev_ids")
            ? anchor.GetMeta("ec_prev_ids").AsStringArray()
            : Array.Empty<string>();
        bool isRotation = oldIds.Length >= 2 && newIds.Length >= 1 &&
                          !string.IsNullOrEmpty(newIds[0]) &&
                          oldIds[1] == newIds[0];
        anchor.SetMeta("ec_prev_ids", newIds);

        foreach (Node c in anchor.GetChildren()) c.QueueFree();

        // Rows live in a sub-host so OnHover mode can hide them as a
        // group while the anchor (and its click hitbox) remain. Sits
        // flush over the anchor so row positions match the old layout
        // exactly.
        var rowsHost = new Control
        {
            Name = RowsHostName,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        rowsHost.AnchorLeft = 0; rowsHost.AnchorTop = 0;
        rowsHost.AnchorRight = 1; rowsHost.AnchorBottom = 1;
        anchor.AddChild(rowsHost);

        // Pre-measure deterministic rows (may be empty for monsters
        // whose very next move is RNG-decided — we still keep an
        // invisible click hitbox over the current intent so the modal
        // remains reachable).
        var rowSpecs = new List<(MoveState mv, float width)>();
        for (int i = 0; peeked != null && i < peeked.Count; i++)
        {
            var mv = peeked[i];
            if (mv?.Intents == null || mv.Intents.Count == 0) continue;
            float w = RowWidth(monster, mv);
            if (w > 0) rowSpecs.Add((mv, w));
        }

        float rowHeight = Math.Max(IntentSize, ChipSize);
        float maxRowWidth = rowSpecs.Count > 0 ? rowSpecs.Max(r => r.width) : IntentSize;
        float stackHeight = rowSpecs.Count * rowHeight
                            + Math.Max(0, rowSpecs.Count - 1) * RowVerticalGap;

        float boxW = maxRowWidth + HitPadX * 2;

        // Position in NCreature local coords. Centered horizontally on
        // the IntentContainer; rows top sits StackBottomOffset above
        // the IntentContainer's center, but the hitbox extends DOWN
        // through the IntentContainer so the click/hover region is
        // continuous from the row stack through the live intent (no
        // dead gap between them).
        var contPos = container.Position;
        var contSize = container.Size;
        float anchorX = contPos.X + contSize.X * 0.5f - boxW * 0.5f;
        float stackTopLocalY = HitPadAbove; // top of stack inside anchor
        float anchorY = contPos.Y + StackBottomOffset - stackTopLocalY - stackHeight;
        float anchorBottomScreen = contPos.Y + contSize.Y + HitPadBelow;
        float boxH = anchorBottomScreen - anchorY;
        anchor.Size = new Vector2(boxW, boxH);
        anchor.Position = new Vector2(anchorX, anchorY);
        anchor.Visible = true;
        // Anchor drops to base z so the live NIntent (deeper in the
        // tree under IntentContainer) wins hit detection over its
        // own area — the game's intent tooltip keeps working. Rows
        // are bumped a few z above the enemy sprite, but kept
        // RELATIVE so the pause / settings overlays (which draw on
        // a higher CanvasLayer or higher absolute z) still darken
        // them along with the rest of the screen.
        anchor.ZIndex = 0;
        rowsHost.ZIndex = 5;
        rowsHost.ZAsRelative = true;

        float timeBase = (float)creatureNode.GetHashCode() * 0.01f;
        int globalIndex = 0;

        // Build rows bottom-up so peeked[0] is at the bottom.
        for (int rowIdx = 0; rowIdx < rowSpecs.Count; rowIdx++)
        {
            var (mv, rowWidth) = rowSpecs[rowIdx];
            // 0 = bottom row (next move), 1 = above it, ...
            int displayPos = rowSpecs.Count - 1 - rowIdx;
            float rowTopY = HitPadAbove + displayPos * (rowHeight + RowVerticalGap);
            // Centered horizontally inside the hit box.
            float rowLeftX = HitPadX + (maxRowWidth - rowWidth) * 0.5f;

            var rowControl = new Control
            {
                Name = "EC_Row_" + rowIdx,
                Position = new Vector2(rowLeftX, rowTopY),
                Size = new Vector2(rowWidth, rowHeight),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            rowsHost.AddChild(rowControl);

            float x = 0f;
            NIntent? lastNi = null;
            foreach (var intent in mv.Intents)
            {
                if (intent == null) continue;
                NIntent ni;
                try { ni = NIntent.Create(timeBase + globalIndex * 0.3f); }
                catch (Exception ex)
                {
                    GD.PrintErr($"{EnemyCycleMod.LogPrefix}NIntent.Create: {ex.Message}");
                    continue;
                }
                rowControl.AddChild(ni);
                try { ni.UpdateIntent(intent, targets, owner); }
                catch (Exception ex)
                {
                    GD.PrintErr($"{EnemyCycleMod.LogPrefix}UpdateIntent: {ex.Message}");
                }
                ni.Scale = new Vector2(IntentScale, IntentScale);
                ni.Modulate = new Color(1f, 1f, 1f, 0.95f);
                ni.Position = new Vector2(x, (rowHeight - IntentSize) * 0.5f);
                ni.MouseFilter = Control.MouseFilterEnum.Pass;
                lastNi = ni;
                x += IntentSize;
                if (intent != mv.Intents[mv.Intents.Count - 1]) x += IntraGap;
                globalIndex++;
            }

            // Group power/card chips into a "strip" Control so they
            // can be bobbed in sync with the row's last NIntent (see
            // NIntent_Process postfix in NIntentPatches.cs). Without
            // this, the intent icon sways but the chips next to it
            // sit static.
            var powers = PowerResolver.ResolveAppliedPowers(monster, mv);
            var cards = CardResolver.ResolveAddedCards(monster, mv);
            var afflictions = AfflictionResolver.Resolve(monster, mv);
            if (powers.Count > 0 || cards.Count > 0 || afflictions.Count > 0)
            {
                var chipStrip = new Control
                {
                    Name = "EC_ChipStrip",
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
                rowControl.AddChild(chipStrip);
                // Icon inside NIntent isn't centered in its 64x64
                // rect — it sits in the lower portion (above where
                // the value label would render). Shift the chip down
                // by ~ChipVerticalNudge so it visually lines up with
                // the icon rather than floating above it.
                const float ChipVerticalNudge = 15f;
                var stripBase = new Vector2(
                    x + ChipLeftGap,
                    (rowHeight - ChipSize) * 0.5f + ChipVerticalNudge);
                chipStrip.Position = stripBase;

                float cx = 0f;
                foreach (var ap in powers)
                {
                    var chip = PowerChip.Create(ap, ChipSize);
                    chipStrip.AddChild(chip);
                    chip.Position = new Vector2(cx, 0);
                    chip.MouseFilter = Control.MouseFilterEnum.Pass;
                    cx += ChipSize + IntraGap;
                }
                foreach (var ac in cards)
                {
                    var chip = CardChip.Create(ac, ChipSize);
                    chipStrip.AddChild(chip);
                    chip.Position = new Vector2(cx, 0);
                    chip.MouseFilter = Control.MouseFilterEnum.Pass;
                    cx += ChipSize + IntraGap;
                }
                foreach (var aa in afflictions)
                {
                    var chip = AfflictionChip.Create(aa, ChipSize);
                    chipStrip.AddChild(chip);
                    chip.Position = new Vector2(cx, 0);
                    chip.MouseFilter = Control.MouseFilterEnum.Pass;
                    cx += ChipSize + IntraGap;
                }

                if (lastNi != null)
                {
                    lastNi.SetMeta("ec_chip_strip", chipStrip);
                    lastNi.SetMeta("ec_chip_strip_base", stripBase);
                }
            }

            if (isRotation)
                StartSlideInAnimation(rowControl, rowTopY, rowHeight + RowVerticalGap);
        }

        if (!anchor.HasMeta("ec_wired"))
        {
            anchor.SetMeta("ec_wired", true);
            var capturedMonster = monster;
            var capturedCreatureNode = creatureNode;
            anchor.Connect(Control.SignalName.GuiInput,
                Callable.From<InputEvent>(ev => OnRowInput(ev, capturedMonster, capturedCreatureNode)));
        }

        // OnHover mode: hidden by default, shown when the cursor is
        // over the anchor OR the live intent (matching the modal's
        // click hitbox). Always mode: rows stay visible. Hover state
        // is tracked as two metas on the NCreature so anchor entries
        // and NIntent entries OR together cleanly without "I left A
        // but I'm now in B" causing a flicker.
        rowsHost.Visible = EnemyCycleMod.Mode != EnemyCycleMod.PreviewMode.OnHover;
        if (!anchor.HasMeta("ec_hover_wired"))
        {
            anchor.SetMeta("ec_hover_wired", true);
            var capturedAnchor = anchor;
            var capturedCreature = creatureNode;
            anchor.MouseEntered += () => SetHoverAnchor(capturedCreature, capturedAnchor, true);
            anchor.MouseExited  += () => SetHoverAnchor(capturedCreature, capturedAnchor, false);
        }
        RefreshHoverVisibility(creatureNode);
    }

    public const string HoverAnchorMeta = "ec_hover_over_anchor";
    public const string HoverIntentMeta = "ec_hover_over_intent";

    private static void SetHoverAnchor(NCreature creatureNode, Control anchor, bool over)
    {
        try
        {
            if (!GodotObject.IsInstanceValid(creatureNode)) return;
            creatureNode.SetMeta(HoverAnchorMeta, over);
            RefreshHoverVisibility(creatureNode);
        }
        catch { /* hover is cosmetic — eat errors */ }
    }

    // Public so the NIntent hover patch can call into the same path.
    public static void RefreshHoverVisibility(NCreature creatureNode)
    {
        try
        {
            if (!GodotObject.IsInstanceValid(creatureNode)) return;
            var anchor = creatureNode.GetNodeOrNull<Control>(NodeName);
            if (anchor == null) return;
            var host = anchor.GetNodeOrNull<Control>(RowsHostName);
            if (host == null) return;
            bool shouldShow = EnemyCycleMod.Mode != EnemyCycleMod.PreviewMode.OnHover
                              || (creatureNode.HasMeta(HoverAnchorMeta) && creatureNode.GetMeta(HoverAnchorMeta).AsBool())
                              || (creatureNode.HasMeta(HoverIntentMeta) && creatureNode.GetMeta(HoverIntentMeta).AsBool());
            host.Visible = shouldShow;
        }
        catch { /* hover is cosmetic — eat errors */ }
    }

    private static float RowWidth(MonsterModel monster, MoveState mv)
    {
        int intentCount = mv.Intents?.Count ?? 0;
        if (intentCount == 0) return 0;
        float w = intentCount * IntentSize + (intentCount - 1) * IntraGap;
        var powers = PowerResolver.ResolveAppliedPowers(monster, mv);
        var cards = CardResolver.ResolveAddedCards(monster, mv);
        var afflictions = AfflictionResolver.Resolve(monster, mv);
        int chipCount = powers.Count + cards.Count + afflictions.Count;
        if (chipCount > 0)
        {
            // First chip gets ChipLeftGap from the last intent; the
            // rest get IntraGap between each other.
            w += ChipLeftGap + ChipSize + (chipCount - 1) * (IntraGap + ChipSize);
        }
        return w;
    }

    private static void StartSlideInAnimation(Control rowControl, float baseY, float slideAmount)
    {
        // Start one row-height above the final position, translucent;
        // tween down + fade to suggest the queue advancing.
        //
        // Earlier version connected to the Ready signal, but rowControl
        // was added to an already-in-tree parent — so _Ready had
        // already fired by the time we Connect()'d, the callback never
        // ran, and rows were left at modulate.a=0 (invisible). Run the
        // tween directly: rowControl IS in the tree at this point.
        rowControl.Position = new Vector2(rowControl.Position.X, baseY - slideAmount);
        rowControl.Modulate = new Color(1f, 1f, 1f, 0.0f);
        try
        {
            var tween = rowControl.CreateTween();
            tween.SetParallel();
            tween.TweenProperty(rowControl, "position:y", baseY, SlideDuration)
                 .SetTrans(Tween.TransitionType.Cubic)
                 .SetEase(Tween.EaseType.Out);
            tween.TweenProperty(rowControl, "modulate:a", 1f, SlideDuration)
                 .SetTrans(Tween.TransitionType.Sine)
                 .SetEase(Tween.EaseType.Out);
        }
        catch (Exception ex)
        {
            // Failsafe — if the tween can't start for any reason, snap
            // the row to its final state so it isn't left invisible.
            try
            {
                rowControl.Position = new Vector2(rowControl.Position.X, baseY);
                rowControl.Modulate = new Color(1f, 1f, 1f, 1f);
            }
            catch { /* ignore */ }
            GD.PrintErr($"{EnemyCycleMod.LogPrefix}slide tween: {ex.Message}");
        }
    }

    private static void OnRowInput(InputEvent ev, MonsterModel monster, NCreature creatureNode)
    {
        if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            // Don't hijack clicks while the player is mid-card-play —
            // either targeting an enemy or holding a non-targeting
            // card. Let the click fall through to the game's
            // card / target handlers.
            if (ModalGuard.SuppressClicksForCardPlay()) return;
            try { CycleModal.Show(monster, creatureNode); }
            catch (Exception ex)
            {
                GD.PrintErr($"{EnemyCycleMod.LogPrefix}CycleModal.Show: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
