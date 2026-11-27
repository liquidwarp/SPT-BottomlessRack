using System;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;

namespace Liquidwarp.BottomlessRack.Client;

[HarmonyPatch(typeof(MainMenuShowOperation), nameof(MainMenuShowOperation.InventoryController), MethodType.Setter)]
internal static class MenuCataloguePatch
{
    // Load this profile's catalogue even if the player goes straight into a raid.
    private static void Postfix(OfflineInventoryController __0) => GearClient.Enter(__0);
}

[HarmonyPatch(typeof(ItemSpecificationPanel), nameof(ItemSpecificationPanel.method_1))]
internal static class InspectDescription
{
    // EFT writes the localized description here on both opening the window and finishing an examination.
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(ItemSpecificationPanel __instance, Item ____item, UIParent ___UI)
    {
        if (!__instance.Examined) return;
        var template = ____item.StringTemplateId;
        if (MarkUnlocked(__instance._itemLabels, template)) return;
        if ((GearClient.OutOfRaid || !GearClient.CurrentRead.IsCompleted) && GearSales.CoversTemplate(____item.Template))
            WaitForUnlock(__instance._itemLabels, template, ___UI);
    }

    private static bool MarkUnlocked(ItemInfoWindowLabels labels, string template)
    {
        if (!GearClient.Unlocked.Contains(template)) return false;
        const string prefix = "UNLOCKED\n\n";
        var description = labels._description.text;
        if (description == null || !description.StartsWith(prefix, StringComparison.Ordinal))
            labels.SetDescriptionText(prefix + description);
        return true;
    }

    private static void WaitForUnlock(ItemInfoWindowLabels labels, string template, UIParent ui)
    {
        void Refresh() => MarkUnlocked(labels, template);
        GearClient.Updated += Refresh;
        ui.AddDisposable(() => GearClient.Updated -= Refresh);
        GearClient.Refresh(checkOnly: true);
    }
}
