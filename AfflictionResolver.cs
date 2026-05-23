// Detect AfflictionModel types applied by a monster's move via the
// CardCmd.AfflictAndPreview<T> generic API. Same IL-scan strategy as
// PowerResolver / CardResolver — works for any monster that uses the
// standard command path.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace EnemyCycle;

public readonly struct AppliedAffliction
{
    public readonly Type AfflictionType;
    public readonly int? Amount;
    public AppliedAffliction(Type t, int? amount) { AfflictionType = t; Amount = amount; }
}

public static class AfflictionResolver
{
    private static readonly Dictionary<string, List<AppliedAffliction>> _cache = new();
    private static readonly Dictionary<Type, AfflictionModel?> _instanceCache = new();
    private static readonly FieldInfo? OnPerformField =
        typeof(MoveState).GetField("_onPerform",
            BindingFlags.Instance | BindingFlags.NonPublic);

    public static List<AppliedAffliction> Resolve(MonsterModel monster, MoveState mv)
    {
        if (monster == null || mv == null) return new List<AppliedAffliction>();
        var key = $"{monster.GetType().FullName}|{mv.StateId}";
        if (_cache.TryGetValue(key, out var cached)) return cached;
        List<AppliedAffliction> found;
        try { found = TryResolve(mv); }
        catch (Exception ex)
        {
            GD.PrintErr($"{EnemyCycleMod.LogPrefix}AfflictionResolver: {ex.Message}");
            found = new List<AppliedAffliction>();
        }
        _cache[key] = found;
        return found;
    }

    public static AfflictionModel? GetInstance(Type afflictionType)
    {
        if (afflictionType == null) return null;
        if (_instanceCache.TryGetValue(afflictionType, out var cached)) return cached;
        AfflictionModel? instance = null;
        try
        {
            var id = ModelDb.GetId(afflictionType);
            instance = ModelDb.GetByIdOrNull<AfflictionModel>(id);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{EnemyCycleMod.LogPrefix}fetch canonical affliction {afflictionType.Name}: {ex.Message}");
        }
        _instanceCache[afflictionType] = instance;
        return instance;
    }

    // Generic call to get the hover tips for an affliction with a
    // given amount. We have to use reflection to invoke
    // HoverTipFactory.FromAffliction<T>(amount) since T is dynamic.
    public static IEnumerable<IHoverTip>? GetHoverTips(Type afflictionType, int amount)
    {
        try
        {
            var method = typeof(HoverTipFactory)
                .GetMethod(nameof(HoverTipFactory.FromAffliction),
                    BindingFlags.Public | BindingFlags.Static);
            if (method == null) return null;
            var generic = method.MakeGenericMethod(afflictionType);
            return generic.Invoke(null, new object[] { amount }) as IEnumerable<IHoverTip>;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{EnemyCycleMod.LogPrefix}affliction hover {afflictionType.Name}: {ex.Message}");
            return null;
        }
    }

    private static List<AppliedAffliction> TryResolve(MoveState mv)
    {
        var result = new List<AppliedAffliction>();
        if (OnPerformField?.GetValue(mv) is not Delegate perform) return result;
        ScanIl(perform.Method, result);
        return result;
    }

    private static void ScanIl(MethodInfo method, List<AppliedAffliction> sink)
    {
        var asm = method.GetCustomAttribute<AsyncStateMachineAttribute>();
        if (asm?.StateMachineType is { } smType)
        {
            var moveNext = smType.GetMethod("MoveNext",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (moveNext != null && moveNext != method)
            {
                ScanIl(moveNext, sink);
                return;
            }
        }

        var body = method.GetMethodBody();
        if (body == null) return;
        var il = body.GetILAsByteArray();
        if (il == null || il.Length < 5) return;

        var module = method.Module;
        var typeArgs = (method.DeclaringType?.IsGenericType ?? false)
            ? method.DeclaringType.GetGenericArguments()
            : Type.EmptyTypes;
        var methodArgs = method.IsGenericMethod
            ? method.GetGenericArguments()
            : Type.EmptyTypes;

        for (int i = 0; i <= il.Length - 5; i++)
        {
            byte op = il[i];
            if (op != 0x28 && op != 0x6F) continue; // call / callvirt
            int token = BitConverter.ToInt32(il, i + 1);
            MethodBase? mb;
            try { mb = module.ResolveMethod(token, typeArgs, methodArgs); }
            catch { continue; }
            if (mb is not MethodInfo mi) continue;
            // Match CardCmd.Afflict<T> and CardCmd.AfflictAndPreview<T>.
            if (mi.Name != "Afflict" && mi.Name != "AfflictAndPreview") continue;
            if (mi.DeclaringType?.FullName != "MegaCrit.Sts2.Core.Commands.CardCmd") continue;
            if (!mi.IsGenericMethod) continue;
            var ga = mi.GetGenericArguments();
            if (ga.Length == 0) continue;
            int? amount = ScanIntBefore(il, i);
            sink.Add(new AppliedAffliction(ga[0], amount));
        }
    }

    // Same recent-ldc.i4 scan PowerResolver uses to recover the
    // numeric amount pushed onto the stack before the call.
    private static int? ScanIntBefore(byte[] il, int callPos)
    {
        const int window = 48;
        int start = Math.Max(0, callPos - window);
        for (int j = callPos - 1; j >= start; j--)
        {
            byte op = il[j];
            switch (op)
            {
                case 0x15: return -1;
                case 0x16: return 0;
                case 0x17: return 1;
                case 0x18: return 2;
                case 0x19: return 3;
                case 0x1A: return 4;
                case 0x1B: return 5;
                case 0x1C: return 6;
                case 0x1D: return 7;
                case 0x1E: return 8;
                case 0x1F:
                    if (j + 1 < il.Length) return (sbyte)il[j + 1];
                    break;
                case 0x20:
                    if (j + 4 < il.Length) return BitConverter.ToInt32(il, j + 1);
                    break;
            }
        }
        return null;
    }
}
