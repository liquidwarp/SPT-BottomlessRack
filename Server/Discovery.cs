using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums.Hideout;

namespace Liquidwarp.BottomlessRack.Server;

public static class Discovery
{
    public static IEnumerable<MongoId> Owned(BotBaseInventory? inventory)
    {
        if (inventory?.Items == null) return [];
        var roots = new HashSet<string>();
        if (inventory.Equipment is { } equipment) roots.Add(equipment.ToString());
        if (inventory.Stash is { } stash) roots.Add(stash.ToString());
        if (inventory.SortingTable is { } sorting) roots.Add(sorting.ToString());
        foreach (var (area, root) in inventory.HideoutAreaStashes ?? [])
            if (int.TryParse(area, out var number) && (HideoutAreas)number is
                HideoutAreas.WeaponStand or HideoutAreas.WeaponStandSecondary or HideoutAreas.EquipmentPresetsStand or HideoutAreas.PlaceOfFame)
                roots.Add(root.ToString());
        return Descendants(inventory.Items, roots);
    }

    internal static IEnumerable<MongoId> Descendants(List<Item> items, HashSet<string> roots)
    {
        var children = items.ToLookup(i => i.ParentId);
        var pending = new Stack<string>(roots);
        while (pending.TryPop(out var parent))
            foreach (var item in children[parent])
            {
                var id = item.Id.ToString();
                if (!roots.Add(id)) continue;
                yield return item.Template;
                pending.Push(id);
            }
    }
}
