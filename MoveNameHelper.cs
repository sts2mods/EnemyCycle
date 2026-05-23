// Localized display name for a monster move. The bestiary uses
// "monsters.<MONSTER>.moves.<MOVE>.title" (with the _MOVE suffix
// stripped from the state id) — we reuse that key. If no loc exists,
// fall back to a prettified version of the state id.
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace EnemyCycle;

public static class MoveNameHelper
{
    public static string GetDisplayName(MonsterModel monster, MoveState mv)
    {
        if (mv == null) return "";
        string stateId = mv.StateId ?? "";
        string trimmed = stateId.EndsWith("_MOVE", System.StringComparison.Ordinal)
            ? stateId.Substring(0, stateId.Length - 5)
            : stateId;

        string entry = monster?.Id.Entry ?? "";
        string key = entry + ".moves." + trimmed + ".title";

        if (LocString.Exists("monsters", key))
            return new LocString("monsters", key).GetFormattedText() ?? Prettify(trimmed);

        return Prettify(trimmed);
    }

    private static string Prettify(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        // SHRINKER_MOVE → Shrinker; STOMP → Stomp
        var parts = raw.Split('_');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0) continue;
            parts[i] = char.ToUpperInvariant(parts[i][0]) +
                       parts[i].Substring(1).ToLowerInvariant();
        }
        return string.Join(" ", parts);
    }
}
