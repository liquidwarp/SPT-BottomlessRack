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
    private static readonly AccessTools.FieldRef<TraderDealScreen, Trader> ScreenTrader =
        AccessTools.FieldRefAccess<TraderDealScreen, Trader>("_trader");
    private static readonly AccessTools.FieldRef<TraderDealScreen, AutoExchange> ScreenExchange =
        AccessTools.FieldRefAccess<TraderDealScreen, AutoExchange>("_autoExchange");
    internal static Trader CurrentTrader => _current?.Trader;
    private readonly TraderDealScreen _screen;
    private Trader Trader => ScreenTrader(_screen);
    private readonly ItemUiContext _ui;
    private Task _catalogueRead;
    private SelectorRefresh _watcher;

    private TraderPurchases(TraderDealScreen screen, ItemUiContext ui)
    {
        _screen = screen;
        _ui = ui;
    }

    // Set the sale scope before EFT binds its first quote, including at Fence.
    private static void Prefix(TraderDealScreen __instance, ItemUiContext itemUiContext, UIParent ___UI)
    {
        _current?.Close();
        if (!GearClient.OutOfRaid) return;
        _current = new TraderPurchases(__instance, itemUiContext);
        ___UI.AddDisposable(_current.Close);
    }

    private static void Postfix(InventoryController stashController)
    {
        var shop = _current;
        if (shop == null || shop.Trader.Id == FenceId) return;
        GearClient.Enter(stashController);
        shop._watcher = SelectorRefresh.Watch(shop._screen.gameObject, shop.Refresh);
        shop._screen.OnTradingModeChanged += shop.ModeChanged;
        shop.WatchRead(GearClient.CurrentRead);
    }

    private static TraderPurchases BuyTab =>
        _current?._catalogueRead != null && _current._screen.TradeMode == ETradeMode.Purchase ? _current : null;

    private static bool Unlocked(Item offered) => GearClient.Ready && GearClient.IsUnlocked(offered);

    private bool CanBuy(Item item) => item != null && _catalogueRead.IsCompleted && !Unlocked(item);

    private void WatchRead(Task read)
    {
        _catalogueRead = read;
        Refresh();
        if (!read.IsCompleted) GearClient.Run(FinishRead(read));
    }

    private async Task FinishRead(Task read)
    {
        await read;
        if (_current == this && Trader.CurrentAssortment != null)
            _screen.UpdateDealButton();
    }

    [HarmonyPatch(typeof(Assortment), nameof(Assortment.Purchase))]
    private static class Purchase
    {
        private static bool Prefix(Assortment __instance, ref Task<IResult> __result, out bool __state)
        {
            var item = __instance.SelectedItem;
            if (BuyTab is { } shop && ReferenceEquals(__instance, shop.Trader.CurrentAssortment) && !shop.CanBuy(item))
            {
                __state = false;
                __result = Task.FromResult<IResult>(new FailedResult("This offer is unavailable."));
                return false;
            }
            // EFT can clear SelectedItem as soon as the last unit in stock is ordered.
            __state = GearSales.ContainsGear(item);
            return true;
        }

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
        var assortment = Trader.CurrentAssortment;
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
        ReferenceEquals(assortment, shop.Trader.CurrentAssortment) && Unlocked(view.Item);

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
            if (BuyTab is { } shop && ReferenceEquals(__instance, shop.Trader.CurrentAssortment) && Unlocked(item))
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
            if (shop.CanBuy(shop.Trader.CurrentAssortment?.SelectedItem)) return true;
            withAutoExchange = false;
            exchangeData = default;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(AutoExchange), nameof(AutoExchange.ExchangeCurrency))]
    private static class Exchange
    {
        private static bool Prefix(AutoExchange __instance, ref Task<bool> __result)
        {
            if (BuyTab is not { } shop || !ReferenceEquals(__instance, ScreenExchange(shop._screen)) ||
                shop.CanBuy(shop.Trader.CurrentAssortment?.SelectedItem)) return true;
            __result = Task.FromResult(false);
            return false;
        }
    }
}
