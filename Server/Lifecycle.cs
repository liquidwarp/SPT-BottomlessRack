using HarmonyLib;
using System.Runtime.CompilerServices;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Callbacks;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.DI.Routing;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Request;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;

namespace Liquidwarp.BottomlessRack.Server;

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

    // A discovery failure must not interrupt a vanilla action.
    internal static void Observe(MongoId id)
    {
        try { Gear.Observe(id); }
        catch (Exception ex) { Log.Error($"Gear discovery failed: {ex}"); }
    }
}

[HarmonyPatch(typeof(ProfileCallbacks), nameof(ProfileCallbacks.GetProfileData))]
internal static class ProfilePatch
{
    private static void Postfix(MongoId sessionID)
    {
        var gate = Mod.Gear.Gate(sessionID);
        gate.Wait();
        try { Mod.Observe(sessionID); }
        finally { gate.Release(); }
    }
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
        __result = Finish(__result, __state);

    private static async ValueTask<string> Finish(ValueTask<string> pending, SemaphoreSlim gate)
    {
        try { return await pending; }
        finally { gate.Release(); }
    }
}

[HarmonyPatch(typeof(ItemEventRouter), nameof(ItemEventRouter.HandleItemEvent))]
internal static class InventoryActionPatch
{
    // SPT shares one output across a request, then replaces it. EFT associates a warning with
    // the command at its index, including any queued moves ahead of an immediate spawn command.
    private sealed class Position { public int Next; }
    private static readonly ConditionalWeakTable<ItemEventRouterResponse, Position> Positions = new();
    private static bool Prefix(string url, PmcData pmcData, BaseInteractionRequestData body, MongoId sessionID,
        ItemEventRouterResponse output, out int __state, ref ValueTask<ItemEventRouterResponse> __result)
    {
        __state = Positions.GetOrCreateValue(output).Next++;
        Mod.Observe(sessionID);
        if (!Mod.Gear.RejectSale(url, body, pmcData, output, __state)) return true;
        __result = ValueTask.FromResult(output);
        return false;
    }

    private static void Postfix(string url, MongoId sessionID, int __state, ref ValueTask<ItemEventRouterResponse> __result) =>
        __result = After(__result, sessionID, url, __state);

    private static async ValueTask<ItemEventRouterResponse> After(ValueTask<ItemEventRouterResponse> pending, MongoId id, string action, int index)
    {
        var result = await pending;
        if (result.Warnings is not { Count: > 0 })
            Mod.Observe(id);
        else if (action == GearProtocol.Action)
            result.Warnings[^1].Index = index;
        return result;
    }
}
