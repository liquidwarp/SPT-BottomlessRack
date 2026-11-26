using HarmonyLib;
using System.Runtime.CompilerServices;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Callbacks;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.DI.Routing;
using SPTarkov.Server.Core.Helpers.Traders;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Services.InRaid;

namespace Liquidwarp.UnlockGearProgression.Server;

[Injectable(TypePriority = OnLoadOrder.Preload + 1)]
public sealed class Mod(GearService service, ISptLogger<Mod> logger) : IOnLoad
{
    internal static GearService Gear { get; private set; } = null!;
    private static ISptLogger<Mod> Log = null!;

    public Task OnLoadAsync(CancellationToken ct = default)
    {
        Gear = service;
        Log = logger;
        new Harmony(MyModInfo.MOD_GUID).PatchAll(typeof(Mod).Assembly);
        return Task.CompletedTask;
    }

    // A malformed third-party assort or a future catalogue schema must not interrupt a vanilla action.
    internal static void Observe(Action action)
    {
        try { action(); }
        catch (Exception ex) { Log.Error($"Gear discovery failed: {ex}"); }
    }

    internal static async Task ObserveRaid(MongoId id, ExitStatus? result, MongoId[] carried)
    {
        try { await Gear.FinishRaid(id, result, carried); }
        catch (Exception ex) { Log.Error($"Could not record gear after raid: {ex}"); }
    }

    internal static void Locked(MongoId id, Action action)
    {
        var gate = Gear.Gate(id);
        gate.Wait();
        try { action(); }
        finally { gate.Release(); }
    }

    internal static async ValueTask<string> Finish(ValueTask<string> pending, SemaphoreSlim gate, Action? accepted = null)
    {
        try
        {
            var result = await pending;
            accepted?.Invoke();
            return result;
        }
        finally { gate.Release(); }
    }
}

[HarmonyPatch(typeof(GameController), nameof(GameController.GameStart))]
internal static class LoginPatch
{
    private static void Postfix(MongoId sessionId) => Mod.Locked(sessionId, () => Mod.Gear.ResetRaid(sessionId));
}

[HarmonyPatch(typeof(ProfileCallbacks), nameof(ProfileCallbacks.GetProfileData))]
internal static class ProfilePatch
{
    private static void Postfix(MongoId sessionID) =>
        Mod.Locked(sessionID, () => Mod.Observe(() => Mod.Gear.Observe(sessionID, true)));
}

[HarmonyPatch(typeof(TraderAssortHelper), nameof(TraderAssortHelper.ResetExpiredTrader))]
internal static class RestockPatch
{
    private static void Postfix(Trader trader) => Mod.Observe(() => Mod.Gear.Restock(trader.Base.Id));
}

[HarmonyPatch(typeof(ItemEventCallbacks), nameof(ItemEventCallbacks.HandleEvents))]
internal static class InventoryBatchPatch
{
    private static void Prefix(MongoId sessionID, out SemaphoreSlim __state)
    {
        __state = Mod.Gear.Gate(sessionID);
        __state.Wait();
    }

    private static void Postfix(ref ValueTask<string> __result, SemaphoreSlim __state) =>
        __result = Mod.Finish(__result, __state);
}

[HarmonyPatch(typeof(ItemEventRouter), nameof(ItemEventRouter.HandleItemEvent))]
internal static class InventoryActionPatch
{
    // SPT shares one output across a request, then replaces it. EFT associates a warning with
    // the command at its index, including any queued moves ahead of an immediate spawn command.
    private sealed class Position { public int Next; }
    private static readonly ConditionalWeakTable<ItemEventRouterResponse, Position> Positions = new();
    private static void Prefix(MongoId sessionID, ItemEventRouterResponse output, out int __state)
    {
        __state = Positions.GetOrCreateValue(output).Next++;
        Mod.Observe(() => Mod.Gear.Observe(sessionID));
    }

    private static void Postfix(string url, MongoId sessionID, int __state, ref ValueTask<ItemEventRouterResponse> __result) =>
        __result = After(__result, sessionID, url, __state);

    private static async ValueTask<ItemEventRouterResponse> After(ValueTask<ItemEventRouterResponse> pending, MongoId id, string action, int index)
    {
        var result = await pending;
        if (result.Warnings is not { Count: > 0 })
            Mod.Observe(() => Mod.Gear.Observe(id, action.StartsWith("Quest", StringComparison.Ordinal) || action == "TradingConfirm"));
        else if (action == GearProtocol.Action)
            result.Warnings[^1].Index = index;
        return result;
    }
}

[HarmonyPatch(typeof(MatchCallbacks), nameof(MatchCallbacks.StartLocalRaidAsync))]
internal static class StartRaidPatch
{
    private static void Prefix(MongoId sessionID, out SemaphoreSlim __state)
    {
        __state = Mod.Gear.Gate(sessionID);
        __state.Wait();
    }
    private static void Postfix(MongoId sessionID, SemaphoreSlim __state, ref ValueTask<string> __result) =>
        __result = Mod.Finish(__result, __state, () => Mod.Gear.StartRaid(sessionID));
}

[HarmonyPatch(typeof(LocationLifecycleService), nameof(LocationLifecycleService.EndLocalRaidAsync))]
internal static class EndRaidPatch
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix(MongoId sessionId, EndLocalRaidRequestData request, out (SemaphoreSlim Gate, MongoId[] Carried) __state)
    {
        // Snapshot before SPT regenerates the player Scav. Publish only after the stock handler accepts the exit.
        var carried = request.Results?.Result is ExitStatus.SURVIVED or ExitStatus.RUNNER
            ? Discovery.Owned(request.Results.Profile?.Inventory, carriedOnly: true).ToArray() : [];
        var gate = Mod.Gear.Gate(sessionId);
        gate.Wait();
        __state = (gate, carried);
    }

    private static void Postfix(MongoId sessionId, EndLocalRaidRequestData request, bool __runOriginal,
        (SemaphoreSlim Gate, MongoId[] Carried) __state, ref Task __result)
    {
        if (__state.Gate != null)
            __result = After(__result, sessionId, __runOriginal, request.Results?.Result, __state);
    }

    private static async Task After(Task pending, MongoId id, bool accepted, ExitStatus? result, (SemaphoreSlim Gate, MongoId[] Carried) state)
    {
        try
        {
            await pending;
            // Fika skips the stock end-raid handler for spectators.
            if (accepted) await Mod.ObserveRaid(id, result, state.Carried);
            else Mod.Gear.ResetRaid(id);
        }
        finally { state.Gate.Release(); }
    }
}
