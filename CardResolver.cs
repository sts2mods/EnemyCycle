// Resolve status / curse cards that a monster move adds to the
// player's deck/discard/hand. Same IL-scan pattern as PowerResolver,
// but targets `CardPileCmd.AddToCombatAndPreview<T>` (the generic API
// every status-spitting move uses).
//
// Caches results per (monster type, state id). Generic — works for any
// monster whose move calls AddToCombatAndPreview<T>.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace EnemyCycle;

public readonly struct AddedCard
{
    public readonly Type CardType;
    public readonly int? Count;
    public AddedCard(Type t, int? c) { CardType = t; Count = c; }
}

public static class CardResolver
{
    private static readonly Dictionary<string, List<AddedCard>> _cache = new();
    private static readonly Dictionary<Type, CardModel?> _cardInstanceCache = new();
    private static readonly FieldInfo? OnPerformField =
        typeof(MoveState).GetField("_onPerform",
            BindingFlags.Instance | BindingFlags.NonPublic);

    public static List<AddedCard> ResolveAddedCards(MonsterModel monster, MoveState mv)
    {
        if (monster == null || mv == null) return new List<AddedCard>();
        var key = $"{monster.GetType().FullName}|{mv.StateId}";
        if (_cache.TryGetValue(key, out var cached)) return cached;
        List<AddedCard> found;
        try { found = TryResolve(mv); }
        catch (Exception ex)
        {
            GD.PrintErr($"{EnemyCycleMod.LogPrefix}CardResolver: {ex.Message}");
            found = new List<AddedCard>();
        }
        _cache[key] = found;
        return found;
    }

    public static CardModel? GetCardInstance(Type cardType)
    {
        if (cardType == null) return null;
        if (_cardInstanceCache.TryGetValue(cardType, out var cached)) return cached;
        CardModel? instance = null;
        try
        {
            // Canonical card instance is registered in ModelDb at
            // startup; constructing one ourselves throws
            // DuplicateModelException.
            var id = MegaCrit.Sts2.Core.Models.ModelDb.GetId(cardType);
            instance = MegaCrit.Sts2.Core.Models.ModelDb.GetByIdOrNull<CardModel>(id);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{EnemyCycleMod.LogPrefix}fetch canonical card {cardType.Name}: {ex.Message}");
        }
        _cardInstanceCache[cardType] = instance;
        return instance;
    }

    public static Texture2D? GetCardPortrait(Type cardType)
    {
        var card = GetCardInstance(cardType);
        if (card == null) return null;
        try { return ResourceLoader.Load<Texture2D>(card.PortraitPath); }
        catch { return null; }
    }

    public static IHoverTip? GetHoverTipForCard(Type cardType)
    {
        var card = GetCardInstance(cardType);
        if (card == null) return null;
        try
        {
            // NHoverTipSet renders CardHoverTip as a full card preview.
            return new CardHoverTip(card);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"{EnemyCycleMod.LogPrefix}CardHoverTip {cardType.Name}: {ex.Message}");
            return null;
        }
    }

    private static List<AddedCard> TryResolve(MoveState mv)
    {
        var result = new List<AddedCard>();
        if (OnPerformField?.GetValue(mv) is not Delegate perform) return result;
        ScanIl(perform.Method, result);
        return result;
    }

    private static void ScanIl(MethodInfo method, List<AddedCard> sink)
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
            if (mi.Name != "AddToCombatAndPreview") continue;
            if (mi.DeclaringType?.FullName != "MegaCrit.Sts2.Core.Commands.CardPileCmd") continue;
            if (!mi.IsGenericMethod) continue;
            var ga = mi.GetGenericArguments();
            if (ga.Length == 0) continue;
            int? count = ScanIntBefore(il, i);
            sink.Add(new AddedCard(ga[0], count));
        }
    }

    // Most-recent ldc.i4* before the call — for AddToCombatAndPreview
    // signature `(targets/target, pileType, count, creator, position)`,
    // the literal int count is pushed shortly before the call.
    private static int? ScanIntBefore(byte[] il, int callPos)
    {
        const int window = 48;
        int start = Math.Max(0, callPos - window);
        for (int j = callPos - 1; j >= start; j--)
        {
            byte op = il[j];
            switch (op)
            {
                case 0x15: return -1;                     // ldc.i4.m1
                case 0x16: return 0;
                case 0x17: return 1;
                case 0x18: return 2;
                case 0x19: return 3;
                case 0x1A: return 4;
                case 0x1B: return 5;
                case 0x1C: return 6;
                case 0x1D: return 7;
                case 0x1E: return 8;
                case 0x1F:                                 // ldc.i4.s int8
                    if (j + 1 < il.Length) return (sbyte)il[j + 1];
                    break;
                case 0x20:                                 // ldc.i4 int32
                    if (j + 4 < il.Length) return BitConverter.ToInt32(il, j + 1);
                    break;
            }
        }
        return null;
    }
}
