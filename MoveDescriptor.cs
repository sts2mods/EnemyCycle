// Auto-generated "wiki-style" description for a monster's move logic.
// Used by the modal when CycleStructure classifies the monster as
// random — instead of leaking a specific RNG outcome, render the rules
// the player could infer themselves from observation.
//
// Output is BBCode (rendered by RichTextLabel). Highlights:
//   • Move names  → gold       (StsColors.gold,   #EFC851)
//   • Numbers     → orange     (StsColors.orange, #FFA518)
//     (percentages, repeat limits, cooldown turns)
// Body text uses the label's default color (cream) untouched.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace EnemyCycle;

public static class MoveDescriptor
{
    private const string GoldHex = "#EFC851";
    private const string OrangeHex = "#FFA518";

    // Plain-text summary of a deterministic monster's intro + cycle, so
    // the modal's "Pattern" section isn't empty for fully predictable
    // monsters. Move names rendered as BBCode gold tags.
    public static string DescribePattern(MonsterModel monster, CycleInfo info)
    {
        if (info.Cycle.Count == 0 && info.Intro.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        bool hasIntro = info.Intro.Count > 0;
        bool hasCycle = info.Cycle.Count > 0;

        if (hasIntro)
        {
            sb.Append("Always starts with ");
            for (int i = 0; i < info.Intro.Count; i++)
            {
                if (i > 0) sb.Append(", then ");
                sb.Append(NameTag(monster, info.Intro[i]));
            }
            sb.Append('.');
            if (hasCycle) sb.AppendLine();
        }
        if (hasCycle)
        {
            sb.Append(hasIntro ? "Afterward, repeats " : "Always repeats ");
            for (int i = 0; i < info.Cycle.Count; i++)
            {
                if (i > 0) sb.Append(" → ");
                sb.Append(NameTag(monster, info.Cycle[i]));
            }
            sb.Append(" forever.");
        }
        return sb.ToString();
    }

    // `orderedMoves` is the same list used to render the "Moves" rows
    // at the top of the modal (info.UniqueMoves). We thread it through
    // so the pattern lines render in the same order the player sees in
    // those rows — both *which* "After X" line comes first, and the
    // order within "After X or Y or Z" enumerations.
    public static string Describe(MonsterModel monster, IReadOnlyList<MoveState>? orderedMoves = null)
    {
        if (monster == null) return string.Empty;
        var sm = MovePredictor.GetStateMachine(monster);
        if (sm == null) return string.Empty;

        var sb = new StringBuilder();

        // Initial state — only meaningful as "always starts with" if it
        // is a MoveState (not a branch).
        var initial = MovePredictor.GetInitialState(sm);
        if (initial is MoveState im)
        {
            sb.Append("Always starts with ").Append(NameTag(monster, im)).Append('.');
            sb.AppendLine();
            sb.AppendLine();
        }

        // Map each StateId → its row index at the top so we can sort
        // by it later. Anything not in the row list sorts after, in
        // its original state-dict order.
        var rowOrder = new Dictionary<string, int>(StringComparer.Ordinal);
        if (orderedMoves != null)
        {
            for (int i = 0; i < orderedMoves.Count; i++)
            {
                var id = orderedMoves[i]?.StateId;
                if (!string.IsNullOrEmpty(id) && !rowOrder.ContainsKey(id!))
                    rowOrder[id!] = i;
            }
        }
        int RowIndex(MoveState m) =>
            (m?.StateId != null && rowOrder.TryGetValue(m.StateId, out var idx))
                ? idx
                : int.MaxValue;

        // Group MoveStates by their FollowUpState identity so monsters
        // where every move shares the same RAND branch get described
        // once ("After any move, randomly chooses...") instead of N
        // times with identical bullet lists.
        var moveStates = sm.States.Values.OfType<MoveState>().ToList();
        var groups = new Dictionary<string, (MonsterState followUp, List<MoveState> owners)>();
        foreach (var m in moveStates)
        {
            var fu = m.FollowUpState;
            if (fu == null) continue;
            if (!groups.TryGetValue(fu.Id, out var g))
            {
                g = (fu, new List<MoveState>());
                groups[fu.Id] = g;
            }
            g.owners.Add(m);
        }

        // Sort owners within each group by their row position so the
        // "X or Y" enumeration matches the rows above.
        foreach (var kv in groups)
        {
            kv.Value.owners.Sort((a, b) => RowIndex(a).CompareTo(RowIndex(b)));
        }

        // Sort the groups themselves by the row position of their
        // first owner — the first row at the top should be the first
        // move mentioned down here too.
        var orderedGroups = groups.Values
            .OrderBy(g => g.owners.Count > 0 ? RowIndex(g.owners[0]) : int.MaxValue)
            .ToList();

        bool first = true;
        foreach (var g in orderedGroups)
        {
            if (!first) sb.AppendLine();
            first = false;
            string prefix = g.owners.Count == moveStates.Count
                ? "After each move"
                : "After " + string.Join(" or ", g.owners.Select(m => NameTag(monster, m)));
            AppendTransition(sb, monster, prefix, g.followUp);
        }

        return sb.ToString().TrimEnd();
    }

    private static void AppendTransition(StringBuilder sb, MonsterModel monster,
                                          string prefix, MonsterState followUp)
    {
        switch (followUp)
        {
            case MoveState mv:
                sb.Append(prefix).Append(", uses ").Append(NameTag(monster, mv)).Append('.');
                sb.AppendLine();
                return;
            case RandomBranchState rand:
                sb.Append(prefix).Append(", randomly chooses:");
                sb.AppendLine();
                float total = 0f;
                foreach (var sw in rand.States)
                {
                    try { total += sw.GetWeight(); } catch { total += 1f; }
                }
                if (total <= 0f) total = 1f;
                foreach (var sw in rand.States)
                {
                    float w = 1f;
                    try { w = sw.GetWeight(); } catch { /* keep 1 */ }
                    int pct = (int)Math.Round(w / total * 100);
                    string note = DescribeRule(sw);
                    sb.Append("  • ");
                    sb.Append(NameTagForId(monster, sw.stateId));
                    sb.Append(" — ").Append(Num(pct + "%"));
                    if (!string.IsNullOrEmpty(note))
                    {
                        sb.Append(" (").Append(note).Append(')');
                    }
                    sb.AppendLine();
                }
                return;
            case ConditionalBranchState:
                sb.Append(prefix).Append(", picks based on the monster's state.");
                sb.AppendLine();
                return;
            default:
                sb.Append(prefix).Append(", continues.");
                sb.AppendLine();
                return;
        }
    }

    private static string DescribeRule(RandomBranchState.StateWeight sw)
    {
        var parts = new List<string>();
        if (sw.repeatType.Equals(MoveRepeatType.UseOnlyOnce))
            parts.Add("only once per combat");
        else if (sw.repeatType.Equals(MoveRepeatType.CannotRepeat))
            parts.Add("cannot use twice in a row");
        else if (sw.repeatType.Equals(MoveRepeatType.CanRepeatXTimes) && sw.maxTimes > 0)
            parts.Add($"at most {Num(sw.maxTimes)} times in a row");
        if (sw.cooldown > 0)
            parts.Add($"{Num(sw.cooldown)}-turn cooldown");
        return string.Join(", ", parts);
    }

    private static string Num(object v) => $"[color={OrangeHex}]{v}[/color]";

    private static string NameTag(MonsterModel monster, MoveState mv) =>
        $"[color={GoldHex}]{GetDisplayName(monster, mv)}[/color]";

    private static string NameTagForId(MonsterModel monster, string stateId) =>
        $"[color={GoldHex}]{GetMoveDisplayName(monster, stateId)}[/color]";

    private static string GetMoveDisplayName(MonsterModel monster, string stateId)
    {
        var sm = MovePredictor.GetStateMachine(monster);
        if (sm != null && sm.States.TryGetValue(stateId, out var st) && st is MoveState mv)
            return GetDisplayName(monster, mv);
        return PrettyId(stateId);
    }

    private static string GetDisplayName(MonsterModel monster, MoveState mv)
    {
        try { return MoveNameHelper.GetDisplayName(monster, mv) ?? PrettyId(mv.StateId); }
        catch { return PrettyId(mv.StateId); }
    }

    private static string PrettyId(string? stateId)
    {
        if (string.IsNullOrEmpty(stateId)) return "?";
        var s = stateId!;
        if (s.EndsWith("_MOVE", StringComparison.Ordinal))
            s = s.Substring(0, s.Length - 5);
        var parts = s.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            var p = parts[i].ToLowerInvariant();
            if (p.Length > 0)
            {
                sb.Append(char.ToUpperInvariant(p[0]));
                if (p.Length > 1) sb.Append(p, 1, p.Length - 1);
            }
        }
        return sb.ToString();
    }
}
