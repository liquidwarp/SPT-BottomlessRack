using System;
using System.Collections.Generic;
using Diz.LanguageExtensions;
using EFT.InventoryLogic;
using HarmonyLib;

namespace Liquidwarp.BottomlessRack.Client;

internal static class PresetItems
{
    // Only set around synchronous EFT planning. A receipt assigns each new body to one preset item.
    internal static Dictionary<Item, string> NewBodies;

    // "Equip available" can omit a build whose ammunition or other contents still need buying.
    // Supplying its receiver or its loose parts would create another incomplete copy on every retry.
    internal static List<Item> Supplies(InventoryEquipment plan, List<Item> missing)
    {
        if (missing.Count == 0) return missing;
        var entries = new Dictionary<string, Item>();
        foreach (var item in missing)
            if (plan.TryFindItem(item.Id, out var entry)) entries[item.Id] = entry;
        var incomplete = new HashSet<string>();
        foreach (var item in entries.Values)
        {
            if (GearClient.Unlocked.Contains(item.StringTemplateId)) continue;
            for (var entry = item; entry != null && entry != plan; entry = entry.CurrentAddress?.Container.ParentItem)
                if (entries.ContainsKey(entry.Id)) incomplete.Add(entry.Id);
        }
        if (incomplete.Count == 0) return missing;
        var supplies = new List<Item>();
        foreach (var item in missing)
        {
            if (!entries.TryGetValue(item.Id, out var entry)) continue;
            for (; entry != null && entry != plan; entry = entry.CurrentAddress?.Container.ParentItem)
                if (incomplete.Contains(entry.Id)) break;
            if (entry == plan) supplies.Add(item);
        }
        return supplies;
    }

    internal static Dictionary<Item, string> AssignBodies(List<Item> missing, List<Item> roots)
    {
        var byTemplate = new Dictionary<string, Queue<Item>>();
        foreach (var root in roots)
        {
            if (!(root is ContainerCollection) || root.StackMaxSize != 1) continue;
            if (!byTemplate.TryGetValue(root.StringTemplateId, out var copies))
                byTemplate.Add(root.StringTemplateId, copies = new Queue<Item>());
            copies.Enqueue(root);
        }
        var assigned = new Dictionary<Item, string>();
        foreach (var item in missing)
            if (byTemplate.TryGetValue(item.StringTemplateId, out var copies) && copies.Count > 0)
                assigned.Add(copies.Dequeue(), item.Id);
        return assigned;
    }

    // Item.Compare includes durability, ignores slot and grid addresses, and collapses identical
    // children. A loadout identifies what is packed or fitted, including its location and quantity.
    internal static bool SameBuild(Item plan, Item owned)
    {
        if (plan == owned) return true;
        if (plan == null || owned == null || plan.TemplateId != owned.TemplateId ||
            plan.StackObjectsCount != owned.StackObjectsCount) return false;
        if (!(plan is ContainerCollection expected)) return true;
        if (!(owned is ContainerCollection actual)) return false;
        int count = 0;
        foreach (var container in expected.Containers)
        {
            count++;
            var other = actual.GetContainer(container.ID);
            if (container is Slot slot)
            {
                if (!(other is Slot fitted) || !SameBuild(slot.ContainedItem, fitted.ContainedItem)) return false;
            }
            else if (container is Grid grid)
            {
                if (!(other is Grid packed) || packed.ItemCollection.Count != grid.ItemCollection.Count) return false;
                foreach (var pair in grid.ItemCollection)
                {
                    var item = packed.GetItemAt(pair.Value);
                    if (item == null || packed.GetItemLocation(item) != pair.Value || !SameBuild(pair.Key, item))
                        return false;
                }
            }
            else if (container is StackSlot stack)
            {
                if (!(other is StackSlot loaded) || !SameRounds(stack, loaded)) return false;
            }
            else
            {
                if (other == null) return false;
                using var items = other.Items.GetEnumerator();
                foreach (var child in container.Items)
                    if (!items.MoveNext() || !SameBuild(child, items.Current)) return false;
                if (items.MoveNext()) return false;
            }
        }
        foreach (var container in actual.Containers) count--;
        return count == 0;
    }

    private static bool SameRounds(StackSlot expected, StackSlot actual)
    {
        // EFT can represent a run of identical rounds as several adjacent stacks.
        using var rounds = actual.Items.GetEnumerator();
        int left = 0;
        foreach (var ammo in expected.Items)
        {
            for (int needed = ammo.StackObjectsCount; needed > 0;)
            {
                if (left == 0)
                {
                    if (!rounds.MoveNext()) return false;
                    left = rounds.Current.StackObjectsCount;
                }
                if (left <= 0 || rounds.Current.TemplateId != ammo.TemplateId) return false;
                int count = Math.Min(left, needed);
                needed -= count;
                left -= count;
            }
        }
        return left == 0 && !rounds.MoveNext();
    }

    private static bool Loose(Item item, CompoundItem stash, SortingTable table)
    {
        // Loose supplies can live inside storage cases. Leave everything carried by another
        // weapon, rig, bag or other piece of equipment with its owner.
        while (item.CurrentAddress is GridItemAddress address)
        {
            item = address.Container.ParentItem;
            if (item == stash || item == table) return true;
            if (!(item is SimpleContainer || item is LootContainer)) break;
        }
        return false;
    }

    internal static IEnumerable<Item> Candidates(Item plan, IEnumerable<Item> available, CompoundItem stash, SortingTable table)
    {
        foreach (var item in available)
        {
            if (!Loose(item, stash, table)) continue;
            if (NewBodies != null && NewBodies.TryGetValue(item, out var planId))
            {
                if (plan.Id == planId) yield return item;
            }
            else if (!(plan is ContainerCollection) || SameBuild(plan, item))
                yield return item;
        }
    }

    // Both preset preview and equip search through here. Mannequins pass InventoryEquipment as the source.
    [HarmonyPatch(typeof(ItemManipulator), nameof(ItemManipulator.FindUserItems))]
    private static class Find
    {
        private static void Postfix(Item presetItem, CompoundItem stashItem, SortingTable sortingTable, ref IEnumerable<Item> __result)
        {
            if (stashItem is Stash && GearClient.OutOfRaid)
                __result = Candidates(presetItem, __result, stashItem, sortingTable);
        }
    }

    [HarmonyPatch(typeof(ItemManipulator), nameof(ItemManipulator.TransferItemsFromPreset))]
    private static class Transfer
    {
        private static bool Prefix(Item presetItem, IEnumerable<Item> foundItems, ItemAddress toAddress,
            CompoundItem transferSource, InventoryController inventoryController, ref Option<IEnumerable<IOperationResult>> __result)
        {
            if (!(transferSource is Stash) || !(presetItem is ContainerCollection) || presetItem.StackMaxSize != 1 ||
                !GearClient.OutOfRaid) return true;
            // EFT otherwise unpacks even matching items with a repairable or consumable component.
            foreach (var item in ItemManipulator.SortUserItems(presetItem, foundItems))
            {
                if (!SameBuild(presetItem, item)) continue;
                if (toAddress.Equals(item.CurrentAddress))
                    __result = new Option<IEnumerable<IOperationResult>>(Array.Empty<IOperationResult>());
                else
                {
                    var move = ItemManipulator.Move(item, toAddress, inventoryController);
                    if (move.Failed) __result = move.Error;
                    else __result = new Option<IEnumerable<IOperationResult>>(new IOperationResult[] { move.Value });
                }
                return false;
            }
            // A simulated body or the new body assigned to this entry still needs its contents fitted.
            return true;
        }
    }
}
