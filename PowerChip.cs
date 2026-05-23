// Small icon+amount chip representing a power a move applies. Hover
// shows the power's dumb description via the game's NHoverTipSet. Used
// in both the preview row above the enemy and inside the modal.
using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Nodes.HoverTips;

namespace EnemyCycle;

public static class PowerChip
{
    public static Control Create(AppliedPower ap, float iconSize)
    {
        var icon = PowerResolver.GetPowerIcon(ap.PowerType);

        var root = new Control
        {
            CustomMinimumSize = new Vector2(iconSize, iconSize),
            MouseFilter = Control.MouseFilterEnum.Stop,
            // ShrinkCenter so the chip vertically centers within
            // taller HBox rows (modal iconSlot is 48 tall, chips are
            // smaller and were collapsing to the top).
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };

        if (icon != null)
        {
            var tex = new TextureRect
            {
                Texture = icon,
                AnchorRight = 1, AnchorBottom = 1,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            root.AddChild(tex);
        }
        else
        {
            // No icon found — show a placeholder so the chip is still
            // visible/hoverable.
            var ph = new ColorRect
            {
                Color = new Color(0.4f, 0.2f, 0.5f, 0.5f),
                AnchorRight = 1, AnchorBottom = 1,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            root.AddChild(ph);
        }

        // Stack-count label (bottom-right corner). Skip negative/zero;
        // negative is usually "infinite" (e.g. Shrink -1) and zero is
        // "unknown amount".
        if (ap.Amount.HasValue && ap.Amount.Value > 0)
        {
            var lbl = new Label
            {
                Text = ap.Amount.Value.ToString(),
                AnchorLeft = 0.45f, AnchorTop = 0.45f, AnchorRight = 1, AnchorBottom = 1,
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

        // Hover handlers — show the power's hover tip anchored to this
        // chip (so it's positioned correctly whether the chip is above
        // the enemy or inside the modal).
        root.MouseEntered += () => OnHover(root, ap);
        root.MouseExited += () => OnUnhover(root);
        return root;
    }

    private static void OnHover(Control anchor, AppliedPower ap)
    {
        try
        {
            var tip = PowerResolver.GetHoverTipForPower(ap.PowerType, ap.Amount);
            if (tip == null) return;
            var align = HoverTip.GetHoverTipAlignment(anchor, 0.5f);
            NHoverTipSet.Remove(anchor);
            NHoverTipSet.CreateAndShow(anchor, new List<IHoverTip> { tip }, align);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{EnemyCycleMod.LogPrefix}PowerChip hover: {ex.Message}");
        }
    }

    private static void OnUnhover(Control anchor)
    {
        try { NHoverTipSet.Remove(anchor); } catch { /* ignore */ }
    }
}
