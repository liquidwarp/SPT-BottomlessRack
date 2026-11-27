using System.Text.Json;
using HarmonyLib;
using Liquidwarp.BottomlessRack.Server;
using NUnit.Framework;
using SPTarkov.Common.Extensions;
using SPTarkov.Server.Core.DI.Routing;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Request;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;

namespace Liquidwarp.BottomlessRack.Tests;

public sealed class IntegrationTests : ServerFixture
{
    private async Task<Catalogue> ReadCatalogue(int? knownRevision = null)
    {
        var body = knownRevision.HasValue ? JsonSerializer.Serialize(new { knownRevision }) : null;
        var router = new CatalogueRouter(Json, Http, Service);
        var response = await router.HandleStaticAsync(GearProtocol.Catalogue, body, Session, "");
        using var reply = JsonDocument.Parse((string)response);
        return reply.RootElement.GetProperty("data").Deserialize<Catalogue>()!;
    }

    [Test]
    public async Task ConditionalCatalogueDiscoversNewGearAndReturnsOnlyTheReceiptForAKnownRevision()
    {
        Award(Magazine);
        var first = await ReadCatalogue();
        Assert.That(first.Templates, Is.EqualTo(new[] { Magazine }));
        await Issue(1, (Magazine, 1));
        var same = await ReadCatalogue(first.Revision);
        Assert.That(same.Revision, Is.EqualTo(first.Revision));
        Assert.That(same.Receipt.Sequence, Is.EqualTo(1));
        Assert.That(same.Templates, Is.Empty);
        Assert.That(same.WeaponBuilds, Is.Empty);

        var rifle = NewItem(Rifle, Inventory.Stash!.Value, "hideout");
        Items.AddRange([rifle, NewItem(Optic, rifle.Id, "mod_scope")]);
        var changed = await ReadCatalogue(first.Revision);
        Assert.That(changed.Revision, Is.GreaterThan(first.Revision));
        Assert.That(changed.Templates, Does.Contain(Optic));
        Assert.That(changed.WeaponBuilds[Rifle], Is.Not.Empty);
        Assert.That((await ReadCatalogue()).Templates, Is.EqualTo(changed.Templates));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RestoredLedgerIsBackfilledBeforeComparingRevisionsAndReused(bool receiptFirst)
    {
        // An older profile earned and subsequently lost its receiver before factory parts were unlocked with it.
        var old = new UnlockLedger
        {
            Templates = [Rifle],
            Receipt = new() { Sequence = 1, Lines = [new() { TemplateId = Rifle, Quantity = 1 }], RootIds = [new MongoId().ToString()] }
        };
        old.Write(Profile);
        Profile.SptData = Json.Deserialize<SptProfile>(Json.Serialize(Profile))!.SptData;
        var extensions = Profile.SptData!.GetExtensionData();
        Assert.That(extensions[MyModInfo.MOD_GUID], Is.TypeOf<JsonElement>());

        if (receiptFirst) await Service.Read(Session, false, default);
        var changed = await ReadCatalogue(old.Templates.Count);
        Assert.That(changed.Revision, Is.GreaterThan(old.Templates.Count));
        Assert.That(changed.WeaponBuilds[Rifle].Length, Is.GreaterThan(1));
        Assert.That(changed.Receipt.RootIds, Is.EqualTo(old.Receipt.RootIds));
        var restored = UnlockLedger.Read(Profile);
        Assert.That(extensions[MyModInfo.MOD_GUID], Is.SameAs(restored));
        Service.Observe(Session);
        Assert.That((await ReadCatalogue(changed.Revision)).WeaponBuilds, Is.Empty);
        Assert.That(UnlockLedger.Read(Profile), Is.SameAs(restored));
    }

    [Test]
    public void UnsupportedLedgerIsLeftIntactOnRead()
    {
        var extensions = Profile.SptData!.GetExtensionData();
        var raw = JsonSerializer.SerializeToElement(new { schemaVersion = 2, future = 42 });
        extensions[MyModInfo.MOD_GUID] = raw;
        Assert.ThrowsAsync<InvalidDataException>(async () => await ReadCatalogue());
        Assert.That(extensions[MyModInfo.MOD_GUID], Is.EqualTo(raw));
    }

    [Test]
    public async Task TraderStockAndProgressionCannotAddToAnExistingCatalogue()
    {
        var other = new MongoId();
        TradersDb[Traders.FENCE] = Offer(Traders.FENCE, Rifle);
        TradersDb[other] = Offer(other, Optic);
        Pmc.TradersInfo![other] = new() { LoyaltyLevel = 1, Unlocked = true };
        Pmc.TradersInfo[Traders.FENCE] = new() { LoyaltyLevel = 4, Unlocked = true };
        // Version 1 saved trader sightings and acquisitions together. Keep this old, unowned unlock.
        new UnlockLedger { Templates = [Magazine] }.Write(Profile);
        Profile.SptData = Json.Deserialize<SptProfile>(Json.Serialize(Profile))!.SptData;
        var first = await ReadCatalogue();
        Assert.That(first.Templates, Is.EqualTo(new[] { Magazine }));
        TradersDb[other] = Offer(other, Rifle, Optic);
        Pmc.TradersInfo[other].LoyaltyLevel = 4;
        Assert.That((await ReadCatalogue(first.Revision)).Revision, Is.EqualTo(first.Revision));
        Assert.That((await Issue(1, (Rifle, 1))).Warnings, Is.Not.Empty);
        Items.Add(NewItem(Optic, Inventory.Stash!.Value, "hideout"));
        Assert.That((await ReadCatalogue()).Templates, Is.EquivalentTo(new[] { Magazine, Optic }));
    }

    [Test]
    public async Task StockEventQueueObservesClaimThenDiscardAndSerializesAConcurrentReplay()
    {
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
        finally { new Harmony(MyModInfo.MOD_GUID).UnpatchSelf(); }
    }
}
