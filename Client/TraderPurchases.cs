using System.Threading.Tasks;
using Comfort.Common;
using EFT.InventoryLogic;
using EFT.Trading;
using EFT.UI;
using EFT.UI.DragAndDrop;
using HarmonyLib;

namespace Liquidwarp.BottomlessRack.Client;

[HarmonyPatch(typeof(TraderDealScreen), nameof(TraderDealScreen.Show))]
internal sealed class TraderPurchases
{
    private const string FenceId = "579dc571d53a0658a154fbec";
    private const string Label = "Unlocked";
    private static TraderPurchases _current;
    internal static Trader CurrentTrader => _current?._trader;
    private readonly TraderDealScreen _screen;
    private readonly Trader _trader;
    private readonly ItemUiContext _ui;
    private Task _catalogueRead;
    private SelectorRefresh _watcher;
    private Task _pendingExchange;

    private TraderPurchases(TraderDealScreen screen, Trader trader, ItemUiContext ui)
    {
        _screen = screen;
        _trader = trader;
        _ui = ui;
    }

    // Set the sale scope before EFT binds its first quote, including at Fence.
    private static void Prefix(TraderDealScreen __instance, Trader trader, ItemUiContext itemUiContext, UIParent ___UI)
    {
        _current?.Close();
        if (!GearClient.OutOfRaid) return;
        _current = new TraderPurchases(__instance, trader, itemUiContext);
        ___UI.AddDisposable(_current.Close);
    }

    private static void Postfix(InventoryController stashController)
    {
        var shop = _current;
        if (shop == null || shop._trader.Id == FenceId) return;
        GearClient.Enter(stashController);
        shop._watcher = SelectorRefresh.Watch(shop._screen.gameObject, shop.Refresh);
        shop._screen.OnTradingModeChanged += shop.ModeChanged;
        shop.WatchRead(GearClient.CurrentRead);
    }

    private static TraderPurchases BuyTab =>
        _current?._catalogueRead != null && _current._screen.TradeMode == ETradeMode.Purchase ? _current : null;

    private static bool Unlocked(Item offered) =>
        offered != null && GearClient.Ready && GearClient.Unlocked.Contains(offered.TemplateId);

    private bool WaitingForCatalogue => !_catalogueRead.IsCompleted;

    private void WatchRead(Task read)
    {
        _catalogueRead = read;
        Refresh();
        if (!read.IsCompleted) GearClient.Run(FinishRead(read));
    }

    private async Task FinishRead(Task read)
    {
        await read;
        if (_current == this && _trader.CurrentAssortment != null)
            _screen.UpdateDealButton();
    }

    [HarmonyPatch(typeof(Assortment), nameof(Assortment.Purchase))]
    private static class Purchase
    {
        // EFT can clear SelectedItem as soon as the last unit in stock is ordered.
        private static void Prefix(Assortment __instance, out bool __state) =>
            __state = GearSales.ContainsGear(__instance.SelectedItem);

        private static void Postfix(bool __state, ref Task<IResult> __result)
        {
            if (__state) __result = AfterPurchase(__result);
        }

        private static async Task<IResult> AfterPurchase(Task<IResult> purchase)
        {
            var result = await purchase;
            if (result.Succeed)
            {
                var read = GearClient.RefreshAfterPurchase();
                if (_current?._catalogueRead != null) _current.WatchRead(read);
            }
            return result;
        }
    }

    private void Close()
    {
        if (_current != this) return;
        _current = null;
        if (_watcher == null) return;
        _screen.OnTradingModeChanged -= ModeChanged;
        _watcher.StopWatching();
        Repaint(closing: true);
    }

    private void ModeChanged(ETradeMode mode) => Refresh();

    private void Refresh()
    {
        var assortment = _trader.CurrentAssortment;
        if (assortment == null) return;
        if (BuyTab == this && Unlocked(assortment.SelectedItem)) assortment.SelectItem(null);
        Repaint(closing: false);
        _screen.UpdateDealButton();
    }

    private void Repaint(bool closing)
    {
        var buying = !closing && BuyTab == this;
        foreach (var row in _screen._traderGridView.GridItemViews)
        {
            if (!(row is TradingItemView view) || view.Item == null) continue;
            bool wasMarked = view._price.text == Label && view._price.gameObject.activeSelf;
            if (wasMarked == (buying && Unlocked(view.Item))) continue;
            view.UpdateScheme();
            if (view.ItemContext != null && ReferenceEquals(view.ItemContext, _ui.CurrentItemContext) &&
                (_ui.PriceTooltip.Displayed || _ui.Tooltip.Displayed))
            {
                view.HideTooltip();
                if (!closing) view.ShowTooltip();
            }
        }
    }

    private static bool Marked(TradingItemView view, Assortment assortment) =>
        BuyTab is { } shop && ReferenceEquals(view.Container, shop._screen._traderGridView) &&
        ReferenceEquals(assortment, shop._trader.CurrentAssortment) && Unlocked(view.Item);

    [HarmonyPatch(typeof(TradingItemView), nameof(TradingItemView.SetPrice))]
    private static class Price
    {
        private static void Postfix(TradingItemView __instance, Assortment ____traderAssortment)
        {
            if (!Marked(__instance, ____traderAssortment)) return;
            __instance._currency.gameObject.SetActive(false);
            __instance._schemeIcon.gameObject.SetActive(false);
            __instance._price.text = Label;
            __instance._price.gameObject.SetActive(true);
        }
    }

    [HarmonyPatch(typeof(TradingItemView), nameof(TradingItemView.ShowTooltip))]
    private static class Hover
    {
        private static void Postfix(TradingItemView __instance, Assortment ____traderAssortment)
        {
            if (!Marked(__instance, ____traderAssortment)) return;
            var tooltip = _current._ui.PriceTooltip;
            if (!tooltip.Displayed) return;
            // The native hover supplies the item name and lets other mods add their item details.
            tooltip._currencyIcon.gameObject.SetActive(false);
            tooltip._price.color = tooltip._priceColor;
            tooltip._price.text = __instance.Item is Mod ? "Free in Modding" : "Free from inventory";
        }
    }

    [HarmonyPatch(typeof(Assortment), nameof(Assortment.SelectItem))]
    private static class Select
    {
        private static void Prefix(Assortment __instance, ref Item item)
        {
            if (BuyTab is { } shop && ReferenceEquals(__instance, shop._trader.CurrentAssortment) && Unlocked(item))
                item = null; // Native deselection clears the old requirements and disables Deal.
        }
    }

    [HarmonyPatch(typeof(TraderDealScreen), nameof(TraderDealScreen.IsValidDeal))]
    private static class Validate
    {
        private static bool Prefix(TraderDealScreen __instance, ref bool withAutoExchange,
            ref AutoExchange.AutoExchangeData exchangeData, ref bool __result)
        {
            if (BuyTab is not { } shop || !ReferenceEquals(__instance, shop._screen)) return true;
            var item = shop._trader.CurrentAssortment?.SelectedItem;
            if (item != null && !shop.WaitingForCatalogue && !Unlocked(item)) return true;
            withAutoExchange = false;
            exchangeData = default;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(TraderDealScreen), nameof(TraderDealScreen.OnDealButtonClickAsync))]
    private static class ExchangeDeal
    {
        private static bool Prefix(TraderDealScreen __instance, AutoExchange ____autoExchange, ref Task __result)
        {
            if (BuyTab is not { } shop || !ReferenceEquals(__instance, shop._screen)) return true;
            if (shop._pendingExchange is { IsCompleted: false })
            {
                __result = shop._pendingExchange;
                return false;
            }
            var assortment = shop._trader.CurrentAssortment;
            // A funded cash deal or ordinary barter submits synchronously after IsValidDeal.
            if (assortment != null && assortment.IsValid) return true;
            if (assortment == null || !__instance.IsValidDeal(out var needsExchange, out var quote))
            {
                __result = Task.CompletedTask;
                return false;
            }
            if (!needsExchange) return true;
            __result = shop._pendingExchange = shop.PurchaseWithExchange(assortment, ____autoExchange, quote);
            return false;
        }
    }

    private bool StillSelected(Assortment assortment, Item item, int quantity) =>
        BuyTab == this && ReferenceEquals(assortment, _trader.CurrentAssortment) &&
        item != null && ReferenceEquals(item, assortment.SelectedItem) && quantity == assortment.CurrentQuantity &&
        !assortment.TransactionInProgress && !WaitingForCatalogue && !Unlocked(item);

    private async Task PurchaseWithExchange(Assortment assortment, AutoExchange exchange, AutoExchange.AutoExchangeData quote)
    {
        var item = assortment.SelectedItem;
        var quantity = assortment.CurrentQuantity;
        if (!await exchange.ApproveAutoExchange(quote) || !StillSelected(assortment, item, quantity)) return;
        if (!await exchange.ExchangeCurrency(quote.CurrencyToAutoExchange, quote.AmountToAutoExchange) ||
            !StillSelected(assortment, item, quantity)) return;
        if ((await assortment.Purchase()).Succeed && _current == this) _screen.TryShowWishlistPurchaseNotification(item);
    }
}
