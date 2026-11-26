using System.Text.Json;
using HarmonyLib;
using Liquidwarp.UnlockGearProgression.Server;
using NUnit.Framework;
using SPTarkov.Server.Core.DI.Routing;
using SPTarkov.Server.Core.Helpers.Commerce;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Quest;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Helpers.Traders;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Request;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Trade;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Services.Commerce;

namespace Liquidwarp.UnlockGearProgression.Tests;

public sealed class IntegrationTests : ServerFixture
{
    private sealed class TestRouter(params ItemRouteAction[] routes) : ItemEventRouter(routes);

    private static Trader Offer(MongoId trader, params string[] outputs)
    {
        var items = outputs.Select(t => NewItem(t, "hideout", "hideout")).ToList();
        foreach (var item in items) item.Upd = new() { StackObjectsCount = 0 };
        return new()
        {
            Base = new() { Id = trader, Currency = CurrencyType.RUB, UnlockedByDefault = true, LoyaltyLevels = [] },
            Assort = new() { Items = items, LoyalLevelItems = items.ToDictionary(i => i.Id, _ => 1),
                BarterScheme = items.ToDictionary(i => i.Id, _ => new List<List<BarterScheme>> { new() { new() { Template = Rifle, Count = 1 } } }) },
            QuestAssort = [], Dialogue = []
        };
    }

    [Test]
    public async Task FenceIsSkippedOnEntryAndRotationButOwnershipOrAnotherTraderUnlocks()
    {
        var other = new MongoId();
        TradersDb[Traders.FENCE] = Offer(Traders.FENCE, Optic, Rifle);
        TradersDb[other] = Offer(other, Magazine);
        Pmc.TradersInfo![other] = new() { LoyaltyLevel = 1, Unlocked = true };
        Pmc.TradersInfo[Traders.FENCE] = new() { LoyaltyLevel = 4, Unlocked = true };
        Activity.AddActiveProfile(Session, 0);
        var first = await Service.Read(Session, true, default);
        Assert.That(first.Templates, Is.EqualTo(new[] { Magazine }));
        Service.Restock(Traders.FENCE);
        Assert.That(UnlockLedger.Read(Profile).Templates, Does.Not.Contain(Optic));
        TradersDb[other] = Offer(other, Optic);
        Service.Restock(other);
        Assert.That(UnlockLedger.Read(Profile).Templates, Does.Contain(Optic));
        Items.Add(NewItem(Rifle, Inventory.Stash!.Value, "hideout"));
        Service.Observe(Session);
        Assert.That(UnlockLedger.Read(Profile).Templates, Does.Contain(Rifle));
    }

    [Test]
    public async Task StockEventQueueObservesClaimThenSaleAndSerializesAConcurrentReplay()
    {
        const string guid = "net.liquidwarp.unlockgearprogression";
        var mod = new Mod(Service, TestLog.For<Mod>());
        try
        {
            await mod.OnLoadAsync();
            var mail = NewItem(Optic);
            var actions = new TestRouter(
                new ItemRouteAction<BaseInteractionRequestData>("ClaimFixture", (_, _, _, _, output, _) =>
                {
                    mail.ParentId = Inventory.Stash!.Value;
                    Items.Add(mail);
                    return ValueTask.FromResult(output);
                }),
                new ItemRouteAction<BaseInteractionRequestData>("SellFixture", (_, _, _, _, output, _) =>
                {
                    Items.Remove(mail);
                    return ValueTask.FromResult(output);
                }));
            var stock = Callbacks(actions, new SpawnRouter(Service));
            var payload = Json.Deserialize<ItemEventRouterRequest>("""{"data":[{"Action":"ClaimFixture"},{"Action":"SellFixture"}]}""")!;
            await stock.HandleEvents("", payload, Session, default);
            Assert.That(Items, Does.Not.Contain(mail));
            Assert.That(UnlockLedger.Read(Profile).Templates, Does.Contain(Optic));
            var spawn = new ItemEventRouterRequest { Data = [JsonSerializer.SerializeToElement(
                new { Action = GearProtocol.Action, sequence = 1, lines = new[] { new { templateId = Optic, quantity = 1 } } })] };
            var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
                Task.Run(async () => await stock.HandleEvents("", spawn, Session, default))));
            var counts = results.Select(body =>
            {
                using var json = JsonDocument.Parse(body);
                return json.RootElement.GetProperty("data").GetProperty("profileChanges").GetProperty(Session.ToString())
                    .GetProperty("items").GetProperty("new").GetArrayLength();
            });
            Assert.That(counts, Is.EquivalentTo(new[] { 0, 1 }));
            Assert.That(Items.Count(i => i.Template == Optic), Is.EqualTo(1));

            var invalid = new ItemEventRouterRequest { Data = [
                JsonSerializer.SerializeToElement(new { Action = "SellFixture" }),
                JsonSerializer.SerializeToElement(new { Action = GearProtocol.Action, sequence = 2,
                    lines = new[] { new { templateId = Rifle, quantity = 1 } } })] };
            using var refused = JsonDocument.Parse(await stock.HandleEvents("", invalid, Session, default));
            Assert.That(refused.RootElement.GetProperty("data").GetProperty("warnings")[0].GetProperty("index").GetInt32(), Is.EqualTo(1));
        }
        finally { new Harmony(guid).UnpatchSelf(); }
    }

    [Test]
    public async Task StockTradePaysForEarnedAndSpawnedMagazinesOnIdenticalTerms()
    {
        Award(Magazine);
        var spawned = (await Issue(1, (Magazine, 1))).ProfileChanges[Session].Items!.NewItems!.Single();
        var ordinary = NewItem(Magazine, Inventory.Stash!.Value, "hideout");
        ordinary.Upd = new() { StackObjectsCount = 1, SpawnedInSession = false };
        Items.Add(ordinary);
        var cash = NewItem(Money.ROUBLES, Inventory.Stash.Value, "hideout");
        cash.Upd = new() { StackObjectsCount = 1 };
        Items.Add(cash);
        TradersDb[Traders.PRAPOR] = Offer(Traders.PRAPOR);
        Pmc.TradersInfo![Traders.PRAPOR] = new() { SalesSum = 0, LoyaltyLevel = 1, Standing = 0, Unlocked = true };
        var handbook = new HandbookHelper(TestLog.For<HandbookHelper>(), Templates, null!, Cloner);
        var trader = new TraderHelper(TestLog.For<TraderHelper>(), Globals, TradersDb, Profiles, handbook, null!, null!, new(), null!, null!);
        var quests = new QuestHelper(TestLog.For<QuestHelper>(), Templates, new(), null!, null!, Profiles, null!, null!, null!, null!, null!, null!, Cloner);
        var payments = new PaymentService(TestLog.For<PaymentService>(), TradersDb, Http, handbook, trader, ItemHelper, Placement, null!, null!, null!);
        var trade = new TradeHelper(TestLog.For<TradeHelper>(), trader, ItemHelper, quests, payments, null!, null!, Http, Placement, null!, null!, null!, Cloner);
        foreach (var mag in new[] { ordinary, spawned })
        {
            var output = new ItemEventRouterResponse { Warnings = [], ProfileChanges = new() { [Session] = new() { Items = new()
                { NewItems = [], ChangedItems = [], DeletedItems = [] } } } };
            var request = new ProcessSellTradeRequestData { TransactionId = Traders.PRAPOR, Price = 250,
                Items = [new() { Id = mag.Id, Count = 1 }] };
            trade.SellItem(Pmc, Pmc, request, Session, output);
            Assert.That(output.Warnings, Is.Empty);
            Assert.That(Items, Does.Not.Contain(mag));
        }
        Assert.That(cash.Upd.StackObjectsCount, Is.EqualTo(501));
        Assert.That(Pmc.TradersInfo[Traders.PRAPOR].SalesSum, Is.EqualTo(500));
    }
}
