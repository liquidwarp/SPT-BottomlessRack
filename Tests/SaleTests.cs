using System.Text.Json;
using HarmonyLib;
using Liquidwarp.BottomlessRack.Server;
using NUnit.Framework;
using SPTarkov.Server.Core.Callbacks;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.DI.Routing;
using SPTarkov.Server.Core.Helpers.Commerce;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Helpers.Quest;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Helpers.Traders;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Request;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Ragfair;
using SPTarkov.Server.Core.Models.Eft.Trade;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Routers.ItemEvents;
using SPTarkov.Server.Core.Servers.Ws;
using SPTarkov.Server.Core.Services.Commerce;
using SPTarkov.Server.Core.Utils;

namespace Liquidwarp.BottomlessRack.Tests;

public sealed class SaleTests : ServerFixture
{
    [SetUp]
    public Task PatchEvents() => new Mod(Service, TestLog.For<Mod>()).OnLoadAsync();

    [TearDown]
    public void UnpatchEvents() => new Harmony(MyModInfo.MOD_GUID).UnpatchSelf();

    private TradeItemEventRouter Trades(TradeHelper? trade = null, MailSendService? mail = null)
    {
        var controller = new TradeController(TestLog.For<TradeController>(), TradersDb, Outputs, trade!, new(),
            new(TestLog.For<RandomUtil>(), Cloner), ItemHelper, null!, null!, Http, Localizer, mail!, null!, new() { Fence = null! });
        return new TradeItemEventRouter(new TradeCallbacks(controller));
    }

    private TradeHelper PaidTrades()
    {
        var handbook = new HandbookHelper(TestLog.For<HandbookHelper>(), Templates, null!, Cloner);
        var trader = new TraderHelper(TestLog.For<TraderHelper>(), Globals, TradersDb, Profiles, handbook, null!, null!, new(), null!, null!);
        var quests = new QuestHelper(TestLog.For<QuestHelper>(), Templates, new(), null!, null!, Profiles, null!, null!, null!, null!, null!, null!, Cloner);
        var payments = new PaymentService(TestLog.For<PaymentService>(), TradersDb, Http, handbook, trader, ItemHelper, Placement,
            Localizer, new PaymentHelper(InventoryConfig), InventoryConfig);
        var purchases = new TraderPurchasePersisterService(TestLog.For<TraderPurchasePersisterService>(), null!, new(), Profiles, Localizer, null!);
        var assorts = new TraderAssortHelper(TestLog.For<TraderAssortHelper>(), TradersDb, new(), Profiles,
            new(TestLog.For<AssortHelper>(), Localizer), purchases, trader, null!, Cloner);
        return new(TestLog.For<TradeHelper>(), trader, ItemHelper, quests, payments, null!, Localizer, Http, Placement, null!, assorts, purchases, Cloner);
    }

    private async Task<ItemEventRouterResponse> Send(ItemEventCallbacks stock, params object[] commands)
    {
        var request = new ItemEventRouterRequest { Data = commands.Select(cmd => JsonSerializer.SerializeToElement(cmd)).ToList() };
        using var response = JsonDocument.Parse(await stock.HandleEvents("", request, Session, default));
        return Json.Deserialize<ItemEventRouterResponse>(response.RootElement.GetProperty("data").GetRawText())!;
    }

    private static object Sell(MongoId trader, params Item[] items) => new
    {
        Action = "TradingConfirm", type = "sell_to_trader", tid = trader.ToString(), price = 250,
        items = items.Select(i => new { id = i.Id.ToString(), count = 1 })
    };

    [Test]
    public async Task FirstPaidPurchaseUnlocksOnlyTheDeliveredGear()
    {
        const string rig = "544a5caa4bdc2d1a388b4568";
        var assort = (TradersDb[Traders.PRAPOR] = Offer(Traders.PRAPOR, rig, Optic)).Assort!;
        var offer = assort.Items[0];
        assort.Items.Add(NewItem(Magazine, offer.Id, Templates.Items[rig].Properties!.Grids!.First().Name));
        assort.BarterScheme[offer.Id] = [[new() { Template = Money.ROUBLES, Count = 250 }]];
        assort.Items[1].Upd!.StackObjectsCount = 5;
        Pmc.TradersInfo![Traders.PRAPOR] = new() { SalesSum = 0, LoyaltyLevel = 1, Standing = 0, Unlocked = true };
        var cash = NewItem(Money.ROUBLES, Inventory.Stash!.Value, "hideout");
        cash.Location = new ItemLocation { X = 0, Y = 0 };
        cash.Upd = new() { StackObjectsCount = 1000 };
        Items.Add(cash);
        var buy = new { Action = "TradingConfirm", type = "buy_from_trader", tid = Traders.PRAPOR.ToString(),
            item_id = offer.Id.ToString(), count = 1, scheme_items = new[] { new { id = cash.Id.ToString(), count = 250 } } };
        var stock = Callbacks(Trades(PaidTrades()));

        // The rig is out of stock for the first attempt. The optic is available throughout.
        Assert.That((await Send(stock, buy)).Warnings, Is.Not.Empty);
        Assert.That(cash.Upd.StackObjectsCount, Is.EqualTo(1000));
        offer.Upd!.StackObjectsCount = 5;
        Assert.That((await Service.Read(Session, true, default)).Templates, Is.Empty);
        Assert.That((await Issue(1, (rig, 1))).Warnings, Is.Not.Empty);

        var result = await Send(stock, buy);
        Assert.That(result.Warnings, Is.Empty);
        Assert.That(cash.Upd.StackObjectsCount, Is.EqualTo(750));
        var delivered = result.ProfileChanges[Session].Items!.NewItems!;
        Assert.That(delivered.Select(i => i.Template.ToString()), Is.EquivalentTo(new[] { rig, Magazine }));
        Assert.That(UnlockLedger.Read(Profile).Templates, Is.EquivalentTo(new[] { rig, Magazine }));
        Items.RemoveAll(i => i.Template == rig || i.Template == Magazine);
        Assert.That((await Service.Read(Session, true, default)).Templates, Is.EquivalentTo(new[] { rig, Magazine }));
    }

    [Test]
    public async Task AllTradersRefuseMagazinesAndTheirContainersBeforeAnOrdinarySalePays()
    {
        Award(Magazine);
        var issued = (await Issue(1, (Magazine, 1))).ProfileChanges[Session].Items!.NewItems!.Single();
        var box = NewItem("5c127c4486f7745625356c13", Inventory.Stash!.Value, "hideout");
        var looted = NewItem(Magazine, box.Id, "main");
        looted.Upd = new() { SpawnedInSession = true };
        var ammo = NewItem(Ammo, Inventory.Stash.Value, "hideout");
        var cash = NewItem(Money.ROUBLES, Inventory.Stash.Value, "hideout");
        cash.Upd = new() { StackObjectsCount = 1 };
        Items.AddRange([box, looted, ammo, cash]);

        TradersDb[Traders.PRAPOR] = Offer(Traders.PRAPOR);
        Pmc.TradersInfo![Traders.PRAPOR] = new() { SalesSum = 0, LoyaltyLevel = 1, Standing = 0, Unlocked = true };
        var stock = Callbacks(Trades(PaidTrades()));
        var initialIds = Items.Select(i => i.Id).ToArray();
        foreach (var buyer in new[] { Traders.PRAPOR, Traders.FENCE, new MongoId() })
            foreach (var blocked in new[] { issued, looted, box })
            {
                var result = await Send(stock, Sell(buyer, ammo, blocked));
                Assert.That(result.Warnings!.Single().ErrorMessage, Is.EqualTo("Gear sales are disabled."));
                Assert.That(Items.Select(i => i.Id), Is.EqualTo(initialIds));
            }
        var missing = await Send(stock, Sell(Traders.PRAPOR, ammo, NewItem(Ammo)));
        Assert.That(missing.Warnings, Has.Count.EqualTo(1));
        Assert.That(cash.Upd.StackObjectsCount, Is.EqualTo(1));
        Assert.That(Pmc.TradersInfo[Traders.PRAPOR].SalesSum, Is.Zero);
        Assert.That((await Send(stock, Sell(Traders.PRAPOR, ammo))).Warnings, Is.Empty);
        Assert.That(Items, Does.Not.Contain(ammo));
        Assert.That(cash.Upd.StackObjectsCount, Is.EqualTo(251));
        Assert.That(Pmc.TradersInfo[Traders.PRAPOR].SalesSum, Is.EqualTo(250));
    }

    [Test]
    public async Task FleaChecksEveryRootAndCurrentContentsBeforeDispatch()
    {
        var box = NewItem("5c127c4486f7745625356c13", Inventory.Stash!.Value, "hideout");
        var magazine = NewItem(Magazine, Inventory.Stash.Value, "hideout");
        var medicine = NewItem("544fb45d4bdc2dee738b4568", Inventory.Stash.Value, "hideout");
        Items.AddRange([box, magazine, medicine]);
        var listings = new List<AddOfferRequestData>();
        var stock = Callbacks(new TestRouter(
            new ItemRouteAction<AddOfferRequestData>("RagFairAddOffer", (_, _, request, _, output, _) =>
            {
                listings.Add(request);
                return ValueTask.FromResult(output);
            }),
            new ItemRouteAction<BaseInteractionRequestData>("PackMagazine", (_, _, _, _, output, _) =>
            {
                magazine.ParentId = box.Id;
                return ValueTask.FromResult(output);
            })));
        object ListOffer(bool pack, params Item[] roots) => new { Action = "RagFairAddOffer", sellInOnePiece = pack,
            items = roots.Select(i => i.Id.ToString()), requirements = new[] { new { _tpl = Money.ROUBLES.ToString(), count = 800 } } };
        foreach (var request in new[] { ListOffer(false, magazine), ListOffer(false, medicine, magazine), ListOffer(true, medicine, magazine) })
            Assert.That((await Send(stock, request)).Warnings!.Single().ErrorMessage, Is.EqualTo("Gear sales are disabled."));
        var mixed = await Send(stock, new { Action = "PackMagazine" }, ListOffer(false, box));
        Assert.That(mixed.Warnings!.Single().Index, Is.EqualTo(1));
        Assert.That(magazine.ParentId, Is.EqualTo(box.Id.ToString()));
        Assert.That(listings, Is.Empty);
        magazine.ParentId = Inventory.Stash.Value;
        Assert.That((await Send(stock, ListOffer(false, box))).Warnings, Is.Empty);
        Assert.That((await Send(stock, ListOffer(false, medicine))).Warnings, Is.Empty);
        Assert.That(listings.SelectMany(o => o.Items!), Is.EqualTo(new[] { box.Id, medicine.Id }));
        Assert.That(listings.All(o => o.Requirements!.Single().Count == 800), Is.True);
    }

    [Test]
    public async Task ModdedReceiverWithoutASpawnRecipeIsBlockedForSaleAndAcceptedAsBarterPayment()
    {
        var tpl = new MongoId();
        Templates.Items.Add(tpl, Templates.Items[Rifle] with { Id = tpl });
        try
        {
            var receiver = NewItem(tpl, Inventory.Stash!.Value, "hideout");
            Items.Add(receiver);
            Assert.That(Gear.Eligible(tpl), Is.True);
            Assert.That(Gear.CanIssue(tpl), Is.False);
            var purchases = new List<ProcessBuyTradeRequestData>();
            var stock = Callbacks(new TestRouter(new ItemRouteAction<ProcessBaseTradeRequestData>(
                "TradingConfirm", (_, _, request, _, output, _) =>
                {
                    purchases.Add((ProcessBuyTradeRequestData)request);
                    return ValueTask.FromResult(output);
                })));
            Assert.That((await Send(stock, Sell(Traders.FENCE, receiver))).Warnings, Has.Count.EqualTo(1));
            var buy = new { Action = "TradingConfirm", type = "buy_from_trader", tid = Traders.FENCE.ToString(),
                item_id = new MongoId().ToString(), count = 1, scheme_items = new[] { new { id = receiver.Id.ToString(), count = 1 } } };
            Assert.That((await Send(stock, buy)).Warnings, Is.Empty);
            Assert.That(purchases.Single().SchemeItems!.Single().Id, Is.EqualTo(receiver.Id));
        }
        finally { Templates.Items.Remove(tpl); }
    }

    [Test]
    public async Task ScavSellAllPaysItsFullQuoteAndKeptGearUsesPmcSaleRules()
    {
        TradersDb[Traders.FENCE] = Offer(Traders.FENCE);
        TradersDb[Traders.FENCE].Dialogue["soldItems"] = ["test"];
        var connections = new SptWebSocketConnectionHandler(TestLog.For<SptWebSocketConnectionHandler>(), null!, Json, Profiles, []);
        var notifications = new NotificationSendHelper(TestLog.For<NotificationSendHelper>(), connections, Saves, new(), new(), Json);
        var mail = new MailSendService(TestLog.For<MailSendService>(), Templates, new(), Saves, new(null!),
            new(TestLog.For<DialogueHelper>(), Profiles), notifications, null!, ItemHelper, Cloner);
        var stock = Callbacks(Trades(mail: mail));
        await Service.FinishRaid(Session, ExitStatus.SURVIVED, [new MongoId(Rifle), new MongoId(Optic)]);
        var kept = NewItem(Optic, Inventory.Stash!.Value, "hideout");
        Items.Add(kept);
        Assert.That((await Send(stock, new { Action = "SellAllFromSavage", totalValue = 18790 })).Warnings, Is.Empty);
        var reward = Profile.DialogueRecords![Traders.FENCE].Messages!.Single().Items!.Data!.Single();
        Assert.That(reward.Template, Is.EqualTo(Money.ROUBLES));
        Assert.That(reward.Upd!.StackObjectsCount, Is.EqualTo(18790));
        Assert.That((await Send(stock, Sell(Traders.FENCE, kept))).Warnings, Has.Count.EqualTo(1));
        Assert.That(Items, Does.Contain(kept));
        Assert.That(UnlockLedger.Read(Profile).Templates, Does.Contain(Rifle));
    }
}
