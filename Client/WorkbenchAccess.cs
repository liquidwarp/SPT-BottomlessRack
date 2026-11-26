using EFT;
using EFT.UI;
using HarmonyLib;

namespace Liquidwarp.UnlockGearProgression.Client;

[HarmonyPatch(typeof(BonusController), nameof(BonusController.HasBonus))]
internal static class WeaponModificationBonusPatch
{
    private static void Postfix(EBonusType type, ref bool __result)
    {
        if (type == EBonusType.UnlockWeaponModification && !__result && GearClient.OutOfRaid)
            __result = true;
    }
}

[HarmonyPatch(typeof(MenuTaskBar), nameof(MenuTaskBar.BonusChangedHandler))]
internal static class EditBuildTaskBarPatch
{
    // The taskbar caches bonus events separately from the inventory context menu's HasBonus query.
    private static void Prefix(EBonusType bonusType, ref bool isActive)
    {
        if (bonusType == EBonusType.UnlockWeaponModification && GearClient.OutOfRaid)
            isActive = true;
    }
}
