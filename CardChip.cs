// Small chip representing a status/curse card a monster move adds to
// the player's deck. Shows the card's portrait + count, and on hover
// renders a full card preview via the game's NHoverTipSet
// (CardHoverTip → card-sized tooltip with art and description).
using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes.HoverTips;

namespace EnemyCycle;

public static class CardChip
{
    public static Control Create(AddedCard ap, float iconSize)
    {
        var portrait = CardResolver.GetCardPortrait(ap.CardType);

        var root = new Control
        {
            CustomMinimumSize = new Vector2(iconSize, iconSize),
            MouseFilter = Control.MouseFilterEnum.Stop,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };

        if (portrait != null)
        {
            var tex = new TextureRect
            {
                Texture = portrait,
                AnchorRight = 1, AnchorBottom = 1,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                ClipContents = true,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            root.AddChild(tex);
        }
        else
        {
            // No portrait found — show a placeholder so the chip is
            // still visible/hoverable.
            var ph = new ColorRect
            {
                Color = new Color(0.45f, 0.2f, 0.55f, 0.5f),
                AnchorRight = 1, AnchorBottom = 1,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            root.AddChild(ph);
        }

        // Subtle border so the chip reads as "a card", not a power.
        var border = new ReferenceRect
        {
            AnchorRight = 1, AnchorBottom = 1,
            BorderColor = new Color(0.7f, 0.55f, 0.95f, 0.9f),
            BorderWidth = 1.5f,
            EditorOnly = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        root.AddChild(border);

        if (ap.Count.HasValue && ap.Count.Value > 1)
        {
            var lbl = new Label
            {
                Text = "x" + ap.Count.Value.ToString(),
                AnchorLeft = 0.4f, AnchorTop = 0.45f, AnchorRight = 1, AnchorBottom = 1,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
            };
            lbl.AddThemeFontSizeOverride("font_size", (int)Math.Max(10, iconSize * 0.42f));
            lbl.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
            lbl.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f));
            lbl.AddThemeConstantOverride("outline_size", 4);
            root.AddChild(lbl);
        }

        root.MouseEntered += () => OnHover(root, ap);
        root.MouseExited += () => OnUnhover(root);
        return root;
    }

    private static void OnHover(Control anchor, AddedCard ap)
    {
        try
        {
            var tip = CardResolver.GetHoverTipForCard(ap.CardType);
            if (tip == null) return;
            var align = HoverTip.GetHoverTipAlignment(anchor, 0.5f);
            NHoverTipSet.Remove(anchor);
            NHoverTipSet.CreateAndShow(anchor, new List<IHoverTip> { tip }, align);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{EnemyCycleMod.LogPrefix}CardChip hover: {ex.Message}");
        }
    }

    private static void OnUnhover(Control anchor)
    {
        try { NHoverTipSet.Remove(anchor); } catch { /* ignore */ }
    }
}
