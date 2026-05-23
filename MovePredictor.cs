// Peek upcoming moves for a monster — two flavors:
//
//  • PeekNext(N): walks via GetNextState with a cloned RNG. Mutates
//    the live StateLog temporarily (restored in finally) so
//    RandomBranchState's weight calc reads the right history. Random
//    branches resolve to *some* outcome (the RNG snapshot's roll).
//
//  • PeekDeterministic(N): walks only as far as the next transition
//    is fully forced — when a RandomBranchState has >1 branch with
//    positive weight, stops. Used for the above-enemy preview rows so
//    we never leak RNG-derived information to the player.
//
// Both call into MonsterMoveStateMachine's own States/StateLog
// (public) and replicate RandomBranchState's eligibility math.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Random;

namespace EnemyCycle;

public static class MovePredictor
{
    private static readonly FieldInfo? CurrentStateField =
        typeof(MonsterMoveStateMachine).GetField("_currentState",
            BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? InitialStateField =
        typeof(MonsterMoveStateMachine).GetField("_initialState",
            BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly PropertyInfo? StatesProp =
        typeof(MonsterMoveStateMachine).GetProperty("States",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    private static readonly PropertyInfo? StateLogProp =
        typeof(MonsterMoveStateMachine).GetProperty("StateLog",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    private static readonly FieldInfo? RngField =
        typeof(MonsterModel).GetField("_rng",
            BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? MoveSmField =
        typeof(MonsterModel).GetField("_moveStateMachine",
            BindingFlags.Instance | BindingFlags.NonPublic);

    public static MonsterMoveStateMachine? GetStateMachine(MonsterModel m) =>
        MoveSmField?.GetValue(m) as MonsterMoveStateMachine;

    public static MonsterState? GetCurrentState(MonsterMoveStateMachine sm) =>
        CurrentStateField?.GetValue(sm) as MonsterState;

    public static MonsterState? GetInitialState(MonsterMoveStateMachine sm) =>
        InitialStateField?.GetValue(sm) as MonsterState;

    public static IDictionary<string, MonsterState>? GetStates(MonsterMoveStateMachine sm) =>
        StatesProp?.GetValue(sm) as IDictionary<string, MonsterState>;

    public static List<MoveState> PeekNext(MonsterModel monster, int count)
    {
        var result = new List<MoveState>();
        if (monster == null || count <= 0) return result;

        var sm = GetStateMachine(monster);
        if (sm == null) return result;
        var states = GetStates(sm);
        if (states == null) return result;
        var current = GetCurrentState(sm);
        if (current == null) return result;
        var rngOrig = RngField?.GetValue(monster) as Rng;
        if (rngOrig == null) return result;
        var rngClone = new Rng(rngOrig.Seed, rngOrig.Counter);

        var stateLog = StateLogProp?.GetValue(sm) as List<MonsterState>;
        int originalLogCount = stateLog?.Count ?? -1;

        var owner = monster.Creature;
        var walker = current;
        int sanity = count * 20;

        try
        {
            while (result.Count < count && sanity-- > 0)
            {
                string nextId;
                try { nextId = walker.GetNextState(owner, rngClone); }
                catch (Exception ex)
                {
                    GD.PrintErr($"{EnemyCycleMod.LogPrefix}GetNextState failed: {ex.Message}");
                    break;
                }
                if (string.IsNullOrEmpty(nextId) || !states.TryGetValue(nextId, out var next))
                    break;
                if (next is MoveState mv)
                {
                    result.Add(mv);
                    stateLog?.Add(mv);
                    walker = mv;
                }
                else
                {
                    walker = next;
                }
            }
        }
        finally
        {
            if (stateLog != null && originalLogCount >= 0 && stateLog.Count > originalLogCount)
            {
                stateLog.RemoveRange(originalLogCount, stateLog.Count - originalLogCount);
            }
        }

        return result;
    }

    // Walks forward only across forced transitions. A transition is
    // "forced" when there's exactly one eligible target:
    //   - MoveState.FollowUpState                  → one target.
    //   - ConditionalBranchState                   → first true branch.
    //   - RandomBranchState w/ exactly one branch  → that branch.
    // Stops the moment the next step would require an RNG roll
    // (>=2 eligible branches), so we never expose hidden info.
    public static List<MoveState> PeekDeterministic(MonsterModel monster, int maxCount)
    {
        var result = new List<MoveState>();
        if (monster == null || maxCount <= 0) return result;
        var sm = GetStateMachine(monster);
        if (sm == null) return result;
        var states = GetStates(sm);
        if (states == null) return result;
        var current = GetCurrentState(sm);
        if (current == null) return result;

        var owner = monster.Creature;
        var stateLog = StateLogProp?.GetValue(sm) as List<MonsterState>;
        int originalLogCount = stateLog?.Count ?? -1;
        var walker = current;
        int sanity = maxCount * 10;

        try
        {
            while (result.Count < maxCount && sanity-- > 0)
            {
                string? nextId = TryForcedNext(walker, owner, sm);
                if (nextId == null || !states.TryGetValue(nextId, out var next)) break;
                if (next is MoveState mv)
                {
                    result.Add(mv);
                    stateLog?.Add(mv);
                    walker = mv;
                }
                else
                {
                    walker = next;
                }
            }
        }
        finally
        {
            if (stateLog != null && originalLogCount >= 0 && stateLog.Count > originalLogCount)
            {
                stateLog.RemoveRange(originalLogCount, stateLog.Count - originalLogCount);
            }
        }
        return result;
    }

    private static string? TryForcedNext(MonsterState walker, Creature owner, MonsterMoveStateMachine sm)
    {
        switch (walker)
        {
            case MoveState mv:
                return mv.FollowUpState?.Id ?? mv.FollowUpStateId;
            case ConditionalBranchState:
                try { return walker.GetNextState(owner, null!); }
                catch { return null; }
            case RandomBranchState rand:
            {
                string? only = null;
                foreach (var sw in rand.States)
                {
                    float w;
                    try { w = ComputeBranchWeight(sw, sm); }
                    catch { return null; }
                    if (w > 0f)
                    {
                        if (only != null) return null;
                        only = sw.stateId;
                    }
                }
                return only;
            }
        }
        return null;
    }

    // Mirror of RandomBranchState.GetStateWeight (private) using only
    // the public surface — see decompiled source for parity.
    public static float ComputeBranchWeight(RandomBranchState.StateWeight sw, MonsterMoveStateMachine sm)
    {
        var stateLog = sm.StateLog;
        var states = sm.States;
        float num = 1f;
        if (sw.repeatType.Equals(MoveRepeatType.UseOnlyOnce))
        {
            if (stateLog.Contains(states[sw.stateId])) num = 0f;
        }
        else if (!sw.repeatType.Equals(MoveRepeatType.CanRepeatForever))
        {
            float maxN = sw.repeatType.Equals(MoveRepeatType.CannotRepeat) ? 1 : sw.maxTimes;
            num = ((float)stateLog.Count < maxN) ? 1 : 0;
            int j = 0;
            while ((float)stateLog.Count >= maxN && (float)j < maxN && stateLog.Count - j > 0)
            {
                if (stateLog[stateLog.Count - 1 - j] != states[sw.stateId])
                {
                    num = 1f;
                    break;
                }
                j++;
            }
        }
        if (sw.cooldown > 0)
        {
            var recent = stateLog.Where(s => s.IsMove).Reverse().Take(sw.cooldown);
            if (recent.Any(m => m.Id == sw.stateId)) return 0f;
        }
        return num * sw.GetWeight();
    }
}
