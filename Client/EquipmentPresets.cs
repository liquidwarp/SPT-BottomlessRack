using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EFT.Builds;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.Builds;
using HarmonyLib;

namespace Liquidwarp.UnlockGearProgression.Client;

[HarmonyPatch(typeof(EquipmentBuildsScreen), nameof(EquipmentBuildsScreen.EquipBuild))]
internal static class EquipmentPresets
{
    private static readonly AccessTools.FieldRef<EquipmentBuildsScreen, EquipmentBuild> CurrentBuild =
        AccessTools.FieldRefAccess<EquipmentBuildsScreen, EquipmentBuild>("_currentBuild");
    private static bool _equipping;

    private static bool Prefix(EquipmentBuildsScreen __instance, EquipmentBuild ____currentBuild,
        BackEndInventoryController ____realInventoryController, bool ____isApplying, UIParent ___UI)
    {
        // The mannequin's storage action transfers from its own equipment, not the stash.
        if (!GearClient.OutOfRaid || ____currentBuild == null || ____currentBuild.BuildType == EEquipmentBuildType.Storage)
            return true;
        if (_equipping || ____isApplying) return false;
        if (!__instance.TryApplyBuild(out var missing, out _)) return false;
        var ready = GearClient.Ready && ReferenceEquals(GearClient.Inventory, ____realInventoryController);
        if (missing.Count == 0 || (ready && !missing.Exists(item => GearClient.Unlocked.Contains(item.TemplateId))))
            return true;
        GearClient.Run(Equip(__instance, ____currentBuild, ____realInventoryController, ___UI.CancellationToken));
        return false;
    }

    private static async Task Equip(EquipmentBuildsScreen screen, EquipmentBuild build, InventoryController inventory,
        CancellationToken lifetime)
    {
        _equipping = true;
        try
        {
            bool Current() => !lifetime.IsCancellationRequested && screen != null && screen.isActiveAndEnabled &&
                ReferenceEquals(build, CurrentBuild(screen)) && GearClient.OutOfRaid;

            // TryApplyBuild simulates the whole loadout, including nested items, partial stacks,
            // the worn equipment and the sorting table. Selecting or previewing a preset only simulates.
            bool catalogueReady = await GearClient.EnsureCatalogue(inventory);
            var issued = new HashSet<string>();
            while (Current() && screen.TryApplyBuild(out var missing, out _))
            {
                if (missing.Count == 0)
                {
                    screen.ApplyBuild(build);
                    return;
                }
                if (!catalogueReady)
                {
                    GearClient.Warn("Could not load unlocked gear. Try equipping the preset again.");
                    return;
                }
                var batch = GearProtocol.PresetSupplies(Shortages(missing), GearClient.Unlocked);
                if (batch.Count == 0)
                {
                    var toBuy = GroupForPurchase(missing);
                    ItemUiContext.Instance.ShowMissingItemsWindow(toBuy, null, false,
                        () => { if (Current()) screen.ApplyBuild(build); },
                        _ => { if (Current()) screen.FindPresetParts(toBuy); });
                    return;
                }
                foreach (var line in batch)
                    if (!issued.Add(line.TemplateId))
                    {
                        GearClient.Warn("EFT could not use the issued gear for this preset. Check your stash.");
                        return;
                    }
                if (await GearClient.Spawn(batch) == null) return;
            }
        }
        finally { _equipping = false; }
    }

    private static IEnumerable<(string, int, bool)> Shortages(List<Item> missing)
    {
        foreach (var item in missing)
            yield return (item.TemplateId, item.StackObjectsCount, item is Weapon);
    }

    private static List<Item> GroupForPurchase(List<Item> missing)
    {
        // EFT's missing-items window accepts the simulation's items, one stack per template.
        var byTemplate = new Dictionary<string, Item>();
        foreach (var item in missing)
        {
            if (byTemplate.TryGetValue(item.TemplateId, out var stack))
                stack.StackObjectsCount += item.StackObjectsCount;
            else
                byTemplate.Add(item.TemplateId, item);
        }
        return new List<Item>(byTemplate.Values);
    }
}
