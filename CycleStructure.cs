// Classify a monster as "deterministic cycle" or "random" and return
// the appropriate display data:
//
//   • Deterministic: a clean repeating period exists in the live walk.
//     Return Intro + Cycle for the modal to render as before.
//
//   • Random: any future transition involves a multi-eligible
//     RandomBranchState. Return UniqueMoves (all reachable MoveStates
//     from the state graph) so the modal renders the set + a generated
//     rules description instead of leaking RNG outcomes.
//
// The "random vs deterministic" decision is structural — based on
// whether the state graph contains a RandomBranchState that could
// have >1 eligible branch given the live StateLog — rather than just
// "no period found in the walk". A monster whose walk happens to
// have no short period (long cycle) is still deterministic if every
// branch resolves to one eligible target.
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace EnemyCycle;

public readonly struct CycleInfo
{
    public readonly List<MoveState> Intro;
    public readonly List<MoveState> Cycle;
    public readonly int CurrentIndexInIntro;
    public readonly int CurrentIndexInCycle;
    public readonly bool IsRandom;
    public readonly List<MoveState> UniqueMoves;

    public CycleInfo(List<MoveState> intro, List<MoveState> cycle,
                    int curIntro, int curCycle,
                    bool isRandom, List<MoveState> uniqueMoves)
    {
        Intro = intro;
        Cycle = cycle;
        CurrentIndexInIntro = curIntro;
        CurrentIndexInCycle = curCycle;
        IsRandom = isRandom;
        UniqueMoves = uniqueMoves;
    }
}

public static class CycleStructure
{
    private const int WalkLookahead = 30;
    private const int MaxAllowedPeriod = 10;
    private const int MinFullPeriods = 3;

    public static CycleInfo Detect(MonsterModel monster)
    {
        if (monster == null) return Empty();

        var sm = MovePredictor.GetStateMachine(monster);
        if (sm == null) return Empty();
        var states = MovePredictor.GetStates(sm);
        if (states == null) return Empty();
        var liveCurrent = MovePredictor.GetCurrentState(sm) as MoveState;
        if (liveCurrent == null) return Empty();

        var uniqueMoves = EnumerateReachableMoves(sm);
        bool structurallyRandom = HasMultiEligibleBranch(sm);

        // Try deterministic detection: walk forward and look for a
        // strict periodic prefix. If we find one and the monster is
        // not structurally random, render as a clean cycle.
        var upcoming = MovePredictor.PeekNext(monster, WalkLookahead);
        var fullWalk = new List<MoveState> { liveCurrent };
        fullWalk.AddRange(upcoming);

        if (!structurallyRandom)
        {
            var (introLen, cycleLen) = FindPeriodicTail(fullWalk);
            if (cycleLen > 0)
            {
                var introMoves = fullWalk.Take(introLen).ToList();
                var cycleMoves = fullWalk.Skip(introLen).Take(cycleLen).ToList();
                int introIdx = introLen > 0 ? 0 : -1;
                int cycleIdx = introLen > 0 ? -1 : 0;
                LogDetection(monster, fullWalk, introMoves, cycleMoves, uniqueMoves, "period", false);
                return new CycleInfo(introMoves, cycleMoves, introIdx, cycleIdx, false, uniqueMoves);
            }
        }

        // Random: no leak. Modal will render uniqueMoves + description.
        LogDetection(monster, fullWalk, new List<MoveState>(), new List<MoveState>(), uniqueMoves, "random", true);
        return new CycleInfo(new List<MoveState>(), new List<MoveState>(), -1, -1, true, uniqueMoves);
    }

    // Has any RandomBranchState in the graph that, given the current
    // StateLog, could have >1 branch with positive weight?
    private static bool HasMultiEligibleBranch(MonsterMoveStateMachine sm)
    {
        foreach (var st in sm.States.Values)
        {
            if (st is not RandomBranchState rand) continue;
            int positiveCount = 0;
            foreach (var sw in rand.States)
            {
                float w;
                try { w = MovePredictor.ComputeBranchWeight(sw, sm); }
                catch { return true; } // assume worst-case
                if (w > 0f) positiveCount++;
                if (positiveCount > 1) return true;
            }
        }
        return false;
    }

    // BFS the state graph starting from the initial state, collecting
    // every reachable MoveState in first-seen order.
    private static List<MoveState> EnumerateReachableMoves(MonsterMoveStateMachine sm)
    {
        var result = new List<MoveState>();
        var seen = new HashSet<string>();
        var queue = new Queue<MonsterState>();
        var initial = MovePredictor.GetInitialState(sm)
                      ?? MovePredictor.GetCurrentState(sm);
        if (initial != null) queue.Enqueue(initial);

        while (queue.Count > 0)
        {
            var st = queue.Dequeue();
            if (!seen.Add(st.Id)) continue;
            if (st is MoveState mv) result.Add(mv);
            foreach (var nextId in NextStateIds(st))
            {
                if (string.IsNullOrEmpty(nextId)) continue;
                if (sm.States.TryGetValue(nextId, out var nxt) && !seen.Contains(nextId))
                    queue.Enqueue(nxt);
            }
        }
        return result;
    }

    private static IEnumerable<string> NextStateIds(MonsterState st)
    {
        switch (st)
        {
            case MoveState mv:
                var id = mv.FollowUpState?.Id ?? mv.FollowUpStateId;
                if (!string.IsNullOrEmpty(id)) yield return id!;
                break;
            case RandomBranchState rand:
                foreach (var sw in rand.States) yield return sw.stateId;
                break;
            // ConditionalBranchState branches are private — we can't
            // enumerate them here, so reachability from a conditional
            // is undercounted. Acceptable: TwigSlime/Flyconid/etc.
            // don't use ConditionalBranchState for branching.
        }
    }

    private static (int introLen, int cycleLen) FindPeriodicTail(List<MoveState> seq)
    {
        int n = seq.Count;
        if (n < 2) return (0, 0);
        int maxIntro = n / 2;
        for (int introLen = 0; introLen <= maxIntro; introLen++)
        {
            int suffixLen = n - introLen;
            int maxP = Math.Min(MaxAllowedPeriod, suffixLen / 2);
            for (int p = 1; p <= maxP; p++)
            {
                if (suffixLen < MinFullPeriods * p) continue;
                bool periodic = true;
                for (int i = introLen + p; i < n; i++)
                {
                    if (!SameMove(seq[i], seq[i - p])) { periodic = false; break; }
                }
                if (periodic) return (introLen, p);
            }
        }
        return (0, 0);
    }

    private static bool SameMove(MoveState a, MoveState b)
    {
        if (a == null || b == null) return a == b;
        return a.StateId == b.StateId;
    }

    private static void LogDetection(MonsterModel monster, List<MoveState> walk,
                                     List<MoveState> intro, List<MoveState> cycle,
                                     List<MoveState> unique, string mode, bool isRandom)
    {
        try
        {
            var monsterName = monster.GetType().Name;
            var walkStr = string.Join(",", walk.Select(m => Shorten(m.StateId)));
            var uniqStr = string.Join(",", unique.Select(m => Shorten(m.StateId)));
            string detail = isRandom
                ? $"unique({unique.Count})=[{uniqStr}]"
                : $"intro({intro.Count})=[{string.Join(",", intro.Select(m => Shorten(m.StateId)))}] " +
                  $"cycle({cycle.Count})=[{string.Join(",", cycle.Select(m => Shorten(m.StateId)))}]";
            GD.Print($"{EnemyCycleMod.LogPrefix}cycle[{monsterName}] walk({walk.Count})=[{walkStr}] → {detail} [{mode}]");
        }
        catch { /* ignore log issues */ }
    }

    private static string Shorten(string? id)
    {
        if (string.IsNullOrEmpty(id)) return "?";
        var s = id!;
        if (s.EndsWith("_MOVE", StringComparison.Ordinal)) s = s.Substring(0, s.Length - 5);
        if (s.Length > 8) s = s.Substring(0, 8);
        return s;
    }

    private static CycleInfo Empty() =>
        new CycleInfo(new List<MoveState>(), new List<MoveState>(),
                      -1, -1, false, new List<MoveState>());
}
