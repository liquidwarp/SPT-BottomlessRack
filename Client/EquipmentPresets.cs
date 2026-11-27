using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EFT;
using EFT.Builds;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.Builds;
using HarmonyLib;

namespace Liquidwarp.BottomlessRack.Client;

[HarmonyPatch(typeof(EquipmentBuildsScreen), nameof(EquipmentBuildsScreen.EquipBuild))]
internal static class EquipmentPresets
{
    private static readonly AccessTools.FieldRef<EquipmentBuildsScreen, EquipmentBuild> CurrentBuild =
        AccessTools.FieldRefAccess<EquipmentBuildsScreen, EquipmentBuild>("_currentBuild");
    private static bool _equipping;

    private static bool CatalogueReady(InventoryController inventory) =>
        GearClient.Ready && ReferenceEquals(GearClient.Inventory, inventory);

    private static bool IsUnlocked(Item item) => GearClient.Unlocked.Contains(item.StringTemplateId);

    [HarmonyPatch(typeof(EquipmentBuildsScreen), nameof(EquipmentBuildsScreen.Show),
        typeof(IEftSession), typeof(BackEndInventoryController), typeof(IHealthController), typeof(InventoryEquipment))]
    private static class Open
    {
        private static readonly AccessTools.FieldRef<EquipmentBuildsScreen, EquipmentBuildItemContext<InventoryEquipment>> Context =
            AccessTools.FieldRefAccess<EquipmentBuildsScreen, EquipmentBuildItemContext<InventoryEquipment>>("_equipmentContext");

        private static void Prefix(BackEndInventoryController inventoryController) => GearClient.Enter(inventoryController);

        private static void Postfix(EquipmentBuildsScreen __instance, BackEndInventoryController inventoryController,
            TabGroup ____equipmentTabs, UIParent ___UI)
        {
            if (!GearClient.OutOfRaid) return;
            void Refresh()
            {
                if (__instance == null || !__instance.isActiveAndEnabled || !CatalogueReady(inventoryController)) return;
                var build = CurrentBuild(__instance);
                var context = Context(__instance);
                if (build == null || build.BuildType == EEquipmentBuildType.Storage || !__instance._equipButton.Interactable ||
                    context == null || context._missingItems.Count == 0) return;

                foreach (var item in build.Equipment.GetAllItems())
                {
                    if (!context._missingItems.ContainsKey(item.Id) || !IsUnlocked(item)) continue;
                    var containersSelected = ____equipmentTabs._selectedTab == __instance._containersTab;
                    __instance.SetCurrentBuild(build);
                    if (containersSelected) ____equipmentTabs.SelectionChangedHandler(__instance._containersTab, true);
                    break;
                }
            }

            GearClient.Updated += Refresh;
            ___UI.AddDisposable(() => GearClient.Updated -= Refresh);
        }
    }

    [HarmonyPatch(typeof(EquipmentBuildsScreen), nameof(EquipmentBuildsScreen.UpdatePreview))]
    private static class Preview
    {
        private static void Prefix(EquipmentBuildsScreen __instance, EquipmentBuild ____currentBuild,
            BackEndInventoryController ____realInventoryController, ref IEnumerable<Item> notFoundItems)
        {
            // SetCurrentBuild has already set the Equip icon and disabled the button on simulation errors.
            if (____currentBuild.BuildType == EEquipmentBuildType.Storage || !CatalogueReady(____realInventoryController) ||
                !__instance._equipButton.Interactable || notFoundItems is ICollection<Item> { Count: 0 }) return;

            var remaining = new List<Item>();
            foreach (var item in notFoundItems)
                if (!IsUnlocked(item)) remaining.Add(item);
            notFoundItems = remaining;
            if (remaining.Count == 0) __instance._equipBuildIcon.sprite = __instance._canEquip;
        }
    }

    private static bool Prefix(EquipmentBuildsScreen __instance, EquipmentBuild ____currentBuild,
        BackEndInventoryController ____realInventoryController, bool ____isApplying, UIParent ___UI)
    {
        // The mannequin's storage action transfers from its own equipment, not the stash.
        if (!GearClient.OutOfRaid || ____currentBuild == null || ____currentBuild.BuildType == EEquipmentBuildType.Storage)
            return true;
        if (_equipping || ____isApplying) return false;
        if (!__instance.TryApplyBuild(out var missing, out _)) return false;
        if (missing.Count == 0 || (CatalogueReady(____realInventoryController) && !missing.Exists(IsUnlocked)))
            return true;
        GearClient.Run(Equip(__instance, ____currentBuild, ____realInventoryController, ___UI.CancellationToken));
        return false;
    }

    private static async Task Equip(EquipmentBuildsScreen screen, EquipmentBuild build, InventoryController inventory,
        CancellationToken lifetime, bool allowPartial = false)
    {
        _equipping = true;
        try
        {
            bool Current() => !lifetime.IsCancellationRequested && screen != null && screen.isActiveAndEnabled &&
                ReferenceEquals(build, CurrentBuild(screen)) && GearClient.OutOfRaid;

            bool catalogueReady = await GearClient.EnsureCatalogue(inventory);
            if (!Current() || !screen.TryApplyBuild(out var missing, out _)) return;
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

            void OfferPurchase(List<Item> shortages, Action equip)
            {
                var toBuy = GroupForPurchase(shortages);
                ItemUiContext.Instance.ShowMissingItemsWindow(toBuy, null, false,
                    () => { if (Current() && !_equipping) equip(); },
                    _ => { if (Current()) screen.FindPresetParts(toBuy); });
            }

            if (!allowPartial)
            {
                var locked = missing.FindAll(item => !IsUnlocked(item));
                if (locked.Count > 0)
                {
                    // Shopping or cancelling should leave no partly assembled gear in the stash.
                    OfferPurchase(locked, () => GearClient.Run(Equip(screen, build, inventory, lifetime, true)));
                    return;
                }
            }
            var supplies = allowPartial ? PresetItems.Supplies(build.Equipment, missing) : missing;
            var batch = GearProtocol.PresetSupplies(Shortages(supplies), GearClient.Unlocked);
            if (batch.Count == 0)
            {
                screen.ApplyBuild(build);
                return;
            }
            var roots = await GearClient.Spawn(batch);
            if (roots == null || !Current()) return;
            var assigned = PresetItems.AssignBodies(supplies, roots);

            void Finish(bool partial)
            {
                PresetItems.NewBodies = assigned;
                try
                {
                    if (!screen.TryApplyBuild(out var remaining, out _)) return;
                    var outstanding = partial ? PresetItems.Supplies(build.Equipment, remaining) : remaining;
                    if (outstanding.Exists(IsUnlocked))
                    {
                        GearClient.Warn("EFT could not use the issued gear for this preset. Check your stash.");
                        return;
                    }
                    if (remaining.Count > 0 && !partial)
                        OfferPurchase(remaining, () => Finish(true));
                    else
                        screen.ApplyBuild(build);
                }
                finally { PresetItems.NewBodies = null; }
            }
            Finish(allowPartial);
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
