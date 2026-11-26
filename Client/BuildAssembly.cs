using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;

namespace Liquidwarp.UnlockGearProgression.Client;

[HarmonyPatch(typeof(EditBuildScreen), nameof(EditBuildScreen.Show), typeof(Item), typeof(Item), typeof(InventoryController), typeof(IEftSession))]
internal static class BuildScreenPatch
{
    private static void Postfix(EditBuildScreen __instance, InventoryController inventoryController)
    {
        if (!GearClient.OutOfRaid) return;
        SelectorRefresh.Watch(__instance.gameObject, __instance.CheckForVitalParts);
        GearClient.Enter(inventoryController);
    }
}

[HarmonyPatch(typeof(EditBuildScreen), nameof(EditBuildScreen.TryToAssembleBuild))]
internal static class BuildAssembly
{
    internal static readonly AccessTools.FieldRef<EditBuildScreen, Item> Target =
        AccessTools.FieldRefAccess<EditBuildScreen, Item>("_itemBody");
    internal static readonly AccessTools.FieldRef<EditBuildScreen, Item> Plan =
        AccessTools.FieldRefAccess<EditBuildScreen, Item>("Item");
    private static readonly AccessTools.FieldRef<EditBuildScreen, List<Item>> PlayerItems =
        AccessTools.FieldRefAccess<EditBuildScreen, List<Item>>("_playerItems");
    private static readonly AccessTools.FieldRef<EditBuildScreen, Item[]> AcceptableBodies =
        AccessTools.FieldRefAccess<EditBuildScreen, Item[]>("_acceptableWeaponBodies");
    private static bool _assembling;

    internal static List<SpawnLine> Missing(EditBuildScreen screen, Weapon target)
    {
        var required = new List<string>();
        foreach (var item in screen.GetItemsInBuild())
            if (item is Mod) required.Add(item.TemplateId);
        var owned = new List<string>();
        var seen = new HashSet<string>();
        foreach (var item in WeaponAssembler.GetInstalledMods(target))
            if (seen.Add(item.Id)) owned.Add(item.TemplateId);
        foreach (var item in screen.InventoryController.Inventory.GetPlayerItems(EPlayerItems.Stash))
            if (item is Mod && screen.IsAcceptableItem(item) && seen.Add(item.Id)) owned.Add(item.TemplateId);
        return GearProtocol.Missing(required, owned);
    }

    private static bool Prefix(EditBuildScreen __instance, Item[] ____acceptableWeaponBodies, UIParent ___UI)
    {
        if (!GearClient.OutOfRaid || !(Plan(__instance) is Weapon plan)) return true;
        if (_assembling) return false;
        var target = Target(__instance) as Weapon;
        if (target == null && (____acceptableWeaponBodies?.Length > 0 || !GearClient.Unlocked.Contains(plan.TemplateId)))
            return true;
        if (target != null)
        {
            var missing = Missing(__instance, target);
            if (missing.Count == 0 || (GearClient.Ready && !missing.Exists(line => GearClient.Unlocked.Contains(line.TemplateId))))
                return true;
        }
        GearClient.Run(Assemble(__instance, target, plan, ___UI.CancellationToken));
        return false;
    }

    private static async Task Assemble(EditBuildScreen screen, Weapon target, Weapon plan, CancellationToken lifetime)
    {
        _assembling = true;
        try
        {
            var planHash = plan.GetHashSum();
            bool Current() => !lifetime.IsCancellationRequested && screen != null && screen.isActiveAndEnabled &&
                GearClient.OutOfRaid && ReferenceEquals(Target(screen), target) && ReferenceEquals(Plan(screen), plan) &&
                plan.GetHashSum() == planHash;

            var inventory = screen.InventoryController;
            var ready = await GearClient.EnsureCatalogue(inventory);
            if (!Current()) return;
            if (target == null)
            {
                if (!ready)
                {
                    GearClient.Warn("Could not load unlocked gear. Try assembling the preset again.");
                    return;
                }
                if (!GearClient.Unlocked.Contains(plan.TemplateId)) return;
                // Some EFT versions omit weapons from the body's picker. Prefer a loose stash
                // weapon before requesting a factory build of the same template.
                var bodies = new List<Item>();
                foreach (var item in inventory.Inventory.Stash.GetAllItemsFromCollection())
                    if (item is Weapon weapon && weapon.TemplateId == plan.TemplateId &&
                        weapon.PinLockState != EItemPinLockState.Locked && weapon.CurrentAddress is GridItemAddress &&
                        weapon.Parent.Container.ParentItem.IsContainer)
                        bodies.Add(weapon);
                if (bodies.Count > 1)
                {
                    AcceptableBodies(screen) = bodies.ToArray();
                    screen.OpenSelectBodyWindow(true);
                    return;
                }
                if (bodies.Count == 0)
                {
                    var roots = await GearClient.One(plan.TemplateId);
                    if (roots == null || roots.Count != 1 || !Current() || !(roots[0] is Weapon issued)) return;
                    bodies.Add(issued);
                    PlayerItems(screen) = new List<Item>(inventory.Inventory.GetPlayerItems(EPlayerItems.Stash));
                }
                AcceptableBodies(screen) = bodies.ToArray();
                target = (Weapon)bodies[0];
                screen.UpdateItemBody(target);
            }

            var missing = Missing(screen, target);
            if (missing.Count > 0 && !ready)
            {
                GearClient.Warn("Could not load unlocked gear. Try assembling the preset again.");
                return;
            }
            missing.RemoveAll(line => !GearClient.Unlocked.Contains(line.TemplateId));
            if (missing.Count > 0)
            {
                if (await GearClient.Spawn(missing) == null || !Current()) return;
                PlayerItems(screen) = new List<Item>(inventory.Inventory.GetPlayerItems(EPlayerItems.Stash));
                screen.method_15();
            }
            var installed = WeaponAssembler.GetInstalledMods(target);
            var remaining = screen.GetMissingBuildItems(installed);
            foreach (var item in remaining)
                if (GearClient.Unlocked.Contains(item.TemplateId))
                {
                    GearClient.Warn("EFT could not use the issued parts for this preset. Check your stash.");
                    return;
                }
            if (remaining.Count == 0)
                await screen.PerformAssemble(installed);
            else
                ItemUiContext.Instance.ShowMissingItemsWindow(remaining, null, false,
                    () => { if (Current()) GearClient.Run(screen.PerformAssemble(installed)); },
                    _ => { if (Current()) screen.FindBuildParts(true, installed); });
        }
        finally { _assembling = false; }
    }
}

[HarmonyPatch(typeof(EditBuildScreen), nameof(EditBuildScreen.CheckForVitalParts))]
internal static class UnlockedWeaponBodyPatch
{
    private static void Postfix(EditBuildScreen __instance, Item[] ____acceptableWeaponBodies, bool ___RefreshingWeapon)
    {
        if (!___RefreshingWeapon && BuildAssembly.Target(__instance) == null && ____acceptableWeaponBodies?.Length == 0 &&
            BuildAssembly.Plan(__instance) is Weapon plan && GearClient.Ready && GearClient.Unlocked.Contains(plan.TemplateId))
            __instance.SetItemReadyToAssembleActive(true);
    }
}

[HarmonyPatch(typeof(EditBuildScreen), nameof(EditBuildScreen.GetMissingBuildItems))]
internal static class MissingBuildItemsPatch
{
    private static void Postfix(EditBuildScreen __instance, Item ____itemBody, ref List<Item> __result)
    {
        if (!GearClient.Ready || __result == null || !(____itemBody is Weapon target)) return;
        // EFT can count the same physical attachment as installed and available. Correct earned shortages
        // by quantity and preserve the native result (and any other provider's entries) for locked parts.
        var counts = new Dictionary<string, int>();
        foreach (var line in BuildAssembly.Missing(__instance, target))
            if (GearClient.Unlocked.Contains(line.TemplateId)) counts[line.TemplateId] = line.Quantity;
        if (counts.Count == 0 && !__result.Exists(item => GearClient.Unlocked.Contains(item.TemplateId))) return;
        var missing = new List<Item>(__result.Count);
        bool changed = false;
        foreach (var item in __result)
        {
            if (GearClient.Unlocked.Contains(item.TemplateId))
            {
                if (!counts.TryGetValue(item.TemplateId, out var count) || count == 0) { changed = true; continue; }
                counts[item.TemplateId] = count - 1;
            }
            missing.Add(item);
        }
        foreach (var item in __instance.GetItemsInBuild())
        {
            if (counts.TryGetValue(item.TemplateId, out var count) && count > 0)
            {
                missing.Add(item);
                counts[item.TemplateId] = count - 1;
                changed = true;
            }
        }
        if (changed) __result = missing;
    }
}
