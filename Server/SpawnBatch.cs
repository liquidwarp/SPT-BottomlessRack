using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Enums;

namespace Liquidwarp.BottomlessRack.Server;

public sealed class SpawnRejected(string message, BackendErrorCodes code = BackendErrorCodes.None) : Exception(message)
{
    public BackendErrorCodes Code { get; } = code;
}

public sealed record SpawnBatch(SpawnReceipt Receipt, List<Item> Additions)
{
    public static SpawnBatch Stage(long sequence, SpawnLine[]? lines, UnlockLedger ledger, GearDefinitions gear,
        PmcData pmc, MongoId session, Action<List<Item>, PmcData, ItemEventRouterResponse> place)
    {
        if (sequence < 1 || lines is not { Length: > 0 })
            throw new SpawnRejected("Invalid spawn request.");
        var requests = new SortedDictionary<string, SpawnLine>(StringComparer.Ordinal);
        int total = 0;
        foreach (var line in lines)
        {
            if (line == null || string.IsNullOrEmpty(line.TemplateId) || !MongoId.IsValidMongoId(line.TemplateId) || line.Quantity <= 0 ||
                line.Quantity > int.MaxValue - total)
                throw new SpawnRejected("Invalid template or spawn quantity.");
            total += line.Quantity;
            if (!requests.TryGetValue(line.TemplateId, out var entry))
                requests.Add(line.TemplateId, entry = new() { TemplateId = line.TemplateId, BareWeapon = line.BareWeapon });
            if (entry.BareWeapon != line.BareWeapon)
                throw new SpawnRejected("Conflicting weapon builds in spawn request.");
            entry.Quantity += line.Quantity;
        }
        var canonical = requests.Values.ToArray();
        if (sequence == ledger.Receipt.Sequence && GearProtocol.SameRequest(canonical, ledger.Receipt.Lines))
            return new(ledger.Receipt, []);
        if (sequence - 1 != ledger.Receipt.Sequence)
            throw new SpawnRejected("Gear requests are out of sync. Check your stash before trying again.");
        if (canonical.Any(l => !ledger.Templates.Contains(l.TemplateId) || !gear.CanIssue(l.TemplateId) ||
            (l.BareWeapon && !gear.IsWeapon(l.TemplateId))))
            throw new SpawnRejected("This gear is locked, missing, or has no usable standard build.");

        if (pmc.Inventory?.Items == null || !pmc.Inventory.Stash.HasValue)
            throw new SpawnRejected("This profile has no main stash.");
        var staged = pmc with { Inventory = pmc.Inventory with { Items = [.. pmc.Inventory.Items] } };
        var additions = new List<Item>();
        var scratch = new ItemEventRouterResponse
        {
            Warnings = [], ProfileChanges = new() { [session] = new() { Items = new() { NewItems = additions } } }
        };
        // Grow only for successful placements. The stash grid bounds the number of copies.
        var roots = new List<string>();
        foreach (var line in canonical)
            for (int n = 0; n < line.Quantity; n++)
            {
                var tree = gear.Create(line.TemplateId, line.BareWeapon);
                var expectedCount = additions.Count + tree.Count;
                place(tree, staged, scratch);
                if (scratch.Warnings is { Count: > 0 } || additions.Count != expectedCount)
                    throw new SpawnRejected("Not enough stash space.", BackendErrorCodes.NotEnoughSpace);
                roots.Add(tree[0].Id.ToString());
            }
        return new(new() { Sequence = sequence, Lines = canonical, RootIds = roots.ToArray() }, additions);
    }
}
