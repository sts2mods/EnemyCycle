// Chip for an affliction a monster move applies to player cards.
// Shows a small label-style icon (using the affliction's localized
// title initial) and binds the game's HoverTipFactory.FromAffliction
// tooltips so hover renders the same description the player sees
// elsewhere.
//
// Afflictions don't have a portrait Texture in the game model — the
// per-card overlay is a Godot scene at OverlayPath rather than a
// reusable icon. Rather than instantiate that scene (which expects
// to be parented to a card), we render a labeled badge that shares
// the same visual rhythm as PowerChip / CardChip.
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.HoverTips;

namespace EnemyCycle;

public static class AfflictionChip
{
    public static Control Create(AppliedAffliction ap, float iconSize)
    {
        var root = new Control
        {
            CustomMinimumSize = new Vector2(iconSize, iconSize),
            MouseFilter = Control.MouseFilterEnum.Stop,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };

        // Background — muted purple-red to differentiate from power
        // (red) and card (purple-bordered) chips.
        var bg = new ColorRect
        {
            Color = new Color(0.40f, 0.10f, 0.20f, 0.85f),
            AnchorRight = 1, AnchorBottom = 1,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        root.AddChild(bg);

        var border = new ReferenceRect
        {
            AnchorRight = 1, AnchorBottom = 1,
            BorderColor = new Color(0.95f, 0.5f, 0.55f, 0.95f),
            BorderWidth = 1.5f,
            EditorOnly = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        root.AddChild(border);

        // Glyph: first letter of the affliction's title, or "!" if
        // we can't resolve a localized name.
        string glyph = "!";
        try
        {
            var inst = AfflictionResolver.GetInstance(ap.AfflictionType);
            var title = inst?.Title.GetFormattedText();
            if (!string.IsNullOrEmpty(title)) glyph = char.ToUpperInvariant(title![0]).ToString();
        }
        catch { /* fall back to "!" */ }

        var lbl = new Label
        {
            Text = glyph,
            AnchorRight = 1, AnchorBottom = 1,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        lbl.AddThemeFontSizeOverride("font_size", (int)Math.Max(12, iconSize * 0.55f));
        lbl.AddThemeColorOverride("font_color", StsColors.cream);
        lbl.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        lbl.AddThemeConstantOverride("outline_size", 4);
        root.AddChild(lbl);

        if (ap.Amount.HasValue && ap.Amount.Value > 1)
        {
            var count = new Label
            {
                Text = "x" + ap.Amount.Value,
                AnchorLeft = 0.4f, AnchorTop = 0.45f, AnchorRight = 1, AnchorBottom = 1,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
            };
            count.AddThemeFontSizeOverride("font_size", (int)Math.Max(10, iconSize * 0.40f));
            count.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
            count.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
            count.AddThemeConstantOverride("outline_size", 4);
            root.AddChild(count);
        }

        root.MouseEntered += () => OnHover(root, ap);
        root.MouseExited += () => OnUnhover(root);
        return root;
    }

    private static void OnHover(Control anchor, AppliedAffliction ap)
    {
        try
        {
            var tips = AfflictionResolver.GetHoverTips(ap.AfflictionType, ap.Amount ?? 1);
            if (tips == null) return;
            var list = tips.ToList();
            if (list.Count == 0) return;
            var align = HoverTip.GetHoverTipAlignment(anchor, 0.5f);
            NHoverTipSet.Remove(anchor);
            NHoverTipSet.CreateAndShow(anchor, list, align);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{EnemyCycleMod.LogPrefix}AfflictionChip hover: {ex.Message}");
        }
    }

    private static void OnUnhover(Control anchor)
    {
        try { NHoverTipSet.Remove(anchor); } catch { /* ignore */ }
    }
}
