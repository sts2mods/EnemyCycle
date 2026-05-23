// Full-screen modal showing a monster's complete move structure
// (intro + cycle). Uses the in-game submenu_panel texture (Modulate-
// darkened so text reads), Kreon fonts + StsColors palette, and an
// animated monster preview on the left (bestiary-style spawn).
//
// Modal rows render intent icons via TextureRect + value Label instead
// of the live NIntent. The NIntent scene's offset/scale interaction
// made alignment fragile inside a Container — flat TextureRects align
// cleanly in HBox and still give us the right damage values via
// AbstractIntent.GetIntentLabel.
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Random;

namespace EnemyCycle;

public static class CycleModal
{
    public const string OverlayName = "EnemyCycleModalOverlay";

    // Fraction of viewport height the modal is allowed to occupy
    // before we wrap the move list in a scroll container.
    private const float MaxViewportFraction = 0.80f;

    private const float IntentIconSize = 56f;
    private const float IntentIconGap = 4f;
    private const float ChipSize = 44f;
    private const float PreviewColumnWidth = 280f;
    private const float PreviewColumnHeight = 540f;
    private const float MonsterScale = 0.85f;

    private const string PanelTexturePath = "res://images/packed/common_ui/submenu_panel.png";
    private const string KreonBoldPath = "res://themes/kreon_bold_glyph_space_one.tres";
    private const string KreonRegularPath = "res://themes/kreon_regular_glyph_space_one.tres";

    public static void Show(MonsterModel monster, NCreature creatureNode)
    {
        var combat = NCombatRoom.Instance;
        if (combat == null) return;
        combat.GetNodeOrNull<Control>(OverlayName)?.QueueFree();

        var info = CycleStructure.Detect(monster);
        if (info.Intro.Count == 0 && info.Cycle.Count == 0 && info.UniqueMoves.Count == 0)
        {
            GD.PrintErr($"{EnemyCycleMod.LogPrefix}no states detected for {monster?.GetType().Name}");
            return;
        }

        var overlay = BuildOverlay(monster, creatureNode, info);
        overlay.Name = OverlayName;
        overlay.ZIndex = 9999;
        combat.AddChild(overlay);
    }

    private static Control BuildOverlay(MonsterModel monster, NCreature creatureNode,
                                        CycleInfo info)
    {
        var dim = new CycleModalRoot
        {
            Name = OverlayName,
            AnchorRight = 1, AnchorBottom = 1,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        // Transparent backdrop. The modal panel itself stands out on
        // its own; an opaque dim stacked with the pause-menu / settings
        // dim creates an ugly "double dark" when the user pauses while
        // the modal is open. Click-blocking still works because the
        // Panel has MouseFilter=Stop.
        var dimStyle = new StyleBoxFlat { BgColor = Colors.Transparent };
        dim.AddThemeStyleboxOverride("panel", dimStyle);
        dim.Connect(Control.SignalName.GuiInput,
            Callable.From<InputEvent>(ev => OnDimInput(ev, dim)));

        // Panel auto-sizes to its content. Anchors collapsed to the
        // center point + Grow Both means size = max(min, content), and
        // the offsets default to 0 so the panel stays centered without
        // hardcoding a fixed height.
        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(1000, 500),
            AnchorLeft = 0.5f, AnchorTop = 0.5f, AnchorRight = 0.5f, AnchorBottom = 0.5f,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Both,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        ApplyPanelTexture(panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 12);
        panel.AddChild(vbox);

        vbox.AddChild(BuildHeader(monster, dim));
        vbox.AddChild(BuildSeparator());

        var body = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        body.AddThemeConstantOverride("separation", 20);

        var (previewBox, previewClone) = BuildMonsterPreview(monster);
        if (previewBox != null) body.AddChild(previewBox);

        var list = BuildMoveList(monster, creatureNode, info, previewClone);
        body.AddChild(list);

        vbox.AddChild(body);

        dim.AddChild(panel);

        // Cap the modal height once Godot has finished its first
        // layout pass. If the panel ends up taller than the allowed
        // viewport fraction, reparent the move list into a
        // ScrollContainer with a fixed height so overflow scrolls
        // instead of pushing the modal off-screen.
        var capturedPanel = panel;
        var capturedBody = body;
        var capturedList = list;
        Callable.From(() => CapModalHeight(capturedPanel, capturedBody, capturedList)).CallDeferred();

        return dim;
    }

    // Anchored auto-sizing was inflating the modal well past its
    // content. Instead we explicitly measure the panel's combined
    // minimum size, optionally wrap the move list in a scroll if we'd
    // exceed the viewport cap, then set the panel's position + size
    // directly so Godot's layout can't override.
    private static void CapModalHeight(PanelContainer panel, HBoxContainer body, Control list)
    {
        if (!GodotObject.IsInstanceValid(panel) ||
            !GodotObject.IsInstanceValid(body) ||
            !GodotObject.IsInstanceValid(list)) return;

        var viewport = panel.GetViewportRect().Size;
        float maxH = viewport.Y * MaxViewportFraction;

        var minSize = panel.GetCombinedMinimumSize();
        float targetW = Math.Max(1000f, minSize.X);
        float targetH = Math.Max(500f, minSize.Y);

        if (targetH > maxH)
        {
            float overhead = minSize.Y - list.GetCombinedMinimumSize().Y;
            float listH = Math.Max(240f, maxH - overhead);
            WrapListInScroll(body, list, listH);
            // Exact size — listH + overhead — so the panel hugs the
            // scrollable area instead of leaving parchment showing
            // below it.
            targetH = listH + overhead;
        }

        // Pin the panel manually — no anchor expansion, no GrowDirection.
        panel.AnchorLeft = 0; panel.AnchorTop = 0;
        panel.AnchorRight = 0; panel.AnchorBottom = 0;
        panel.GrowHorizontal = Control.GrowDirection.End;
        panel.GrowVertical = Control.GrowDirection.End;
        panel.Position = new Vector2((viewport.X - targetW) / 2f,
                                     (viewport.Y - targetH) / 2f);
        panel.Size = new Vector2(targetW, targetH);
    }

    // Wrap the move list in the game's NScrollableContainer + the
    // standard NScrollbar so the modal matches the rest of the UI
    // (card library, run history) instead of using Godot's default
    // scrollbar. NScrollableContainer expects children named "Content"
    // (the scrolled Control) and "Scrollbar" — those must exist before
    // the container enters the tree, since its _Ready does a hard
    // GetNode<NScrollbar>("Scrollbar").
    // Bar gets its own visual column inside the scroll. The list is
    // confined to a "Mask" child (which clips it) so the bar's column
    // is genuinely separate from the list area.
    private const float BarColumnWidth = 70f;
    private const float ScrollbarInsetY = 40f;

    private static void WrapListInScroll(HBoxContainer body, Control list, float listH)
    {
        int idx = -1;
        var children = body.GetChildren();
        for (int i = 0; i < children.Count; i++)
        {
            if (children[i] == list) { idx = i; break; }
        }
        if (idx < 0) return;

        body.RemoveChild(list);

        var scroll = new NScrollableContainer
        {
            Name = "EC_Scroll",
            CustomMinimumSize = new Vector2(0, listH),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            // ClipContents stays on the Mask child instead so the
            // bar (which lives outside the Mask) can render freely.
            ClipContents = false,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };

        // "Mask" Control occupies the LEFT side of the scroll and
        // does the clipping for the list. NScrollableContainer's
        // _Ready accepts "Mask/Content" as the content path, so the
        // list lives inside the Mask. The right-side BarColumnWidth
        // of scroll is left empty for the bar's column.
        var mask = new Control
        {
            Name = "Mask",
            AnchorLeft = 0,  AnchorRight = 1,
            AnchorTop = 0,   AnchorBottom = 1,
            OffsetLeft = 0,  OffsetRight = -BarColumnWidth,
            OffsetTop = 0,   OffsetBottom = 0,
            ClipContents = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        scroll.AddChild(mask);

        // List is "Content" — full width of the Mask. It can grow
        // taller than the Mask (that's what scrolling moves).
        list.Name = "Content";
        list.AnchorLeft = 0; list.AnchorRight = 1;
        list.AnchorTop = 0;  list.AnchorBottom = 0;
        list.OffsetLeft = 0; list.OffsetRight = 0; list.OffsetTop = 0;
        list.GrowHorizontal = Control.GrowDirection.End;
        list.GrowVertical = Control.GrowDirection.End;
        mask.AddChild(list);

        try
        {
            var sbarScene = ResourceLoader.Load<PackedScene>("res://scenes/ui/scrollbar.tscn");
            if (sbarScene != null)
            {
                var bar = sbarScene.Instantiate<NScrollbar>();
                bar.Name = "Scrollbar";
                // Bar sits in the right BarColumnWidth column,
                // outside Mask's clip area but still a direct child
                // of scroll (NScrollableContainer requires this).
                bar.AnchorLeft = 1; bar.AnchorRight = 1;
                bar.AnchorTop = 0;  bar.AnchorBottom = 1;
                bar.OffsetLeft = -BarColumnWidth + 15;
                bar.OffsetRight = -15;
                bar.OffsetTop = ScrollbarInsetY;
                bar.OffsetBottom = -ScrollbarInsetY;
                bar.GrowHorizontal = Control.GrowDirection.Begin;
                scroll.AddChild(bar);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{EnemyCycleMod.LogPrefix}scrollbar instantiate: {ex.Message}");
        }

        body.AddChild(scroll);
        body.MoveChild(scroll, idx);

        // NScrollableContainer hides the scrollbar in _Ready and only
        // makes it visible when ItemRectChanged fires on the content
        // — which doesn't always happen on first layout. Force the
        // visibility recalc one frame later.
        var capturedScroll = scroll;
        Callable.From(() =>
        {
            if (!GodotObject.IsInstanceValid(capturedScroll)) return;
            try { capturedScroll.UpdatePadding(0f, 0f); }
            catch { /* ignore */ }
        }).CallDeferred();
    }

    private static Control BuildMoveList(MonsterModel monster, NCreature creatureNode,
                                          CycleInfo info, MonsterModel? previewClone)
    {
        // No ScrollContainer — we want the move list's full height to
        // propagate up so the modal panel grows to match its content.
        var list = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        list.AddThemeConstantOverride("separation", 14);

        var owner = creatureNode.Entity;
        var targets = owner.CombatState?.Players?.Select(p => p.Creature).ToList()
                      ?? new List<Creature>();
        var liveCurrent = MovePredictor.GetCurrentState(
            MovePredictor.GetStateMachine(monster)!) as MoveState;
        string? liveCurrentId = liveCurrent?.StateId;

        // Never mode collapses the modal to just the pattern section —
        // the user explicitly opted out of seeing per-move rows.
        if (EnemyCycleMod.Mode == EnemyCycleMod.PreviewMode.Never)
        {
            string bbcode = info.IsRandom
                ? MoveDescriptor.Describe(monster, info.UniqueMoves)
                : MoveDescriptor.DescribePattern(monster, info);
            list.AddChild(BuildPatternBlock(bbcode));
            return list;
        }

        if (info.IsRandom)
        {
            list.AddChild(BuildSectionHeader("Moves",
                "Order is random — see pattern below."));
            for (int i = 0; i < info.UniqueMoves.Count; i++)
            {
                var mv = info.UniqueMoves[i];
                bool isCurrent = liveCurrentId != null && mv.StateId == liveCurrentId;
                list.AddChild(BuildMoveRow(monster, mv, isCurrent, owner, targets, previewClone));
            }
            list.AddChild(BuildPatternBlock(MoveDescriptor.Describe(monster, info.UniqueMoves)));
            return list;
        }

        if (info.Intro.Count > 0)
        {
            list.AddChild(BuildSectionHeader("First Move",
                "Only happens once at the start of combat."));
            for (int i = 0; i < info.Intro.Count; i++)
            {
                bool isCurrent = i == info.CurrentIndexInIntro;
                list.AddChild(BuildMoveRow(monster, info.Intro[i], isCurrent, owner, targets, previewClone));
            }
        }
        if (info.Cycle.Count > 0)
        {
            string sub = "Repeats every " + info.Cycle.Count + " turn"
                + (info.Cycle.Count == 1 ? "" : "s")
                + (info.Intro.Count > 0 ? " after the first move." : ".");
            list.AddChild(BuildSectionHeader("Cycle", sub));
            for (int i = 0; i < info.Cycle.Count; i++)
            {
                bool isCurrent = i == info.CurrentIndexInCycle;
                list.AddChild(BuildMoveRow(monster, info.Cycle[i], isCurrent, owner, targets, previewClone));
            }
        }
        list.AddChild(BuildPatternBlock(MoveDescriptor.DescribePattern(monster, info)));
        return list;
    }

    private static Control BuildPatternBlock(string bbcode)
    {
        var vb = new VBoxContainer();
        vb.AddThemeConstantOverride("separation", 6);
        vb.AddChild(BuildSectionHeader("Pattern", string.Empty));

        var rules = new RichTextLabel
        {
            BbcodeEnabled = true,
            Text = string.IsNullOrEmpty(bbcode) ? "(no pattern detected)" : bbcode,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            FitContent = true,
            ScrollActive = false,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            // Hint the wrap width for FitContent's height calc. Must
            // be ≤ the list area's available width (modal_w - margins
            // - preview - separation - bar_column) so the VBox's
            // reported MinSize.X doesn't push the whole list past the
            // Mask's right edge. ~480 fits comfortably at the 1000px
            // modal width and still wraps the rules text in 3-4 lines.
            CustomMinimumSize = new Vector2(480, 0),
        };
        rules.AddThemeFontSizeOverride("normal_font_size", 16);
        rules.AddThemeFontSizeOverride("bold_font_size", 16);
        rules.AddThemeColorOverride("default_color", StsColors.cream);
        rules.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        rules.AddThemeConstantOverride("outline_size", 4);
        rules.AddThemeConstantOverride("line_separation", 4);
        TryApplyFont(rules, KreonRegularPath, "normal_font");
        TryApplyFont(rules, KreonBoldPath, "bold_font");
        vb.AddChild(rules);
        return vb;
    }

    private static (Control? box, MonsterModel? clone) BuildMonsterPreview(MonsterModel monster)
    {
        if (monster == null) return (null, null);
        try
        {
            var canonical = monster.CanonicalInstance ?? monster;
            var clone = (MonsterModel)canonical.ToMutable();
            clone.Rng = Rng.Chaotic;
            clone.SetUpForCombat();

            var entity = new Creature(clone, CombatSide.Enemy, null)
            {
                CombatState = new NullCombatState()
            };
            var nc = NCreature.Create(entity);
            if (nc == null) return (null, null);

            var box = new Control
            {
                CustomMinimumSize = new Vector2(PreviewColumnWidth, PreviewColumnHeight),
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                // Don't clip — large monsters (Vine Shambler, etc.)
                // overflow the preview column. We push them behind the
                // move-row panels with negative ZIndex so the cycle
                // info stays readable on top.
                ClipContents = false,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            box.AddChild(nc);
            // SetupForBestiary touches _stateDisplay and IntentContainer
            // which are only resolved in _Ready. The modal subtree
            // isn't attached to combat yet, so _Ready hasn't fired —
            // call it deferred so we fire after the node is fully in
            // the tree.
            var capturedNc = nc;
            Callable.From(() =>
            {
                if (!GodotObject.IsInstanceValid(capturedNc)) return;
                try { capturedNc.SetupForBestiary(); }
                catch (Exception ex)
                {
                    GD.PrintErr($"{EnemyCycleMod.LogPrefix}deferred SetupForBestiary: {ex.Message}");
                }
            }).CallDeferred();
            nc.Position = new Vector2(PreviewColumnWidth * 0.5f, PreviewColumnHeight * 0.78f);
            nc.Scale = new Vector2(MonsterScale, MonsterScale);
            // Register so Creature.GetCreatureNode() finds this clone
            // when its moves Perform — otherwise animations target
            // nothing ("creature node doesn't exist" errors).
            ModalCreatureRegistry.Register(entity, nc);
            // Tree order already puts the move rows after the preview
            // (they're added later to the body HBox), so they naturally
            // render on top of any monster overflow. Don't set a custom
            // ZIndex — negative values push behind the modal background.
            return (box, clone);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{EnemyCycleMod.LogPrefix}BuildMonsterPreview: {ex.Message}\n{ex.StackTrace}");
            return (null, null);
        }
    }

    // Click a move row in the modal → make the preview monster perform
    // that move's animation (bestiary-style). The preview clone has its
    // own state machine so playing moves on it doesn't affect the live
    // combat monster.
    private static void PlayMoveOnPreview(MonsterModel? clone, string stateId)
    {
        if (clone == null || string.IsNullOrEmpty(stateId)) return;
        try
        {
            var sm = clone.MoveStateMachine;
            if (sm == null) return;
            if (!sm.States.TryGetValue(stateId, out var state)) return;
            if (state is not MoveState mvState) return;
            if (clone.IsPerformingMove) return; // wait for current to finish
            clone.SetMoveImmediate(mvState, forceTransition: true);
            _ = clone.PerformMove();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{EnemyCycleMod.LogPrefix}PlayMoveOnPreview: {ex.Message}");
        }
    }

    private static void ApplyPanelTexture(PanelContainer panel)
    {
        Texture2D? tex = null;
        try { tex = ResourceLoader.Load<Texture2D>(PanelTexturePath); }
        catch (Exception ex)
        {
            GD.PrintErr($"{EnemyCycleMod.LogPrefix}load panel tex: {ex.Message}");
        }

        if (tex != null)
        {
            var sbt = new StyleBoxTexture
            {
                Texture = tex,
                TextureMarginLeft = 48,
                TextureMarginRight = 48,
                TextureMarginTop = 48,
                TextureMarginBottom = 48,
                ContentMarginLeft = 30,
                ContentMarginRight = 30,
                ContentMarginTop = 28,
                ContentMarginBottom = 48,
                ModulateColor = new Color(0.42f, 0.42f, 0.45f, 1f),
            };
            panel.AddThemeStyleboxOverride("panel", sbt);
        }
        else
        {
            var sb = new StyleBoxFlat
            {
                BgColor = new Color(0.08f, 0.08f, 0.10f, 0.97f),
                BorderColor = StsColors.gold,
                BorderWidthLeft = 2, BorderWidthRight = 2,
                BorderWidthTop = 2, BorderWidthBottom = 2,
                CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
                CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
                ContentMarginLeft = 24, ContentMarginRight = 24,
                ContentMarginTop = 20, ContentMarginBottom = 20,
            };
            panel.AddThemeStyleboxOverride("panel", sb);
        }
    }

    private static Control BuildHeader(MonsterModel monster, Control dim)
    {
        // Tall monsters (Bygone Effigy, etc.) overflow their preview
        // column upward and would otherwise paint on top of the title
        // (drawn earlier in tree order). Bump the header's z_index so
        // it always paints in front of body content.
        var hb = new HBoxContainer { ZIndex = 100 };
        hb.AddThemeConstantOverride("separation", 10);

        var titleLbl = new Label
        {
            Text = SafeMonsterTitle(monster) + " — Move Cycle",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
        };
        titleLbl.AddThemeFontSizeOverride("font_size", 28);
        titleLbl.AddThemeColorOverride("font_color", StsColors.gold);
        titleLbl.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.6f));
        titleLbl.AddThemeConstantOverride("shadow_offset_x", 3);
        titleLbl.AddThemeConstantOverride("shadow_offset_y", 2);
        TryApplyFont(titleLbl, KreonBoldPath, "font");
        hb.AddChild(titleLbl);

        var closeBtn = new Button
        {
            Text = "Close",
            CustomMinimumSize = new Vector2(96, 36),
        };
        closeBtn.AddThemeColorOverride("font_color", StsColors.cream);
        closeBtn.AddThemeColorOverride("font_hover_color", StsColors.gold);
        TryApplyFont(closeBtn, KreonBoldPath, "font");
        closeBtn.AddThemeFontSizeOverride("font_size", 18);
        closeBtn.Pressed += () => dim.QueueFree();
        hb.AddChild(closeBtn);
        return hb;
    }

    private static Control BuildSeparator()
    {
        var sep = new HSeparator();
        sep.AddThemeColorOverride("color", StsColors.halfTransparentCream);
        return sep;
    }

    private static Control BuildSectionHeader(string title, string subtitle)
    {
        var vb = new VBoxContainer();
        vb.AddThemeConstantOverride("separation", 2);

        var titleLbl = new Label { Text = title };
        titleLbl.AddThemeFontSizeOverride("font_size", 20);
        titleLbl.AddThemeColorOverride("font_color", StsColors.gold);
        titleLbl.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.6f));
        titleLbl.AddThemeConstantOverride("shadow_offset_x", 2);
        titleLbl.AddThemeConstantOverride("shadow_offset_y", 2);
        TryApplyFont(titleLbl, KreonBoldPath, "font");
        vb.AddChild(titleLbl);

        if (!string.IsNullOrEmpty(subtitle))
        {
            var subLbl = new Label { Text = subtitle };
            subLbl.AddThemeFontSizeOverride("font_size", 13);
            subLbl.AddThemeColorOverride("font_color", StsColors.cream);
            subLbl.Modulate = new Color(1, 1, 1, 0.75f);
            TryApplyFont(subLbl, KreonRegularPath, "font");
            vb.AddChild(subLbl);
        }
        return vb;
    }

    private static Control BuildMoveRow(MonsterModel monster, MoveState mv, bool isCurrent,
                                        Creature owner, List<Creature> targets,
                                        MonsterModel? previewClone)
    {
        var row = new PanelContainer();
        // Muted red tint for the current move — calmer than gold and
        // matches "this enemy is attacking" semantics. The "(current)"
        // label on the right does the labelling.
        var currentBg = new Color(0.28f, 0.10f, 0.10f, 0.7f);
        var currentBorder = new Color(0.78f, 0.30f, 0.30f, 1f);
        var normalBg = new Color(0.05f, 0.05f, 0.07f, 0.6f);
        var normalBorder = new Color(1, 1, 1, 0.18f);

        var normalStyle = MakeRowStyle(
            isCurrent ? currentBg : normalBg,
            isCurrent ? currentBorder : normalBorder,
            isCurrent ? 2 : 1);
        var hoverStyle = MakeRowStyle(
            isCurrent ? Lighten(currentBg, 0.10f) : new Color(0.13f, 0.13f, 0.16f, 0.85f),
            isCurrent ? Lighten(currentBorder, 0.15f) : new Color(1, 1, 1, 0.42f),
            2);
        var pressedStyle = MakeRowStyle(
            isCurrent ? Lighten(currentBg, 0.20f) : new Color(0.20f, 0.20f, 0.24f, 0.95f),
            isCurrent ? Lighten(currentBorder, 0.25f) : StsColors.gold,
            2);

        row.AddThemeStyleboxOverride("panel", normalStyle);

        var hb = new HBoxContainer();
        hb.AddThemeConstantOverride("separation", 14);

        // Intent icons (TextureRect + value label, NOT NIntent).
        var iconsBox = new HBoxContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
        iconsBox.AddThemeConstantOverride("separation", (int)IntentIconGap);
        if (mv.Intents != null)
        {
            foreach (var intent in mv.Intents)
            {
                if (intent == null) continue;
                iconsBox.AddChild(BuildIntentIcon(intent, owner, targets));
            }
        }
        hb.AddChild(iconsBox);

        // Power chips (icon + amount + hover for description).
        var powers = PowerResolver.ResolveAppliedPowers(monster, mv);
        var cards = CardResolver.ResolveAddedCards(monster, mv);
        if (powers.Count > 0 || cards.Count > 0)
        {
            var chipsBox = new HBoxContainer
            {
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            };
            chipsBox.AddThemeConstantOverride("separation", 6);
            foreach (var ap in powers)
                chipsBox.AddChild(PowerChip.Create(ap, ChipSize));
            foreach (var ac in cards)
                chipsBox.AddChild(CardChip.Create(ac, ChipSize));
            hb.AddChild(chipsBox);
        }

        var nameLbl = new Label
        {
            Text = MoveNameHelper.GetDisplayName(monster, mv),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            VerticalAlignment = VerticalAlignment.Center,
        };
        nameLbl.AddThemeFontSizeOverride("font_size", 22);
        nameLbl.AddThemeColorOverride("font_color", StsColors.cream);
        nameLbl.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.6f));
        nameLbl.AddThemeConstantOverride("shadow_offset_x", 2);
        nameLbl.AddThemeConstantOverride("shadow_offset_y", 2);
        TryApplyFont(nameLbl, KreonBoldPath, "font");
        hb.AddChild(nameLbl);

        if (isCurrent)
        {
            var curLbl = new Label
            {
                Text = "(current)",
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                VerticalAlignment = VerticalAlignment.Center,
            };
            curLbl.AddThemeFontSizeOverride("font_size", 16);
            curLbl.AddThemeColorOverride("font_color", new Color(1f, 0.55f, 0.55f));
            curLbl.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.5f));
            curLbl.AddThemeConstantOverride("shadow_offset_x", 2);
            curLbl.AddThemeConstantOverride("shadow_offset_y", 2);
            TryApplyFont(curLbl, KreonRegularPath, "font");
            hb.AddChild(curLbl);
        }

        row.AddChild(hb);

        // Make the row clickable — clicking plays the move's animation
        // on the preview monster (bestiary-style).
        if (previewClone != null)
        {
            row.MouseFilter = Control.MouseFilterEnum.Stop;
            var capturedStateId = mv.StateId ?? "";
            var capturedClone = previewClone;
            var capturedNormal = normalStyle;
            var capturedHover = hoverStyle;
            var capturedPressed = pressedStyle;
            var capturedRow = row;

            capturedRow.MouseEntered += () =>
            {
                if (GodotObject.IsInstanceValid(capturedRow))
                    capturedRow.AddThemeStyleboxOverride("panel", capturedHover);
            };
            capturedRow.MouseExited += () =>
            {
                if (GodotObject.IsInstanceValid(capturedRow))
                    capturedRow.AddThemeStyleboxOverride("panel", capturedNormal);
            };

            row.Connect(Control.SignalName.GuiInput, Callable.From<InputEvent>(ev =>
            {
                if (ev is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
                {
                    if (mb.Pressed)
                    {
                        capturedRow.AddThemeStyleboxOverride("panel", capturedPressed);
                    }
                    else
                    {
                        // Released: restore hover (we're still over it).
                        capturedRow.AddThemeStyleboxOverride("panel", capturedHover);
                        PlayMoveOnPreview(capturedClone, capturedStateId);
                    }
                }
            }));
        }

        // Make intent-icon cells and power chips bubble their clicks up
        // to the row by switching their MouseFilter to Pass. Their own
        // hover handlers still fire (Pass still receives events).
        SetChildrenClickPass(hb);
        return row;
    }

    private static StyleBoxFlat MakeRowStyle(Color bg, Color border, int borderWidth) => new StyleBoxFlat
    {
        BgColor = bg,
        BorderColor = border,
        BorderWidthLeft = borderWidth, BorderWidthRight = borderWidth,
        BorderWidthTop = borderWidth, BorderWidthBottom = borderWidth,
        CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
        CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
        ContentMarginLeft = 14, ContentMarginRight = 14,
        ContentMarginTop = 10, ContentMarginBottom = 10,
    };

    private static Color Lighten(Color c, float amount) => new Color(
        Math.Min(1f, c.R + amount),
        Math.Min(1f, c.G + amount),
        Math.Min(1f, c.B + amount),
        Math.Min(1f, c.A + amount * 0.5f));

    private static void SetChildrenClickPass(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            if (child is Control ctl && ctl.MouseFilter == Control.MouseFilterEnum.Stop)
                ctl.MouseFilter = Control.MouseFilterEnum.Pass;
            SetChildrenClickPass(child);
        }
    }

    private static Control BuildIntentIcon(AbstractIntent intent, Creature owner,
                                           List<Creature> targets)
    {
        // Cell: fixed size, holds icon (top) + value label (bottom),
        // both anchored explicitly so they don't drift.
        var cell = new Control
        {
            CustomMinimumSize = new Vector2(IntentIconSize, IntentIconSize),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };

        Texture2D? tex = null;
        try { tex = intent.GetTexture(targets, owner); }
        catch { /* ignore */ }
        if (tex != null)
        {
            var rect = new TextureRect
            {
                Texture = tex,
                AnchorLeft = 0, AnchorTop = 0, AnchorRight = 1, AnchorBottom = 1,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            cell.AddChild(rect);
        }

        // Value label (damage / multi-attack / status count).
        string valueText = "";
        try
        {
            if (intent is AttackIntent || intent is StatusIntent)
                valueText = intent.GetIntentLabel(targets, owner).GetFormattedText() ?? "";
        }
        catch { /* ignore */ }

        if (!string.IsNullOrEmpty(valueText))
        {
            // Value strings include BBCode like "6[font_size=18]x2[/font_size]"
            // (the smaller multi-hit marker) — RichTextLabel renders it
            // properly; a plain Label shows the raw tags.
            var lbl = new RichTextLabel
            {
                BbcodeEnabled = true,
                Text = "[center]" + valueText + "[/center]",
                AnchorLeft = 0, AnchorTop = 0.5f, AnchorRight = 1, AnchorBottom = 1,
                ScrollActive = false,
                FitContent = true,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            lbl.AddThemeFontSizeOverride("normal_font_size", 20);
            lbl.AddThemeColorOverride("default_color", new Color(1, 0.965f, 0.886f));
            lbl.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
            lbl.AddThemeConstantOverride("outline_size", 6);
            TryApplyFont(lbl, KreonBoldPath, "normal_font");
            cell.AddChild(lbl);
        }

        // Hover handler — show the intent's standard hover tip near the
        // cell (proper alignment, not top-left).
        var captured = (intent, owner, (IEnumerable<Creature>)targets);
        cell.MouseEntered += () =>
        {
            try
            {
                if (!captured.intent.HasIntentTip) return;
                var tip = captured.intent.GetHoverTip(captured.Item3, captured.owner);
                var align = HoverTip.GetHoverTipAlignment(cell, 0.5f);
                NHoverTipSet.Remove(cell);
                NHoverTipSet.CreateAndShow(cell, new List<IHoverTip> { tip }, align);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"{EnemyCycleMod.LogPrefix}intent cell hover: {ex.Message}");
            }
        };
        cell.MouseExited += () =>
        {
            try { NHoverTipSet.Remove(cell); } catch { /* ignore */ }
        };
        return cell;
    }

    private static string SafeMonsterTitle(MonsterModel m)
    {
        try
        {
            var s = m?.Title?.GetFormattedText();
            if (!string.IsNullOrEmpty(s)) return s!;
        }
        catch { /* ignore */ }
        return m?.GetType().Name ?? "Monster";
    }

    private static void TryApplyFont(Control c, string fontPath, string slot)
    {
        try
        {
            var font = ResourceLoader.Load<Font>(fontPath);
            if (font != null) c.AddThemeFontOverride(slot, font);
        }
        catch { /* fall back */ }
    }

    private static void OnDimInput(InputEvent ev, Control overlay)
    {
        if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            overlay.QueueFree();
    }
}
