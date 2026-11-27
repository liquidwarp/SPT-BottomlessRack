using System.Collections.Concurrent;
using HarmonyLib;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Helpers.Traders;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Request;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Eft.Ragfair;
using SPTarkov.Server.Core.Models.Eft.Trade;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.Profile;
using SPTarkov.Server.Core.Utils;

namespace Liquidwarp.BottomlessRack.Server;

[Injectable(InjectionType.Singleton)]
public sealed class GearService(ProfileHelper profiles, GearDefinitions gear, TradersTable traders, TraderAssortHelper assorts,
    InventoryHelper inventory, SaveServer saves, HttpResponseUtil http, ProfileActivityService activity, ISptLogger<GearService> log)
{
    private readonly ConcurrentDictionary<MongoId, SemaphoreSlim> _gates = new();
    private readonly ConcurrentDictionary<MongoId, byte> _inRaid = new();
    private readonly ConcurrentDictionary<MongoId, byte> _pendingSaves = new();
    // Use SPT's per-profile save lock during publication so autosave cannot see half a grant.
    private static readonly AccessTools.FieldRef<SaveServer, ConcurrentDictionary<MongoId, SemaphoreSlim>> SaveLocks =
        AccessTools.FieldRefAccess<SaveServer, ConcurrentDictionary<MongoId, SemaphoreSlim>>("saveLocks");
    private static readonly AccessTools.FieldRef<SaveServer, ConcurrentDictionary<MongoId, string>> SaveHashes =
        AccessTools.FieldRefAccess<SaveServer, ConcurrentDictionary<MongoId, string>>("saveMd5");

    public SemaphoreSlim Gate(MongoId id) => _gates.GetOrAdd(id, _ => new(1, 1));
    public bool InRaid(MongoId id) => _inRaid.ContainsKey(id);
    public void StartRaid(MongoId id) => _inRaid[id] = 1;
    public void ResetRaid(MongoId id) => _inRaid.TryRemove(id, out _);

    private SemaphoreSlim SaveLock(MongoId id) => SaveLocks(saves).GetOrAdd(id, _ => new(1, 1));

    internal bool RejectSale(string action, BaseInteractionRequestData body, PmcData pmc, ItemEventRouterResponse output, int index)
    {
        IEnumerable<MongoId>? offered;
        switch (action)
        {
            case ItemEventActions.TRADING_CONFIRM when body is ProcessSellTradeRequestData { Type: ItemEventActions.SELL_TO_TRADER } sale:
                offered = sale.Items?.Select(line => line?.Id ?? default);
                break;
            case ItemEventActions.RAGFAIR_ADD_OFFER when body is AddOfferRequestData offer:
                offered = offer.Items;
                break;
            default:
                return false;
        }
        var error = CheckSale(pmc.Inventory?.Items, offered);
        if (error == null) return false;
        http.AppendErrorToOutput(output, error);
        output.Warnings![^1].Index = index;
        return true;
    }

    private string? CheckSale(List<Item>? inventoryItems, IEnumerable<MongoId>? offered)
    {
        const string missing = "Could not find every item offered for sale in your inventory.";
        var pending = offered?.ToHashSet();
        if (inventoryItems == null || pending is not { Count: > 0 }) return missing;
        var roots = new HashSet<string>();
        foreach (var item in inventoryItems)
        {
            if (!pending.Remove(item.Id)) continue;
            if (gear.Eligible(item.Template)) return GearCategories.SaleWarning;
            roots.Add(item.Id.ToString());
            if (pending.Count == 0) break;
        }
        if (pending.Count != 0) return missing;
        return Discovery.Descendants(inventoryItems, roots).Any(gear.Eligible) ? GearCategories.SaleWarning : null;
    }

    private void StoreDiscovery(MongoId id, SptProfile profile, UnlockLedger previous, UnlockLedger current)
    {
        if (ReferenceEquals(previous, current)) return;
        var gate = SaveLock(id);
        gate.Wait();
        try { current.Write(profile); }
        finally { gate.Release(); }
    }

    public void Observe(MongoId id, bool checkTraders = false, MongoId? trader = null)
    {
        if (InRaid(id) && trader == null) return;
        var profile = profiles.GetFullProfile(id);
        if (profile?.CharacterData?.PmcData is not { Inventory.Items: not null } pmc) return;
        var original = UnlockLedger.Read(profile);
        var ledger = trader.HasValue ? original : original.Discover(Discovery.Owned(pmc.Inventory), gear);
        if (checkTraders)
        {
            foreach (var pair in traders)
                if ((!trader.HasValue || trader == pair.Key) && Discovery.Accessible(pair.Key, pair.Value, pmc.TradersInfo))
                {
                    try { ledger = ledger.Discover(Discovery.Offered(assorts.GetAssort(id, pair.Key, false)), gear); }
                    catch (Exception ex) { log.Warning($"Unable to read gear offers from trader {pair.Key}: {ex.Message}"); }
                }
        }
        StoreDiscovery(id, profile, original, ledger);
    }

    public void Restock(MongoId trader)
    {
        if (trader == Traders.FENCE) return;
        foreach (var active in activity.GetActiveProfileIdsWithinMinutes(5))
        {
            MongoId id = active;
            var gate = Gate(id);
            gate.Wait();
            try { Observe(id, true, trader); }
            finally { gate.Release(); }
        }
    }

    public async Task FinishRaid(MongoId id, ExitStatus? result, MongoId[] carried)
    {
        if (result == ExitStatus.TRANSIT) return;
        ResetRaid(id);
        var profile = profiles.GetFullProfile(id);
        var previous = UnlockLedger.Read(profile);
        if (result is ExitStatus.SURVIVED or ExitStatus.RUNNER)
        {
            var next = previous.Discover(carried, gear);
            StoreDiscovery(id, profile, previous, next);
        }
        Observe(id, true);
        if (UnlockLedger.Read(profile).Templates.Count != previous.Templates.Count)
            await saves.SaveProfileAsync(id);
    }

    public async ValueTask<Catalogue> Read(MongoId id, bool reconcile, CancellationToken ct)
    {
        var gate = Gate(id);
        await gate.WaitAsync(ct);
        try
        {
            if (_pendingSaves.ContainsKey(id)) await SaveGrant(id);
            // An open selector also notices direct stash grants made outside the inventory queue.
            Observe(id, checkTraders: reconcile);
            var profile = profiles.GetFullProfile(id);
            var state = UnlockLedger.Read(profile);
            if (reconcile)
            {
                var updated = state.ReconcileWeaponParts(gear);
                StoreDiscovery(id, profile, state, updated);
                state = updated;
            }
            var catalogue = new Catalogue { Revision = state.Templates.Count, Receipt = state.Receipt };
            if (!reconcile) return catalogue;
            catalogue.Templates = state.Templates.Where(t => MongoId.IsValidMongoId(t) && gear.CanIssue(t))
                .Order(StringComparer.Ordinal).ToArray();
            foreach (var tpl in catalogue.Templates)
                if (gear.IsWeapon(tpl))
                    catalogue.WeaponBuilds.Add(tpl, gear.Recipe(tpl).Select(p => new WeaponBuildPart
                    {
                        TemplateId = p.Template.ToString(), ParentIndex = p.ParentIndex, SlotId = p.Slot
                    }).ToArray());
            return catalogue;
        }
        finally { gate.Release(); }
    }

    // Invoked inside EFT's serialized item-event batch. The client never supplies a profile or an item ID.
    public async ValueTask<ItemEventRouterResponse> Spawn(MongoId id, PmcData pmc, SpawnRequest request,
        ItemEventRouterResponse output, CancellationToken ct)
    {
        try
        {
            if (InRaid(id)) throw new SpawnRejected("Gear can only be spawned out of raid.");
            if (saves.IsProfileInvalidOrUnloadable(id)) throw new SpawnRejected("Unable to save this profile.");
            var profile = profiles.GetFullProfile(id);
            var ledger = UnlockLedger.Read(profile);
            var batch = SpawnBatch.Stage(request.Sequence, request.Lines, ledger, gear, pmc, id,
                (tree, staged, scratch) => inventory.AddItemToStash(id,
                    new() { ItemWithModsToAdd = tree, FoundInRaid = false, UseSortingTable = false }, staged, scratch));
            if (batch.Additions.Count > 0)
            {
                var saveLock = SaveLock(id);
                await saveLock.WaitAsync(ct);
                try
                {
                    pmc.Inventory!.Items!.AddRange(batch.Additions);
                    (ledger with { Receipt = batch.Receipt }).Write(profile);
                    _pendingSaves[id] = 1;
                }
                finally { saveLock.Release(); }
            }
            // Once issued, finish saving even if the caller disconnected.
            await SaveGrant(id);
            output.ProfileChanges[id].Items!.NewItems!.AddRange(batch.Additions);
            return output;
        }
        catch (SpawnRejected error) { return http.AppendErrorToOutput(output, error.Message, error.Code); }
    }

    private async Task SaveGrant(MongoId id)
    {
        try
        {
            await saves.SaveProfileAsync(id);
            _pendingSaves.TryRemove(id, out _);
        }
        catch
        {
            // SPT caches the hash before its disk write. A read or retry must try that same snapshot again.
            SaveHashes(saves).TryRemove(id, out _);
            throw;
        }
    }
}
