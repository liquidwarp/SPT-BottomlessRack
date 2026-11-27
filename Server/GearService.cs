using System.Collections.Concurrent;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Request;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Eft.Ragfair;
using SPTarkov.Server.Core.Models.Eft.Trade;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;

namespace Liquidwarp.BottomlessRack.Server;

[Injectable(InjectionType.Singleton)]
public sealed class GearService(ProfileHelper profiles, GearDefinitions gear, InventoryHelper inventory, SaveServer saves,
    HttpResponseUtil http)
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

    private UnlockLedger ReadLedger(MongoId id, SptProfile profile)
    {
        var ledger = UnlockLedger.Read(profile, out var restored);
        if (restored)
        {
            // A receipt read can be the first access to an old profile after a server restart.
            ledger = ledger.ReconcileWeaponParts(gear);
            StoreLedger(id, profile, null, ledger);
        }
        return ledger;
    }

    private void StoreLedger(MongoId id, SptProfile profile, UnlockLedger? previous, UnlockLedger current)
    {
        if (ReferenceEquals(previous, current)) return;
        var gate = SaveLock(id);
        gate.Wait();
        try { current.Write(profile); }
        finally { gate.Release(); }
    }

    public void Observe(MongoId id)
    {
        if (InRaid(id)) return;
        var profile = profiles.GetFullProfile(id);
        if (profile?.CharacterData?.PmcData is not { Inventory.Items: not null } pmc) return;
        var original = ReadLedger(id, profile);
        StoreLedger(id, profile, original, original.Discover(Discovery.Owned(pmc.Inventory), gear));
    }

    public async Task FinishRaid(MongoId id, ExitStatus? result, MongoId[] carried)
    {
        if (result == ExitStatus.TRANSIT) return;
        ResetRaid(id);
        var profile = profiles.GetFullProfile(id);
        var previous = ReadLedger(id, profile);
        if (result is ExitStatus.SURVIVED or ExitStatus.RUNNER)
        {
            var next = previous.Discover(carried, gear);
            StoreLedger(id, profile, previous, next);
        }
        Observe(id);
        if (ReadLedger(id, profile).Templates.Count != previous.Templates.Count)
            await saves.SaveProfileAsync(id);
    }

    public ValueTask<Catalogue> Read(MongoId id, bool reconcile, CancellationToken ct) =>
        Read(id, reconcile, null, ct);

    internal async ValueTask<Catalogue> Read(MongoId id, bool reconcile, int? knownRevision, CancellationToken ct)
    {
        var gate = Gate(id);
        await gate.WaitAsync(ct);
        try
        {
            if (_pendingSaves.ContainsKey(id)) await SaveGrant(id);
            // An open selector also notices direct stash grants made outside the inventory queue.
            Observe(id);
            var profile = profiles.GetFullProfile(id);
            var state = ReadLedger(id, profile);
            if (reconcile && !knownRevision.HasValue)
            {
                var updated = state.ReconcileWeaponParts(gear);
                StoreLedger(id, profile, state, updated);
                state = updated;
            }
            var catalogue = new Catalogue { Revision = state.Templates.Count, Receipt = state.Receipt };
            if (!reconcile || knownRevision == catalogue.Revision) return catalogue;
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
            var ledger = ReadLedger(id, profile);
            var batch = SpawnBatch.Stage(request.Sequence, request.Lines, ledger, gear, inventory, pmc, id);
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
