using System.Text.Json;
using Liquidwarp.BottomlessRack.Server;
using NUnit.Framework;
using SPTarkov.Common.Extensions;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums.Hideout;

namespace Liquidwarp.BottomlessRack.Tests;

public sealed class ProgressionTests : ServerFixture
{
    [Test]
    public async Task MagazineThroughItemEventHasStashDeltaAndDurableReceipt()
    {
        Award(Magazine);
        var result = await Issue(1, (Magazine, 1));
        Assert.That(result.Warnings, Is.Empty);
        var item = result.ProfileChanges[Session].Items!.NewItems!.Single();
        Assert.That(item.ParentId, Is.EqualTo(Inventory.Stash!.Value.ToString()));
        Assert.That(item.Location, Is.Not.Null);
        Assert.That(item.Upd!.SpawnedInSession, Is.False);
        var saved = Json.DeserializeFromFile<SptProfile>($"user/profiles/{Session}.json")!;
        var receipt = UnlockLedger.Read(saved).Receipt;
        Assert.That(receipt.RootIds, Is.EqualTo(new[] { item.Id.ToString() }));
        Assert.That(saved.CharacterData!.PmcData!.Inventory!.Items!.Last().Id, Is.EqualTo(item.Id));

        Saves.DeleteProfileById(Session);
        Saves.AddProfile(saved);
        Profile = saved;
        Assert.That((await Issue(1, (Magazine, 1))).ProfileChanges[Session].Items!.NewItems, Is.Empty);
        Assert.That((await Issue(1, (Magazine, 2))).Warnings, Is.Not.Empty);
        Assert.That((await Issue(3, (Magazine, 1))).Warnings, Is.Not.Empty);
        Assert.That((await Issue(2, (Magazine, 1))).Warnings, Is.Empty);
        Assert.That(Items.Select(i => i.Id).Distinct().Count(), Is.EqualTo(3));
    }

    [Test]
    public async Task ShotgunnerSuppliesABareMossbergAndPreservesTheFactoryBuildAndReceipt()
    {
        const string mossberg = "5e870397991fd70db46995c8", speedFeed = "5e87076ce2db31558c75a11d", moe = "5eea21647547d6330471b3c9";
        Award(mossberg, moe);
        await Issue(1, (mossberg, 1));
        var original = Items.Single(i => i.Template == mossberg);
        var originalGuard = Items.Single(i => i.Template == speedFeed);
        var bare = new SpawnLine { TemplateId = mossberg, Quantity = 1, BareWeapon = true };
        var request = new SpawnRequest { Sequence = 2, Lines = [bare, new() { TemplateId = moe, Quantity = 1 }] };
        var result = await Issue(request);
        Assert.That(result.Warnings, Is.Empty);
        var added = result.ProfileChanges[Session].Items!.NewItems!;
        Assert.That(added.Select(i => i.Template.ToString()), Is.EquivalentTo(new[] { mossberg, moe }));
        Assert.That(added.All(i => i.ParentId == Inventory.Stash!.Value.ToString()), Is.True);
        Assert.That(originalGuard.ParentId, Is.EqualTo(original.Id.ToString()));
        Assert.That((await Issue(request)).ProfileChanges[Session].Items!.NewItems, Is.Empty);
        Assert.That((await Issue(2, (mossberg, 1), (moe, 1))).Warnings, Is.Not.Empty);
        Assert.That((await Issue(new SpawnRequest { Sequence = 3, Lines = [new() { TemplateId = moe, Quantity = 1, BareWeapon = true }] })).Warnings, Is.Not.Empty);
    }

    [Test]
    public async Task RejectsLockedOrInvalidRequestsWithoutConsumingSequence()
    {
        Award(Magazine);
        foreach (var response in new[] { await Issue(1, (Magazine, 1), (Optic, 1)), await Issue(1, (Magazine, 0)),
            await Issue(1, (Magazine, int.MaxValue), (Magazine, 1)), await Issue(1, ("bad id", 1)), await Issue(0, (Magazine, 1)) })
            Assert.That(response.Warnings, Is.Not.Empty);
        Assert.That(UnlockLedger.Read(Profile).Receipt.Sequence, Is.Zero);
        Assert.That(Items.Count, Is.EqualTo(1));
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(5)]
    public async Task MagazineBatchesAreEmptyStashCopiesOfTheFittedModel(int copies)
    {
        var weapon = NewItem(Rifle, Inventory.Equipment!.Value, "FirstPrimaryWeapon");
        var fitted = NewItem(Magazine, weapon.Id, "mod_magazine");
        var rounds = NewItem(Ammo, fitted.Id, "cartridges") with { Upd = new() { StackObjectsCount = 17 } };
        Items.AddRange([weapon, fitted, rounds]);
        Service.Observe(Session);

        var result = await Issue(1, (fitted.Template, copies));
        Assert.That(result.Warnings, Is.Empty);
        var added = result.ProfileChanges[Session].Items!.NewItems!;
        Assert.That(added, Has.Count.EqualTo(copies));
        Assert.That(added.All(i => i.Template == fitted.Template && i.ParentId == Inventory.Stash!.Value.ToString() &&
            i.Location != null && i.Upd!.SpawnedInSession == false), Is.True);
        Assert.That(Items.Select(i => i.Id).Distinct().Count(), Is.EqualTo(4 + copies));
        Assert.That(Items.Single(i => i.ParentId == weapon.Id.ToString()).Id, Is.EqualTo(fitted.Id));
        var retainedRounds = Items.Single(i => i.Template == Ammo);
        Assert.That(retainedRounds.ParentId, Is.EqualTo(fitted.Id.ToString()));
        Assert.That(retainedRounds.Upd!.StackObjectsCount, Is.EqualTo(17));
    }

    [Test]
    public async Task OnlyTheMainStashCountsAndLatePlacementFailureRollsBackWholeBatch()
    {
        Award(Magazine);
        // The real SPT grid has room for one magazine after applying a temporary stash-size template.
        var original = Templates.Items[StashTpl];
        var grid = original.Properties!.Grids!.First();
        var size = Templates.Items[Magazine].Properties!;
        var tiny = new MongoId();
        Templates.Items[tiny] = original with { Id = tiny, Properties = original.Properties with
            { Grids = [grid with { Properties = grid.Properties! with { CellsH = size.Width, CellsV = size.Height } }] } };
        Items[0].Template = tiny;
        Items.Add(NewItem("544a5caa4bdc2d1a388b4568", Inventory.Equipment!.Value, "TacticalVest"));
        Items.Add(NewItem("544a5cde4bdc2d39388b456b", Inventory.Equipment.Value, "Backpack"));
        var failed = await Issue(1, (Magazine, 2));
        Assert.That(failed.Warnings!.Single().ErrorMessage, Is.EqualTo("Not enough stash space."));
        Assert.That(failed.ProfileChanges[Session].Items!.NewItems, Is.Empty);
        Assert.That(Items.Any(i => i.Template == Magazine), Is.False);
        Assert.That((await Issue(1, (Magazine, 1))).Warnings, Is.Empty);
        Assert.That((await Issue(2, (Magazine, 1))).Warnings, Is.Not.Empty);
    }

    [Test]
    public void WeaponUnlockAddsFactoryPartsAndRecoveredPartsIndependently()
    {
        var custom = NewItem(Rifle, Inventory.Equipment!.Value.ToString(), "FirstPrimaryWeapon");
        Items.Add(custom);
        Items.Add(NewItem(Optic, custom.Id, "mod_scope"));
        Service.Observe(Session);
        var ledger = UnlockLedger.Read(Profile);
        var fresh = Gear.Create(Rifle);
        Assert.That(fresh.Count, Is.GreaterThan(4));
        Assert.That(ledger.Templates, Does.Contain(Optic));
        Assert.That(fresh.Select(i => i.Template.ToString()), Does.Not.Contain(Optic));
        Assert.That(fresh.All(i => ledger.Templates.Contains(i.Template.ToString())), Is.True);
        Assert.That(fresh.Select(i => i.Id).Intersect(Gear.Create(Rifle).Select(i => i.Id)), Is.Empty);
        Assert.That(fresh.Skip(1).All(i => fresh.Any(p => p.Id.ToString() == i.ParentId)), Is.True);
        Assert.That(fresh.All(i => i.Upd!.SpawnedInSession == false), Is.True);
        Assert.That(fresh[0].Upd!.Repairable!.Durability, Is.EqualTo(fresh[0].Upd!.Repairable!.MaxDurability));
        Assert.That(fresh.Any(i => i.SlotId is "cartridges" or "patron_in_weapon"), Is.False);
        Assert.That(Gear.Eligible(Ammo), Is.False);
    }

    [Test]
    public async Task UnlockExaminesRecoveredAndFactoryGearAndPreservesReadEntries()
    {
        var factoryPart = Gear.Recipe(Rifle).Skip(1).First(p => Gear.Eligible(p.Template)).Template;
        var original = Templates.Items[factoryPart];
        var receiver = new MongoId();
        Templates.Items[factoryPart] = original with { Properties = original.Properties! with { ExaminedByDefault = false } };
        Templates.Items[receiver] = Templates.Items[Rifle] with
        {
            Id = receiver, Properties = Templates.Items[Rifle].Properties! with { ExaminedByDefault = false }
        };
        try
        {
            Pmc.Encyclopedia = new() { [factoryPart] = true };
            var scav = Profile.CharacterData!.ScavData = new() { Encyclopedia = [] };
            Items.Add(NewItem(Rifle, Inventory.Stash!.Value, "hideout"));
            Items.Add(NewItem(receiver, Inventory.Stash.Value, "hideout"));
            var catalogue = await Service.Read(Session, true, default);
            Assert.That(Pmc.Encyclopedia[factoryPart], Is.True);
            Assert.That(scav.Encyclopedia[factoryPart], Is.False);
            Assert.That(Pmc.Encyclopedia[receiver], Is.False);
            Assert.That(scav.Encyclopedia[receiver], Is.False);
            Assert.That(catalogue.Examined, Does.Contain(factoryPart.ToString()).And.Contain(receiver.ToString()));
            Assert.That(catalogue.Templates, Does.Not.Contain(receiver.ToString()));
            Assert.That(Pmc.Info!.Experience, Is.Zero);
            scav.Encyclopedia[receiver] = true;
            Service.Observe(Session);
            Assert.That(scav.Encyclopedia[receiver], Is.True);
            Assert.That(Pmc.Encyclopedia.ContainsKey(Ammo), Is.False);
        }
        finally
        {
            Templates.Items[factoryPart] = original;
            Templates.Items.Remove(receiver);
        }
    }

    [Test]
    public void ArmorIncludesIntegralInsertsAndExcludesRemovablePlates()
    {
        var armor = Templates.Items.Values.First(t => t.Type == "Item" && t.Properties?.Slots?.Any(s =>
            s.Properties?.Filters?.Any(f => f.Locked == true && f.Plate.HasValue) == true) == true &&
            Gear.Eligible(t.Id) && Gear.Recipe(t.Id).Length > 1);
        var armorTree = Gear.Create(armor.Id);
        Assert.That(armorTree.Skip(1).All(i => !Gear.Eligible(i.Template)), Is.True);
        Assert.That(armorTree.Skip(1).All(i => i.Upd!.Repairable!.Durability == i.Upd.Repairable.MaxDurability), Is.True);
    }

    [Test]
    public void PendingMailPartialClaimsRacksAndDiscardingLastCopy()
    {
        var unclaimed = new MongoId();
        var magazine = NewItem(Magazine, unclaimed, "main");
        var optic = NewItem(Optic, unclaimed, "main");
        Items.AddRange([magazine, optic]);
        Service.Observe(Session);
        Assert.That(UnlockLedger.Read(Profile).Templates, Is.Empty);
        var caseItem = NewItem(StashTpl, Inventory.Stash!.Value, "hideout");
        Items.Add(caseItem);
        magazine.ParentId = caseItem.Id;
        Service.Observe(Session);
        Items.Remove(magazine);
        Service.Observe(Session);
        Assert.That(UnlockLedger.Read(Profile).Templates, Is.EquivalentTo(new[] { Magazine }));
        var rack = new MongoId();
        Inventory.HideoutAreaStashes = new() { [((int)HideoutAreas.WeaponStand).ToString()] = rack };
        optic.ParentId = rack;
        Service.Observe(Session);
        Assert.That(UnlockLedger.Read(Profile).Templates, Does.Contain(Optic));
        Assert.That(UnlockLedger.Read(new SptProfile { SptData = new() }).Templates, Is.Empty);
    }

    [Test]
    public async Task CatalogueReadUnlocksPmcEquipmentAndTransferredScavGear()
    {
        var scavEquipment = new MongoId();
        var optic = NewItem(Optic, scavEquipment, "mod_scope");
        Profile.CharacterData!.ScavData = new() { Inventory = new() { Equipment = scavEquipment, Items = [optic] } };
        Items.Add(NewItem(Magazine, Inventory.Equipment!.Value, "TacticalVest"));

        var first = await Service.Read(Session, true, default);
        Assert.That(first.Templates, Is.EquivalentTo(new[] { Magazine }));

        Profile.CharacterData.ScavData.Inventory.Items!.Remove(optic);
        optic.ParentId = Inventory.Stash!.Value;
        Items.Add(optic);
        var second = await Service.Read(Session, true, default);
        Assert.That(second.Templates, Is.EquivalentTo(new[] { Magazine, Optic }));
    }

    [Test]
    public void ProfileSnapshotRoundTripsThroughUnmodifiedSptSerializer()
    {
        Profile.SptData!.GetExtensionData()["unrelated.mod"] = true;
        var earlier = Json.Serialize(Profile);
        Award(Optic);
        var latest = Json.Deserialize<SptProfile>(Json.Serialize(Profile))!;
        Assert.That(UnlockLedger.Read(latest).Templates, Does.Contain(Optic));
        Assert.That(latest.SptData!.GetExtensionData().ContainsKey("unrelated.mod"), Is.True);
        Assert.That(UnlockLedger.Read(Json.Deserialize<SptProfile>(earlier)!).Templates, Is.Empty);
        Assert.That(JsonDocument.Parse(Json.Serialize(Gear.Create(Magazine)[0])!).RootElement.GetRawText(),
            Does.Not.Contain("bottomlessrack"));
    }

    [Test]
    public async Task ReceiptReadRetriesFailedDiskWriteWithoutIssuingTwice()
    {
        Award(Magazine);
        var filename = $"user/profiles/{Session}.json";
        Directory.CreateDirectory(filename);
        try { Assert.CatchAsync(async () => await Issue(1, (Magazine, 1))); }
        finally { Directory.Delete(filename); }
        var recovered = await Service.Read(Session, false, default);
        Assert.That(recovered.Receipt.RootIds, Has.Length.EqualTo(1));
        var saved = Json.DeserializeFromFile<SptProfile>(filename)!;
        Assert.That(UnlockLedger.Read(saved).Receipt.RootIds, Is.EqualTo(recovered.Receipt.RootIds));
        Assert.That((await Issue(1, (Magazine, 1))).ProfileChanges[Session].Items!.NewItems, Is.Empty);
        Assert.That(Items.Count(i => i.Template == Magazine), Is.EqualTo(1));
    }

    [Test]
    public void ModdedReceiverWithNoPresetStaysEarnedAcrossItemModRemoval()
    {
        var tpl = new MongoId();
        var custom = Templates.Items[Rifle] with { Id = tpl };
        Templates.Items.Add(tpl, custom);
        try
        {
            Award(tpl);
            Assert.That(Gear.CanIssue(tpl), Is.False);
            var snapshot = Json.Serialize(Profile);
            Templates.Items.Remove(tpl);
            var absent = new GearDefinitions(Templates, Globals, TradersDb, ItemHelper);
            Assert.That(absent.Eligible(tpl), Is.False);
            Assert.That(absent.IsWeapon(tpl), Is.False);
            Assert.That(UnlockLedger.Read(Json.Deserialize<SptProfile>(snapshot)!).Templates, Does.Contain(tpl.ToString()));
            Templates.Items.Add(tpl, custom);
            Assert.That(new GearDefinitions(Templates, Globals, TradersDb, ItemHelper).Eligible(tpl), Is.True);
        }
        finally { Templates.Items.Remove(tpl); }
    }

    [Test]
    public void BuildPlannerCountsRepeatedPartsAndUsesOwnedItemsFirst()
    {
        var missing = GearProtocol.Missing([Optic, Magazine, Optic], [Optic, Magazine]);
        Assert.That(missing.Select(l => (l.TemplateId, l.Quantity)), Is.EqualTo(new[] { (Optic, 1) }));
        Assert.That(GearProtocol.Missing([Magazine], [Rifle, Magazine]), Is.Empty);
    }
}
