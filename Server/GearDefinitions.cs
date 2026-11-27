using System.Collections.Concurrent;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace Liquidwarp.BottomlessRack.Server;

[Injectable(InjectionType.Singleton, TypePriority = int.MaxValue)]
public sealed class GearDefinitions(TemplateTable templates, GlobalTable globals, TradersTable traders) : IOnLoad
{
    // SPT has finished registering modded content and has not started its trader update loop.
    public Task OnLoadAsync(CancellationToken ct = default)
    {
        _recipes = new WeaponRecipes(templates, this).Load(globals, traders);
        return Task.CompletedTask;
    }

    private readonly ConcurrentDictionary<MongoId, bool> _eligible = new();
    private ConcurrentDictionary<MongoId, RecipePart[]> _recipes = new();

    internal IEnumerable<MongoId> Ancestors(MongoId id)
    {
        // Guard bad item-mod hierarchies. Vanilla trees are only a few levels deep.
        for (var depth = 0; depth < GearCategories.MaxDepth && templates.Items.TryGetValue(id, out var item) && item != null; depth++)
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
        if (!templates.Items.TryGetValue(tpl, out var item) || item?.Type != "Item" ||
            item.Properties is not { QuestItem: not true, Width: > 0, Height: > 0 })
            return false;
        bool matchesFamily = false;
        foreach (var parent in Ancestors(tpl))
        {
            if (GearCategories.Exclusions.Contains(parent)) return false;
            matchesFamily |= GearCategories.Families.Contains(parent);
        }
        return matchesFamily;
    });

    public RecipePart[] Recipe(MongoId id) => _recipes.GetOrAdd(id, MakeStandaloneRecipe);

    public bool CanIssue(MongoId id) => Eligible(id) && Recipe(id).Length > 0;
    public bool IsWeapon(MongoId id) => Ancestors(id).Contains(BaseClasses.WEAPON);

    private RecipePart[] MakeStandaloneRecipe(MongoId id)
    {
        if (!Eligible(id) || (IsWeapon(id) && WeaponRecipes.NeedsPart(templates.Items[id], []))) return [];
        return WithIntegralInserts([new(id, -1, null)]);
    }

    internal RecipePart[] WithIntegralInserts(RecipePart[] parts)
    {
        // Integral armor is specified by locked slots, including on modded armor without a preset.
        List<RecipePart>? inserts = null;
        for (var i = 0; i < parts.Length; i++)
            foreach (var slot in templates.Items[parts[i].Template].Properties?.Slots ?? [])
            {
                if (slot?.Name is not { } name) continue;
                var filter = slot.Properties?.Filters?.FirstOrDefault(f => f?.Locked == true);
                if (filter == null) continue;
                var insert = filter.Plate ?? filter.Filter?.FirstOrDefault();
                if (insert.HasValue && Ancestors(insert.Value).Contains(BaseClasses.BUILT_IN_INSERTS))
                    (inserts ??= []).Add(new(insert.Value, i, name));
            }
        return inserts == null ? parts : [.. parts, .. inserts];
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
