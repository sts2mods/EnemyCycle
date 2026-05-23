// Shared "should we suppress this modal-opening click?" check used by
// both the cycle preview rows and the live intent hitbox.
//
// Two signals — OR'd, any one blocks the modal:
//   1. NTargetManager.IsInSelection — the targeting arrow is up.
//   2. NPlayerHand._currentCardPlay — non-null whenever the player
//      has lifted a card from hand and a card-play is in flight,
//      regardless of whether the card requires a target. This is
//      the signal we needed for non-targeting plays (AoE attacks,
//      self-buff skills) where IsInSelection stays false.
//
// `_currentCardPlay` is private, so we reach it via reflection and
// cache the FieldInfo + NPlayerHand instance after the first hit.
using System;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace EnemyCycle;

internal static class ModalGuard
{
    private static NPlayerHand? _hand;
    private static FieldInfo? _currentCardPlayField;

    public static bool SuppressClicksForCardPlay()
    {
        try
        {
            var tm = NTargetManager.Instance;
            if (tm != null && tm.IsInSelection) return true;
            return IsCardPlayInFlight();
        }
        catch
        {
            return false;
        }
    }

    private static bool IsCardPlayInFlight()
    {
        try
        {
            if (_hand == null || !GodotObject.IsInstanceValid(_hand))
            {
                _hand = FindInTree<NPlayerHand>();
                if (_hand == null) return false;
            }
            _currentCardPlayField ??= typeof(NPlayerHand).GetField("_currentCardPlay",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (_currentCardPlayField == null) return false;
            return _currentCardPlayField.GetValue(_hand) != null;
        }
        catch { return false; }
    }

    private static T? FindInTree<T>() where T : Node
    {
        try
        {
            if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null) return null;
            return FindByType<T>(tree.Root);
        }
        catch { return null; }
    }

    private static T? FindByType<T>(Node node) where T : Node
    {
        if (node is T t) return t;
        foreach (var child in node.GetChildren())
        {
            var found = FindByType<T>(child);
            if (found != null) return found;
        }
        return null;
    }
}
