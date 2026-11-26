using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Enums.Hideout;

namespace Liquidwarp.UnlockGearProgression.Server;

public static class Discovery
{
    public static IEnumerable<MongoId> Owned(BotBaseInventory? inventory, bool carriedOnly = false)
    {
        if (inventory?.Items == null) return [];
        var roots = new HashSet<string>();
        if (inventory.Equipment is { } equipment) roots.Add(equipment.ToString());
        if (!carriedOnly)
        {
            if (inventory.Stash is { } stash) roots.Add(stash.ToString());
            if (inventory.SortingTable is { } sorting) roots.Add(sorting.ToString());
            foreach (var (area, root) in inventory.HideoutAreaStashes ?? [])
                if (int.TryParse(area, out var number) && (HideoutAreas)number is
                    HideoutAreas.WeaponStand or HideoutAreas.WeaponStandSecondary or HideoutAreas.EquipmentPresetsStand or HideoutAreas.PlaceOfFame)
                    roots.Add(root.ToString());
        }
        return Descendants(inventory.Items, roots);
    }

    public static bool Accessible(MongoId id, Trader trader, Dictionary<MongoId, TraderInfo>? relations) =>
        id != Traders.FENCE && trader.Assort?.Items?.Count > 0 && relations != null &&
        relations.TryGetValue(id, out var info) && info.Disabled != true && (info.Unlocked ?? trader.Base.UnlockedByDefault) == true;

    public static IEnumerable<MongoId> Offered(TraderAssort assort)
    {
        var roots = new HashSet<string>();
        foreach (var item in assort.Items)
            if (item.ParentId == "hideout" && assort.BarterScheme?.ContainsKey(item.Id) == true)
            {
                roots.Add(item.Id.ToString());
                yield return item.Template;
            }
        foreach (var id in Descendants(assort.Items, roots)) yield return id;
    }

    private static IEnumerable<MongoId> Descendants(List<Item> items, HashSet<string> roots)
    {
        var children = items.ToLookup(i => i.ParentId);
        var pending = new Stack<string>(roots);
        while (pending.TryPop(out var parent))
            foreach (var item in children[parent])
                if (roots.Add(item.Id.ToString()))
                {
                    yield return item.Template;
                    pending.Push(item.Id.ToString());
                }
    }
}
