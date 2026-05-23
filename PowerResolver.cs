// Figure out which PowerModel(s) a monster move applies, and how many
// stacks, by IL-scanning the move's perform delegate. Monster moves are
// async, so the real PowerCmd.Apply<T>(...) call lives in the compiler-
// generated state machine's MoveNext, not the surface method.
//
// Amount detection: when the amount literal in source is an integer
// (`3m`, `-1m`, `2m`), the compiler emits `ldc.i4.X ; newobj
// System.Decimal::.ctor(int32)` right before the Apply args finish
// pushing. We scan a small window backwards from each Apply call site
// for a Decimal-ctor `newobj`, then for the most recent `ldc.i4` before
// that — that's the amount. Anything more exotic (decimal expressions,
// computed values) just won't have an amount detected; we still report
// the power type.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace EnemyCycle;

public readonly struct AppliedPower
{
    public readonly Type PowerType;
    public readonly int? Amount; // null = unknown / non-literal
    public AppliedPower(Type t, int? a) { PowerType = t; Amount = a; }
}

public static class PowerResolver
{
    private static readonly Dictionary<string, List<AppliedPower>> _cache = new();
    private static readonly Dictionary<Type, PowerModel?> _powerInstanceCache = new();
    private static readonly FieldInfo? OnPerformField =
        typeof(MoveState).GetField("_onPerform",
            BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? PowerAmountField =
        typeof(PowerModel).GetField("_amount",
            BindingFlags.Instance | BindingFlags.NonPublic);

    public static List<AppliedPower> ResolveAppliedPowers(MonsterModel monster, MoveState mv)
    {
        if (monster == null || mv == null) return new List<AppliedPower>();
        var key = $"{monster.GetType().FullName}|{mv.StateId}";
        if (_cache.TryGetValue(key, out var cached)) return cached;
        List<AppliedPower> found;
        try { found = TryResolve(mv); }
        catch (Exception ex)
        {
            GD.PrintErr($"{EnemyCycleMod.LogPrefix}PowerResolver: {ex.Message}");
            found = new List<AppliedPower>();
        }
        _cache[key] = found;
        return found;
    }

    public static PowerModel? GetPowerInstance(Type powerType)
    {
        if (powerType == null) return null;
        if (_powerInstanceCache.TryGetValue(powerType, out var cached)) return cached;
        PowerModel? instance = null;
        try
        {
            // Game registers one canonical PowerModel per type in
            // ModelDb at startup; constructing a fresh one ourselves
            // would throw DuplicateModelException. Fetch the canonical
            // and reuse it. The canonical is shared so we must NOT
            // mutate its _amount.
            instance = MegaCrit.Sts2.Core.Models.ModelDb.DebugPower(powerType);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{EnemyCycleMod.LogPrefix}fetch canonical {powerType.Name}: {ex.Message}");
        }
        _powerInstanceCache[powerType] = instance;
        return instance;
    }

    public static IHoverTip? GetHoverTipForPower(Type powerType, int? amount)
    {
        var instance = GetPowerInstance(powerType);
        if (instance == null) return null;
        try
        {
            // Canonical instance is shared — don't mutate _amount.
            // Dumb description rarely references {Amount} anyway, and
            // the chip itself shows the stack count visually.
            _ = amount;
            return instance.DumbHoverTip;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{EnemyCycleMod.LogPrefix}DumbHoverTip {powerType.Name}: {ex.Message}");
            return null;
        }
    }

    public static Texture2D? GetPowerIcon(Type powerType)
    {
        var instance = GetPowerInstance(powerType);
        try { return instance?.Icon; }
        catch { return null; }
    }

    private static List<AppliedPower> TryResolve(MoveState mv)
    {
        var result = new List<AppliedPower>();
        if (OnPerformField?.GetValue(mv) is not Delegate perform) return result;
        ScanIl(perform.Method, result);
        return result;
    }

    private static void ScanIl(MethodInfo method, List<AppliedPower> sink)
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

        // Track unique power types referenced via ModelDb.Power<T>()
        // — some monsters (ThievingHopper, Bygone Effigy) build the
        // PowerModel from ModelDb and then call the *non-generic*
        // PowerCmd.Apply overload, which loses the type at the call
        // site. If we see a non-generic Apply later in the same
        // method, attribute the Power<T> references to that Apply so
        // the chip still shows up in the preview.
        bool sawNonGenericApply = false;
        var referencedPowerTypes = new List<Type>();
        var alreadySeen = new HashSet<Type>();

        for (int i = 0; i <= il.Length - 5; i++)
        {
            byte op = il[i];
            if (op != 0x28 && op != 0x6F) continue; // call / callvirt
            int token = BitConverter.ToInt32(il, i + 1);
            MethodBase? mb;
            try { mb = module.ResolveMethod(token, typeArgs, methodArgs); }
            catch { continue; }
            if (mb is not MethodInfo mi) continue;

            // PowerCmd.Apply<T>(...) — the simple/common case.
            if (mi.Name == "Apply" &&
                mi.DeclaringType?.FullName == "MegaCrit.Sts2.Core.Commands.PowerCmd")
            {
                if (mi.IsGenericMethod)
                {
                    var ga = mi.GetGenericArguments();
                    if (ga.Length > 0)
                    {
                        int? amount = ScanAmountBefore(il, i, module, typeArgs, methodArgs);
                        sink.Add(new AppliedPower(ga[0], amount));
                        alreadySeen.Add(ga[0]);
                    }
                }
                else
                {
                    sawNonGenericApply = true;
                }
                continue;
            }

            // ModelDb.Power<T>() — record T as a candidate; promoted
            // to "applied" if a non-generic Apply also appears.
            if (mi.Name == "Power" &&
                mi.DeclaringType?.FullName == "MegaCrit.Sts2.Core.Models.ModelDb" &&
                mi.IsGenericMethod)
            {
                var ga = mi.GetGenericArguments();
                if (ga.Length > 0 && !alreadySeen.Contains(ga[0]))
                    referencedPowerTypes.Add(ga[0]);
            }
        }

        if (sawNonGenericApply)
        {
            foreach (var t in referencedPowerTypes)
            {
                if (alreadySeen.Add(t))
                    sink.Add(new AppliedPower(t, null));
            }
        }
    }

    // Walk back up to `window` bytes from `callPos` looking for the
    // decimal newobj that wraps the amount literal, then take the most
    // recent ldc.i4* before that newobj.
    private static int? ScanAmountBefore(byte[] il, int callPos, Module module,
                                         Type[] typeArgs, Type[] methodArgs)
    {
        const int window = 96;
        int start = Math.Max(0, callPos - window);

        int? newobjPos = null;
        for (int j = callPos - 1; j >= start; j--)
        {
            if (il[j] != 0x73) continue; // newobj
            if (j + 5 > il.Length) continue;
            int token = BitConverter.ToInt32(il, j + 1);
            MethodBase? mb;
            try { mb = module.ResolveMethod(token, typeArgs, methodArgs); }
            catch { continue; }
            if (mb?.DeclaringType?.FullName != "System.Decimal") continue;
            var pars = mb.GetParameters();
            if (pars.Length != 1) continue;
            if (pars[0].ParameterType != typeof(int)) continue;
            newobjPos = j;
            break;
        }
        if (!newobjPos.HasValue) return null;

        // Look backwards from the newobj for ldc.i4*.
        int wstart = Math.Max(0, newobjPos.Value - 24);
        for (int j = newobjPos.Value - 1; j >= wstart; j--)
        {
            byte op = il[j];
            switch (op)
            {
                case 0x15: return -1;                    // ldc.i4.m1
                case 0x16: return 0;
                case 0x17: return 1;
                case 0x18: return 2;
                case 0x19: return 3;
                case 0x1A: return 4;
                case 0x1B: return 5;
                case 0x1C: return 6;
                case 0x1D: return 7;
                case 0x1E: return 8;
                case 0x1F:                                // ldc.i4.s int8
                    if (j + 1 < il.Length) return (sbyte)il[j + 1];
                    break;
                case 0x20:                                // ldc.i4 int32
                    if (j + 4 < il.Length) return BitConverter.ToInt32(il, j + 1);
                    break;
            }
        }
        return null;
    }
}
