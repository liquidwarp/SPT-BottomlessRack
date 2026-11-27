using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.Trading;
using EFT.UI;
using EFT.UI.DragAndDrop;
using HarmonyLib;

namespace Liquidwarp.UnlockGearProgression.Client;

[HarmonyPatch(typeof(TraderDealScreen), nameof(TraderDealScreen.Show))]
internal sealed class TraderPurchases
{
    private const string FenceId = "579dc571d53a0658a154fbec";
    private const string Label = "Unlocked";
    private static TraderPurchases _current;
    private readonly TraderDealScreen _screen;
    private readonly Trader _trader;
    private readonly ItemUiContext _ui;
    private readonly Task _firstRead;
    private readonly SelectorRefresh _watcher;
    private Task _pendingExchange;

    private TraderPurchases(TraderDealScreen screen, Trader trader, ItemUiContext ui)
    {
        _screen = screen;
        _trader = trader;
        _ui = ui;
        _firstRead = GearClient.CurrentRead;
        _watcher = SelectorRefresh.Watch(screen.gameObject, Refresh);
        screen.OnTradingModeChanged += ModeChanged;
    }

    // A trader change can reuse the screen and its item views.
    private static void Prefix(Trader trader)
    {
        _current?.Close();
        GearSales.CurrentTrader = GearClient.OutOfRaid ? trader : null;
    }

    private static void Postfix(TraderDealScreen __instance, Trader trader, InventoryController stashController,
        ItemUiContext itemUiContext, UIParent ___UI)
    {
        if (trader.Id == FenceId || !GearClient.OutOfRaid) return;
        GearClient.Enter(stashController);
        var shop = _current = new TraderPurchases(__instance, trader, itemUiContext);
        ___UI.AddDisposable(shop.Close);
        shop.Refresh();
        if (shop.WaitingForCatalogue) GearClient.Run(shop.FinishFirstRead());
    }

    private static TraderPurchases BuyTab =>
        _current != null && _current._screen.TradeMode == ETradeMode.Purchase ? _current : null;

    private static bool Unlocked(Item offered) =>
        offered != null && GearClient.Ready && GearClient.Unlocked.Contains(offered.TemplateId);

    private bool WaitingForCatalogue => !GearClient.Ready && !_firstRead.IsCompleted;

    private async Task FinishFirstRead()
    {
        await _firstRead;
        // A successful read raises Updated. A failed first read must also release Deal.
        if (_current == this && !GearClient.Ready && _trader.CurrentAssortment != null)
            _screen.UpdateDealButton();
    }

    private void Close()
    {
        if (_current != this) return;
        _current = null;
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
        private static bool Prefix(TradingItemView __instance, Assortment ____traderAssortment)
        {
            if (!Marked(__instance, ____traderAssortment)) return true;
            var item = __instance.Item;
            var name = __instance.Examined
                ? Singleton<ItemFactory>.Instance.BriefItemName(item, item.Name.Localized())
                : "Unknown item".Localized();
            var hint = item is Mod ? "Already unlocked. Open Modding from your inventory to fit this part for free."
                : "Already unlocked. Equip this gear for free from your inventory.";
            _current._ui.Tooltip.Show(name + "\n" + hint);
            return false;
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
        !assortment.TransactionInProgress && !Unlocked(item);

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
