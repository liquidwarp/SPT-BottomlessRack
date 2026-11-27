using System.Text.Json;
using HarmonyLib;
using Liquidwarp.UnlockGearProgression.Server;
using NUnit.Framework;
using SPTarkov.Server.Core.DI.Routing;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Request;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Enums;

namespace Liquidwarp.UnlockGearProgression.Tests;

public sealed class IntegrationTests : ServerFixture
{
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
    public async Task StockEventQueueObservesClaimThenDiscardAndSerializesAConcurrentReplay()
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
                new ItemRouteAction<BaseInteractionRequestData>("DiscardFixture", (_, _, _, _, output, _) =>
                {
                    Items.Remove(mail);
                    return ValueTask.FromResult(output);
                }));
            var stock = Callbacks(actions, new SpawnRouter(Service));
            var payload = Json.Deserialize<ItemEventRouterRequest>("""{"data":[{"Action":"ClaimFixture"},{"Action":"DiscardFixture"}]}""")!;
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
                JsonSerializer.SerializeToElement(new { Action = "DiscardFixture" }),
                JsonSerializer.SerializeToElement(new { Action = GearProtocol.Action, sequence = 2,
                    lines = new[] { new { templateId = Rifle, quantity = 1 } } })] };
            using var refused = JsonDocument.Parse(await stock.HandleEvents("", invalid, Session, default));
            Assert.That(refused.RootElement.GetProperty("data").GetProperty("warnings")[0].GetProperty("index").GetInt32(), Is.EqualTo(1));
        }
        finally { new Harmony(guid).UnpatchSelf(); }
    }
}
