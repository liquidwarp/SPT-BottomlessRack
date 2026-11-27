using System.Runtime.CompilerServices;
using Comfort.Common;
using EFT.InventoryLogic;
using EFT.Trading;
using EFT.UI;
using EFT.UI.Ragfair;
using HarmonyLib;

namespace Liquidwarp.UnlockGearProgression.Client;

[HarmonyPatch(typeof(TraderDealScreen), nameof(TraderDealScreen.Close))]
internal static class GearSales
{
    // Only the PMC trader screen sets this. The Scav transfer also stages items with Fence.
    internal static Trader CurrentTrader;
    private static void Prefix() => CurrentTrader = null;

    private static readonly ConditionalWeakTable<ItemTemplate, StrongBox<bool>> Covered = new();

    private static StrongBox<bool> Classify(ItemTemplate template)
    {
        var included = false;
        if (template._type == NodeType.Item && !template.QuestItem && template.Width > 0 && template.Height > 0)
        {
            var parent = template;
            for (var depth = 0; parent != null && depth < GearCategories.MaxDepth; depth++, parent = parent.Parent)
            {
                if (GearCategories.Exclusions.Contains(parent._id)) return new(false);
                included |= GearCategories.Families.Contains(parent._id);
            }
        }
        return new(included);
    }

    private static bool ContainsGear(Item item)
    {
        if (item == null) return false;
        if (Covered.GetValue(item.Template, Classify).Value) return true;
        if (item is ContainerCollection collection)
            foreach (var child in collection.GetAllItemsFromCollection())
                if (Covered.GetValue(child.Template, Classify).Value) return true;
        return false;
    }

    [HarmonyPatch(typeof(Trader), nameof(Trader.GetUserItemPrice))]
    private static class SellQuote
    {
        private static bool Prefix(Trader __instance, Item item, ref Trader.ItemPrice? __result)
        {
            if (!ReferenceEquals(__instance, CurrentTrader) || !ContainsGear(item)) return true;
            __result = null;
            return false;
        }
    }

    [HarmonyPatch(typeof(Assortment), nameof(Assortment.Sell))]
    private static class Sell
    {
        private static bool Prefix(Assortment __instance)
        {
            if (!ReferenceEquals(CurrentTrader?.CurrentAssortment, __instance)) return true;
            // EFT stages clones. Inspect the originals so changes after staging count.
            foreach (var item in __instance._clones.Values)
                if (ContainsGear(item))
                {
                    GearClient.Warn(GearCategories.SaleWarning);
                    return false;
                }
            return true;
        }
    }

    [HarmonyPatch(typeof(RagFair), nameof(RagFair.CanBeSelectedAtRagfair))]
    private static class SelectOffer
    {
        private static bool Prefix(Item item, ref string error, ref bool __result)
        {
            if (!ContainsGear(item)) return true;
            error = GearCategories.SaleWarning;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(AddOfferWindow), nameof(AddOfferWindow.CheckRestrictions))]
    private static class ConfirmOffer
    {
        private static bool Prefix(RagfairNewOfferContext ____offerContext, ref IResult __result)
        {
            foreach (var item in ____offerContext.SelectedItems)
                if (ContainsGear(item))
                {
                    __result = new FailedResult(GearCategories.SaleWarning);
                    return false;
                }
            return true;
        }
    }
}
