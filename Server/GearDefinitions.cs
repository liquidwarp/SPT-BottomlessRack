using System.Collections.Concurrent;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace Liquidwarp.UnlockGearProgression.Server;

[Injectable(InjectionType.Singleton)]
public sealed class GearDefinitions(TemplateTable templates, GlobalTable globals)
{
    private static readonly HashSet<MongoId> Families =
    [
        BaseClasses.WEAPON, BaseClasses.KNIFE, BaseClasses.MOD, BaseClasses.MAGAZINE,
        BaseClasses.ARMOR, BaseClasses.ARMOR_PLATE, BaseClasses.ARMORED_EQUIPMENT,
        BaseClasses.VEST, BaseClasses.BACKPACK, BaseClasses.HEADWEAR, BaseClasses.HEADPHONES,
        BaseClasses.FACE_COVER, BaseClasses.VISORS, BaseClasses.ARM_BAND
    ];

    private readonly ConcurrentDictionary<MongoId, bool> _eligible = new();
    private readonly ConcurrentDictionary<MongoId, RecipePart[]> _recipes = new();
    private readonly Lazy<Dictionary<MongoId, Preset>> _defaults = new(() =>
        globals.ItemPresets.Values.Where(p => p.Encyclopedia.HasValue && p.Items?.Count > 0)
            .GroupBy(p => p.Encyclopedia!.Value).ToDictionary(g => g.Key, g => g.First()));

    internal IEnumerable<MongoId> Ancestors(MongoId id)
    {
        // Guard bad item-mod hierarchies. Vanilla trees are only a few levels deep.
        for (var depth = 0; depth < 64 && templates.Items.TryGetValue(id, out var item); depth++)
        {
            yield return id;
            var parent = item.Parent;
            if (parent.IsEmpty || parent == id)
                yield break;
            id = parent;
        }
    }

    public bool Eligible(MongoId id) => _eligible.GetOrAdd(id, tpl =>
    {
        if (!templates.Items.TryGetValue(tpl, out var item) || item.Type != "Item" ||
            item.Properties is not { QuestItem: not true, Width: > 0, Height: > 0 })
            return false;
        bool matchesFamily = false;
        foreach (var parent in Ancestors(tpl))
        {
            if (parent == BaseClasses.THROW_WEAP || parent == BaseClasses.BUILT_IN_INSERTS || parent == BaseClasses.AMMO)
                return false;
            matchesFamily |= Families.Contains(parent);
        }
        return matchesFamily;
    });

    public RecipePart[] Recipe(MongoId id) => _recipes.GetOrAdd(id, MakeRecipe);

    private RecipePart[] MakeRecipe(MongoId id)
    {
        var result = new List<RecipePart> { new(id, -1, null) };
        if (Ancestors(id).Contains(BaseClasses.WEAPON) && _defaults.Value.TryGetValue(id, out var preset))
        {
            var root = preset.Items.FirstOrDefault(i => i.Template == id && i.ParentId == null);
            if (root != null)
            {
                var children = preset.Items.ToLookup(i => i.ParentId);
                var seen = new HashSet<MongoId> { root.Id };
                void AddSlots(Item parent, int index)
                {
                    foreach (var child in children[parent.Id.ToString()])
                    {
                        if (!Eligible(child.Template) || !seen.Add(child.Id) ||
                            templates.Items[parent.Template].Properties?.Slots?.Any(s => s.Name == child.SlotId) != true)
                            continue;
                        var next = result.Count;
                        result.Add(new(child.Template, index, child.SlotId));
                        AddSlots(child, next);
                    }
                }
                AddSlots(root, 0);
            }
        }

        // Integral armor is specified by locked slots, including on modded armor without a preset.
        var partsWithSlots = result.Count;
        for (var i = 0; i < partsWithSlots; i++)
            foreach (var slot in templates.Items[result[i].Template].Properties?.Slots ?? [])
            {
                var filter = slot.Properties?.Filters?.FirstOrDefault(f => f.Locked == true);
                if (filter == null) continue;
                var insert = filter.Plate ?? filter.Filter?.FirstOrDefault();
                if (insert.HasValue && Ancestors(insert.Value).Contains(BaseClasses.BUILT_IN_INSERTS))
                    result.Add(new(insert.Value, i, slot.Name));
            }
        return result.ToArray();
    }

    public List<Item> Create(MongoId id)
    {
        var result = new List<Item>();
        foreach (var part in Recipe(id))
        {
            var props = templates.Items[part.Template].Properties;
            var upd = new Upd { StackObjectsCount = 1, SpawnedInSession = false };
            if (props?.MaxDurability is { } hp)
                upd.Repairable = new() { MaxDurability = hp, Durability = hp };
            if (props?.Foldable == true) upd.Foldable = new() { Folded = false };
            if (props?.WeapFireType?.FirstOrDefault() is { } mode) upd.FireMode = new() { FireMode = mode };
            result.Add(new Item
            {
                Id = new MongoId(), Template = part.Template, SlotId = part.Slot,
                ParentId = part.ParentIndex < 0 ? null : result[part.ParentIndex].Id.ToString(), Upd = upd
            });
        }
        return result;
    }
}

public sealed record RecipePart(MongoId Template, int ParentIndex, string? Slot);
