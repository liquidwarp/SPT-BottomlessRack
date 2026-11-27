using System.Text.Json;
using System.Text.Json.Nodes;
using Path = System.IO.Path;
using Liquidwarp.BottomlessRack.Server;
using NUnit.Framework;
using SPTarkov.Common.Extensions;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;

namespace Liquidwarp.BottomlessRack.Tests;

public sealed class WeaponRecipeTests : ServerFixture
{
    private static Slot Mount(string name, bool required, params MongoId[] allowed) => new()
    {
        Name = name, Required = required, MergeSlotWithChildren = true,
        Properties = new() { Filters = [new() { Filter = [.. allowed] }] }
    };

    private MongoId Clone(string tpl, params Slot[] slots)
    {
        var id = new MongoId();
        var original = Templates.Items[tpl];
        Templates.Items.Add(id, original with { Id = id, Properties = original.Properties! with { Slots = slots } });
        return id;
    }

    private TraderAssort Assort(MongoId trader)
    {
        return (TradersDb[trader] = new()
        {
            Base = new() { Id = trader }, QuestAssort = [], Dialogue = [],
            Assort = new() { Items = [], BarterScheme = [], LoyalLevelItems = [] }
        }).Assort!;
    }

    private static Item Offer(TraderAssort assort, MongoId receiver, int? loyalty, params (string Slot, MongoId Part)[] parts)
    {
        var root = NewItem(receiver, "hideout", "hideout");
        root.Upd = new() { StackObjectsCount = 42, UnlimitedCount = true, SpawnedInSession = true };
        assort.Items.Add(root);
        foreach (var (slot, part) in parts) assort.Items.Add(NewItem(part, root.Id, slot));
        assort.BarterScheme.Add(root.Id, [[new() { Template = Rifle, Count = 2 }]]);
        if (loyalty.HasValue) assort.LoyalLevelItems.Add(root.Id, loyalty.Value);
        return root;
    }

    [Test]
    public async Task OnlyEarnedReceiversExposeRecipesAndReconciliationCreditsSoldReceivers()
    {
        var receiver = Clone(Rifle, Mount("mod_magazine", true, Magazine));
        var bare = Clone(Rifle);
        var broken = Clone(Rifle, Mount("mod_scope", true, Optic));
        var vendor = Assort(new MongoId());
        var root = Offer(vendor, receiver, 4, ("mod_magazine", Magazine));
        var magazine = vendor.Items.Last();
        vendor.Items.Add(NewItem(Ammo, magazine.Id, "cartridges"));
        vendor.Items.Add(NewItem(Ammo, root.Id, Templates.Items[Rifle].Properties!.Chambers!.First().Name));
        Offer(Assort(Traders.FENCE), broken, 1, ("mod_scope", Optic));
        await LoadGear();
        // No trader relation. A full read must never grant these offers or leak their recipes.
        Assert.That((await Service.Read(Session, true, default)).WeaponBuilds, Is.Empty);
        Assert.That((await Issue(1, (receiver, 1))).Warnings, Is.Not.Empty);
        new UnlockLedger { Templates = [receiver.ToString(), bare.ToString(), broken.ToString()] }.Write(Profile);
        vendor.Items.Clear(); // A restock before the player's first full read.
        Assert.That((await Service.Read(Session, false, default)).WeaponBuilds, Is.Empty);
        var catalogue = await Service.Read(Session, true, default);
        Assert.That(catalogue.Templates, Is.EquivalentTo(new[] { receiver.ToString(), bare.ToString(), Magazine }));
        Assert.That(catalogue.WeaponBuilds.Keys, Is.EquivalentTo(new[] { receiver.ToString(), bare.ToString() }));
        Assert.That(catalogue.WeaponBuilds[bare], Has.Length.EqualTo(1));
        var wire = Json.Deserialize<Catalogue>(Json.Serialize(catalogue))!;
        var preview = wire.WeaponBuilds[receiver];
        Assert.That(preview.Select(p => (p.TemplateId, p.ParentIndex, p.SlotId)),
            Is.EqualTo(new[] { (receiver.ToString(), -1, (string?)null), (Magazine, 0, "mod_magazine") }));
        Assert.That(UnlockLedger.Read(Profile).Templates, Does.Contain(broken.ToString()));
        Assert.That((await Service.Read(Session, true, default)).Revision, Is.EqualTo(catalogue.Revision));
        Assert.That((await Issue(1, (receiver, 1), (broken, 1))).Warnings, Is.Not.Empty);
        Assert.That(Items, Has.Count.EqualTo(1));

        var result = await Issue(1, (receiver, 2));
        Assert.That(result.Warnings, Is.Empty);
        var copies = result.ProfileChanges[Session].Items!.NewItems!;
        Assert.That(copies.Select(i => i.Id).Distinct().Count(), Is.EqualTo(4));
        foreach (var body in copies.Where(i => i.Template == receiver))
        {
            var child = copies.Single(i => i.ParentId == body.Id.ToString());
            Assert.That((child.Template.ToString(), child.SlotId), Is.EqualTo((preview[1].TemplateId, preview[1].SlotId)));
            Assert.That(body.Upd!.StackObjectsCount, Is.EqualTo(1));
            Assert.That(body.Upd.UnlimitedCount, Is.Not.True);
            Assert.That(body.Upd.SpawnedInSession, Is.False);
        }
    }

    [Test]
    public async Task StartupCachesIntegralInsertsOnFittedWeaponParts()
    {
        var insert = Clone(Optic);
        Templates.Items[insert].Parent = BaseClasses.BUILT_IN_INSERTS;
        var pocket = Mount("soft_armor", false, insert);
        pocket.Properties!.Filters!.Single().Locked = true;
        var attachment = Clone(Optic, pocket);
        var receiver = Clone(Rifle, Mount("mod_mount", true, attachment));
        Offer(Assort(new MongoId()), receiver, 1, ("mod_mount", attachment));
        var loaded = await LoadGear();

        // The first player lookup must already have the complete startup recipe.
        Templates.Items[attachment].Properties!.Slots = [];
        var parts = loaded.Recipe(receiver);
        Assert.That(parts, Is.EqualTo(new RecipePart[]
        {
            new(receiver, -1, null), new(attachment, 0, "mod_mount"), new(insert, 1, "soft_armor")
        }));
        Assert.That(loaded.Recipe(receiver), Is.SameAs(parts));
    }

    [Test]
    public async Task CompleteOffersRankByPartCountThenLoyaltyThenSlotPathAndTemplate()
    {
        var left = Clone(Optic);
        var right = Clone(Optic);
        var receiver = Clone(Rifle, Mount("mod_scope", true, left, right), Mount("mod_magazine", false, Magazine));
        var lower = string.CompareOrdinal(left.ToString(), right.ToString()) < 0 ? left : right;
        var higher = lower == left ? right : left;
        async Task<MongoId> Winner() => (await LoadGear()).Recipe(receiver)[1].Template;
        foreach (var reverse in new[] { false, true })
        {
            TradersDb.Clear();
            var vendor = Assort(new MongoId());
            Offer(vendor, receiver, 1, ("mod_scope", lower), ("mod_magazine", Magazine));
            Offer(vendor, receiver, null, ("mod_scope", lower));
            Offer(vendor, receiver, 4, ("mod_scope", higher));
            Assert.That(await Winner(), Is.EqualTo(higher));
            Offer(vendor, receiver, 4, ("mod_scope", lower));
            if (reverse) vendor.Items.Reverse();
            Assert.That(await Winner(), Is.EqualTo(lower));
        }
    }

    [Test]
    public async Task MalformedOrIncompatibleOffersCannotSupplyAVitalPart()
    {
        var mount = Clone(Optic, Mount("mod_scope", true, Optic));
        var receiver = Clone(Rifle, Mount("mod_mount", true, mount));
        var vendor = Assort(new MongoId());
        Offer(vendor, receiver, 1); // Missing receiver slot.
        Offer(vendor, receiver, 1, ("mod_mount", mount)); // Missing nested slot.
        Offer(vendor, receiver, 1, ("mod_mount", Optic)); // Wrong slot filter.
        Offer(vendor, receiver, 1, ("invented", mount));
        var duplicate = Offer(vendor, receiver, 1, ("mod_mount", mount), ("mod_mount", mount));
        foreach (var child in vendor.Items.Where(i => i.ParentId == duplicate.Id.ToString()).ToArray())
            vendor.Items.Add(NewItem(Optic, child.Id, "mod_scope"));
        var unlisted = Offer(vendor, receiver, 1, ("mod_mount", mount));
        vendor.BarterScheme.Remove(unlisted.Id);
        vendor.Items.Add(NewItem(Optic, vendor.Items.Last().Id, "mod_scope"));
        async Task<bool> Available() => (await LoadGear()).CanIssue(receiver);
        Assert.That(await Available(), Is.False);
        var valid = Offer(vendor, receiver, 2, ("mod_mount", mount));
        var childMount = vendor.Items.Last();
        vendor.Items.Add(NewItem(Optic, childMount.Id, "mod_scope"));
        Assert.That(await Available(), Is.True);
        vendor.Items.Add(NewItem(Magazine, valid.Id, "bad_slot") with { Id = childMount.Id });
        Assert.That(await Available(), Is.False);
        vendor.Items.RemoveAt(vendor.Items.Count - 1);
        Templates.Items[mount].Properties!.ConflictingItems = [receiver];
        Assert.That(await Available(), Is.False);
    }

    [Test]
    public async Task PublishedDefaultTakesPriorityOverSimplerOffers()
    {
        var preset = Globals.ItemPresets.Values.First(p => p.Encyclopedia == Rifle);
        var vendor = Assort(new MongoId());
        var receiver = Clone(Rifle);
        Offer(vendor, receiver, 1);
        // A usable published default with a part has priority over the complete bare offer.
        Templates.Items[receiver].Properties!.Slots = [Mount("mod_magazine", false, Magazine)];
        var root = NewItem(receiver);
        var id = new MongoId();
        Globals.ItemPresets.Add(id, preset with { Id = id, Encyclopedia = receiver,
            Items = [root, NewItem(Magazine, root.Id, "mod_magazine")] });
        try
        {
            var loaded = await LoadGear();
            Assert.That(loaded.Recipe(receiver).Select(p => p.Template), Is.EqualTo(new MongoId[] { receiver, Magazine }));
            Globals.ItemPresets[id].Items.Add(NewItem(Optic, new MongoId(), "mod_scope"));
            var recovered = await LoadGear();
            Assert.That(recovered.Recipe(receiver).Select(p => p.Template), Is.EqualTo(new MongoId[] { receiver }));
        }
        finally { Globals.ItemPresets.Remove(id); }
    }

    [Test]
    public async Task SlotExclusionsApplyToNestedPartsAndEmptyFiltersMatchNothing()
    {
        var mount = Clone(Optic, Mount("mod_scope", true, Optic));
        var slot = Mount("mod_mount", true, mount);
        var receiver = Clone(Rifle, slot);
        var vendor = Assort(new MongoId());
        Offer(vendor, receiver, 1, ("mod_mount", mount));
        vendor.Items.Add(NewItem(Optic, vendor.Items.Last().Id, "mod_scope"));
        var filter = slot.Properties!.Filters!.Single();
        filter.GetExtensionData()["ExcludedFilter"] = JsonSerializer.SerializeToElement(new[] { Optic });
        Assert.That((await LoadGear()).CanIssue(receiver), Is.False);
        filter.GetExtensionData().Clear();
        Assert.That((await LoadGear()).CanIssue(receiver), Is.True);
        filter.Filter = [BaseClasses.MOD];
        Assert.That((await LoadGear()).CanIssue(receiver), Is.True);
        filter.GetExtensionData()["ExcludedFilter"] = JsonSerializer.SerializeToElement(new[] { BaseClasses.MOD.ToString() });
        Assert.That((await LoadGear()).CanIssue(receiver), Is.False);
        filter.GetExtensionData().Clear();
        filter.Filter = [];
        Assert.That((await LoadGear()).CanIssue(receiver), Is.False);
    }

    [Test]
    public void StockWeaponDefaultsKeepTheirMountedGearAtTheSameSlotPaths()
    {
        foreach (var preset in Globals.ItemPresets.Values.Where(p => p.Encyclopedia is { } id && Gear.IsWeapon(id))
                     .DistinctBy(p => p.Encyclopedia))
        {
            var root = preset.Items.Single(i => i.ParentId == null);
            var expected = new List<(string, MongoId)>();
            var children = preset.Items.ToLookup(i => i.ParentId);
            void Walk(Item parent, string path)
            {
                expected.Add((path, parent.Template));
                foreach (var child in children[parent.Id.ToString()])
                    if (Gear.Eligible(child.Template)) Walk(child, path + "/" + child.SlotId);
            }
            Walk(root, "");
            var actual = new List<(string Path, MongoId Template)>();
            foreach (var part in Gear.Recipe(root.Template))
                actual.Add((part.ParentIndex < 0 ? "" : actual[part.ParentIndex].Path + "/" + part.Slot, part.Template));
            Assert.That(actual, Is.EquivalentTo(expected), preset.Name);
        }
    }

    [Test]
    public async Task InstalledMsgaOffersProduceTheMp700ShortyAndWoodMp12()
    {
        var mod = Path.Combine(Environment.GetEnvironmentVariable("BOTTOMLESSRACK_TEST_SPT_PATH")!, "SPT_Runtime/user/mods/makeshotgunsgreatagain/db");
        if (!Directory.Exists(mod)) Assert.Ignore("Optional Make Shotguns Great Again fixture is not installed.");
        const string full = "6910e86bbcb9218635891ae2", shorty = "6a8103d9e2f14c7b9a5d3e01", mp12 = "691098356f593043295f6ddc";
        // Apply the six installed template overlays in memory. Deliberately omit the MP-12 global default.
        foreach (var file in new[] { "MP700", "MP700_barrel", "MP700_SHORTY", "MP700_SHORTY_barrel", "mp12", "mp12_600mm_barrel" })
            foreach (var (id, overlay) in JsonNode.Parse(File.ReadAllText(Path.Combine(mod, "CustomItems", file + ".json")))!.AsObject())
            {
                var original = Templates.Items[overlay!["itemTplToClone"]!.GetValue<string>()];
                var props = JsonNode.Parse(Json.Serialize(original.Properties)!)!.AsObject();
                foreach (var (key, value) in overlay["overrideProperties"]!.AsObject()) props[key] = value?.DeepClone();
                Templates.Items[id] = original with { Id = id, Parent = overlay["parentId"]!.GetValue<string>(),
                    Properties = Json.Deserialize<TemplateItemProperties>(props.ToJsonString()) };
            }
        var vendor = Assort(Traders.JAEGER);
        foreach (var file in new[] { "MP-12 polymer", "Nitro", "MP700_SHORTY", "MP-12 wood" })
        {
            var assort = Json.DeserializeFromFile<Dictionary<MongoId, TraderAssort>>(
                Path.Combine(mod, "weaponPresets/Assorts", file + ".json"))![Traders.JAEGER];
            vendor.Items.AddRange(assort.Items);
            foreach (var (id, price) in assort.BarterScheme) vendor.BarterScheme.Add(id, price);
            foreach (var (id, level) in assort.LoyalLevelItems) vendor.LoyalLevelItems.Add(id, level);
        }
        await LoadGear();
        Award(full, shorty, mp12);
        var catalogue = await Service.Read(Session, true, default);
        var issued = await Issue(1, (full, 1), (shorty, 1), (mp12, 1));
        Assert.That(issued.Warnings, Is.Empty);
        var items = issued.ProfileChanges[Session].Items!.NewItems!;
        foreach (var (id, expected) in new Dictionary<string, string[]>
        {
            [full] = [full, "6910e8d90b57259e9dd34f78", "611a31ce5b7ffe001b4649d1"],
            [shorty] = [shorty, "6a8103f0d6a84f2c8b6e5104"],
            [mp12] = [mp12, "6910d6c97e9a164e15f94d48", "61f7b85367ddd414173fdb36", "61f7b234ea4ab34f2f59c3ec"]
        })
        {
            var blueprint = catalogue.WeaponBuilds[id];
            Assert.That(blueprint.Select(p => p.TemplateId), Is.EqualTo(expected));
            var tree = new List<Item> { items.Single(i => i.Template.ToString() == id) };
            foreach (var part in blueprint.Skip(1))
                tree.Add(items.Single(i => i.ParentId == tree[part.ParentIndex].Id.ToString() && i.SlotId == part.SlotId));
            Assert.That(tree.Select(i => i.Template.ToString()), Is.EqualTo(expected));
        }
        Assert.That(items, Has.Count.EqualTo(9));
        var ids = vendor.Items.ToDictionary(i => i.Id, _ => new MongoId());
        var reordered = Assort(Traders.JAEGER);
        foreach (var row in vendor.Items.AsEnumerable().Reverse())
            reordered.Items.Add(row with { Id = ids[row.Id], ParentId = row.ParentId == "hideout" ? "hideout" : ids[row.ParentId!].ToString() });
        foreach (var (id, price) in vendor.BarterScheme) reordered.BarterScheme.Add(ids[id], price);
        foreach (var (id, level) in vendor.LoyalLevelItems) reordered.LoyalLevelItems.Add(ids[id], level);
        var originalRecipe = Gear.Recipe(mp12);
        var reloaded = await LoadGear();
        Assert.That(reloaded.Recipe(mp12), Is.EqualTo(originalRecipe));
    }
}
