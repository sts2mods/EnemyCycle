// Inject an Enemy Cycle row into the in-game Settings screen so the
// user can flip preview modes without backing out to the main menu's
// Mod info screen. The same screen scene backs the Settings popup
// you get from the in-combat pause menu, so this one patch covers
// both code paths.
//
// Layout mirrors the game's own Screenshake row: a divider, then a
// label on the left with a "paginator" on the right — left arrow,
// mode name, right arrow. We reuse the game's own arrow textures so
// the row looks native instead of bolted on.
using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;

namespace EnemyCycle;

[HarmonyPatch(typeof(NSettingsScreen), "_Ready")]
public static class NSettingsScreen_Ready_Patch
{
    private const string RowName = "EC_ModeRow";
    private const string DividerName = "EC_ModeDivider";
    private const string LabelInPaginatorName = "EC_PaginatorLabel";

    private const string LeftArrowPath  = "res://images/atlases/ui_atlas.sprites/settings_tiny_left_arrow.tres";
    private const string RightArrowPath = "res://images/packed/common_ui/settings_tiny_right_arrow.png";

    static void Postfix(NSettingsScreen __instance)
    {
        try
        {
            var vbox = __instance.GetNodeOrNull<VBoxContainer>(
                "ScrollContainer/Mask/Clipper/GeneralSettings/VBoxContainer");
            if (vbox == null) return;
            // Idempotent — _Ready can fire repeatedly when the popup
            // reopens; bail if we already injected.
            if (vbox.GetNodeOrNull<Node>(RowName) != null) return;

            // Cream-tinted 2px divider, same as the rows above.
            vbox.AddChild(new ColorRect
            {
                Name = DividerName,
                Color = new Color(0.909804f, 0.862745f, 0.745098f, 0.25098f),
                CustomMinimumSize = new Vector2(0, 2),
                MouseFilter = Control.MouseFilterEnum.Ignore,
                SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            });

            var row = new MarginContainer
            {
                Name = RowName,
                CustomMinimumSize = new Vector2(0, 64),
            };
            row.AddThemeConstantOverride("margin_left", 12);
            row.AddThemeConstantOverride("margin_right", 12);
            vbox.AddChild(row);

            var hb = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Pass };
            hb.AddThemeConstantOverride("separation", 12);
            row.AddChild(hb);

            // Left side: setting label.
            var label = new Label
            {
                Text = "Enemy Cycle Preview",
                VerticalAlignment = VerticalAlignment.Center,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            label.AddThemeFontSizeOverride("font_size", 28);
            label.AddThemeColorOverride("font_color", StsColors.cream);
            label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.25098f));
            label.AddThemeConstantOverride("shadow_offset_x", 3);
            label.AddThemeConstantOverride("shadow_offset_y", 2);
            hb.AddChild(label);

            // Right side: paginator (left arrow, value label, right arrow).
            var paginator = BuildPaginator();
            hb.AddChild(paginator);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{EnemyCycleMod.LogPrefix}settings screen patch: {ex.Message}");
        }
    }

    private static Control BuildPaginator()
    {
        var root = new HBoxContainer
        {
            CustomMinimumSize = new Vector2(280, 64),
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        root.AddThemeConstantOverride("separation", 6);

        Label? valueLbl = null;

        var leftArrow = BuildArrow(LeftArrowPath, () =>
        {
            EnemyCycleSettings.CycleModePrev();
            if (valueLbl != null && GodotObject.IsInstanceValid(valueLbl))
                valueLbl.Text = ModeText(EnemyCycleMod.Mode);
        });
        root.AddChild(leftArrow);

        valueLbl = new Label
        {
            Name = LabelInPaginatorName,
            Text = ModeText(EnemyCycleMod.Mode),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(160, 64),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        valueLbl.AddThemeFontSizeOverride("font_size", 28);
        valueLbl.AddThemeColorOverride("font_color", StsColors.cream);
        valueLbl.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.25098f));
        valueLbl.AddThemeConstantOverride("shadow_offset_x", 3);
        valueLbl.AddThemeConstantOverride("shadow_offset_y", 2);
        root.AddChild(valueLbl);

        var rightArrow = BuildArrow(RightArrowPath, () =>
        {
            EnemyCycleSettings.CycleMode();
            if (valueLbl != null && GodotObject.IsInstanceValid(valueLbl))
                valueLbl.Text = ModeText(EnemyCycleMod.Mode);
        });
        root.AddChild(rightArrow);

        return root;
    }

    // 64×64 click area with a centred arrow texture. Falls back to a
    // text glyph if the texture asset isn't loadable for some reason
    // (e.g. a patch shuffled them around).
    private static Control BuildArrow(string texPath, Action onPressed)
    {
        var btn = new Button
        {
            Flat = true,
            CustomMinimumSize = new Vector2(64, 64),
            FocusMode = Control.FocusModeEnum.None,
            MouseFilter = Control.MouseFilterEnum.Stop,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };
        btn.Pressed += () => onPressed();

        var tex = ResourceLoader.Load<Texture2D>(texPath);
        if (tex != null)
        {
            var img = new TextureRect
            {
                Texture = tex,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                AnchorLeft = 0, AnchorTop = 0, AnchorRight = 1, AnchorBottom = 1,
                OffsetLeft = 0, OffsetTop = 0, OffsetRight = 0, OffsetBottom = 0,
            };
            img.Scale = new Vector2(0.75f, 0.75f);
            img.PivotOffset = new Vector2(32, 32);
            btn.AddChild(img);
        }
        else
        {
            btn.Text = texPath.Contains("left") ? "◀" : "▶";
            btn.AddThemeFontSizeOverride("font_size", 28);
        }
        return btn;
    }

    private static string ModeText(EnemyCycleMod.PreviewMode m) => m switch
    {
        EnemyCycleMod.PreviewMode.Always  => "Always",
        EnemyCycleMod.PreviewMode.OnHover => "On hover",
        EnemyCycleMod.PreviewMode.Never   => "Never",
        _ => m.ToString(),
    };
}
