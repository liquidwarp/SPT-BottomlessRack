using System;
using System.Threading.Tasks;
using EFT;
using EFT.Communications;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.Utilities;
using HarmonyLib;
using UnityEngine;

namespace Liquidwarp.BottomlessRack.Client;

[HarmonyPatch(typeof(InventoryItemContextInteractions), MethodType.Constructor,
    typeof(ItemContext), typeof(ItemUiContext), typeof(CompoundItem[]), typeof(Action))]
internal static class SpareMagazines
{
    private static void Postfix(InventoryItemContextInteractions __instance)
    {
        if (__instance.Item is not Weapon weapon || UnlockedMagazine(weapon) is not { } magazine) return;
        var template = magazine.StringTemplateId;
        __instance.method_2(MyModInfo.MOD_GUID + ".spareMagazines", "Spawn spare magazines",
            () => GearClient.Run(SpawnCopies(weapon, template)), ResourcesCache.Pop<Sprite>("Characteristics/Icons/UnloadAmmo"));
    }

    private static Magazine UnlockedMagazine(Weapon weapon)
    {
        if (!GearClient.Ready || GearClient.Inventory == null || !ReferenceEquals(weapon.Owner, GearClient.Inventory) ||
            weapon.GetMagazineSlot() is not { Locked: false, ContainedItem: Magazine magazine })
            return null;

        if (magazine.ReloadMagType is not (Weapon.EReloadMode.ExternalMagazine or Weapon.EReloadMode.ExternalMagazineWithInternalReloadSupport))
            return null;
        return GearClient.IsUnlocked(magazine) ? magazine : null;
    }

    private static async Task SpawnCopies(Weapon weapon, string template)
    {
        var request = RequestedCopies(weapon, template);
        if (request == null)
        {
            GearClient.Warn("Spare magazines are unavailable. Reopen the weapon menu.");
            return;
        }

        var spares = await GearClient.Spawn(new[] { request });
        if (spares is { Count: > 0 })
        {
            var noun = spares.Count == 1 ? "magazine" : "magazines";
            NotificationManager.DisplayMessageNotification($"Spawned {spares.Count} spare {noun} in your stash.");
        }
    }

    private static SpawnLine RequestedCopies(Weapon weapon, string template)
    {
        if (UnlockedMagazine(weapon) is not { } magazine || magazine.StringTemplateId != template) return null;
        var count = (magazine.Template.Width, magazine.Template.Height) switch
        {
            (1, 1) => 3,
            (1, 2 or 3) or (2 or 3, 1) => 2,
            _ => 1
        };
        return new SpawnLine { TemplateId = template, Quantity = count };
    }
}
