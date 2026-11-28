using System.Collections.Concurrent;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace Liquidwarp.BottomlessRack.Server;

[Injectable(InjectionType.Singleton, TypePriority = int.MaxValue)]
public sealed class GearDefinitions(TemplateTable templates, GlobalTable globals, TradersTable traders, ItemHelper itemHelper) : IOnLoad
{
    // SPT has finished registering modded content and has not started its trader update loop.
    public Task OnLoadAsync(CancellationToken ct = default)
    {
        _recipes = new WeaponRecipes(templates, this).Load(globals, traders);
        return Task.CompletedTask;
    }

    private readonly ConcurrentDictionary<MongoId, bool> _eligible = new();
    private ConcurrentDictionary<MongoId, RecipePart[]> _recipes = new();

    // EFT slot filters can name a concrete template. SPT's base-class cache contains its parents.
    internal bool MatchesFilter(MongoId tpl, HashSet<MongoId> filter) =>
        filter.Contains(tpl) || itemHelper.IsOfBaseclasses(tpl, filter);

    internal bool MatchesFilter(MongoId tpl, MongoId filter) =>
        templates.Items.ContainsKey(tpl) && (tpl == filter || itemHelper.IsOfBaseclass(tpl, filter));

    // Static factories keep cache hits allocation-free during the per-item ownership walks.
    public bool Eligible(MongoId id) => _eligible.GetOrAdd(id, static (tpl, self) => self.IsEligible(tpl), this);

    private bool IsEligible(MongoId tpl) =>
        templates.Items.TryGetValue(tpl, out var item) && item?.Type == "Item" &&
        item.Properties is { QuestItem: not true, Width: > 0, Height: > 0 } &&
        MatchesFilter(tpl, GearCategories.Families) && !MatchesFilter(tpl, GearCategories.Exclusions);

    public RecipePart[] Recipe(MongoId id) => _recipes.GetOrAdd(id, static (tpl, self) => self.MakeStandaloneRecipe(tpl), this);

    public bool CanIssue(MongoId id) => Eligible(id) && Recipe(id).Length > 0;
    internal bool ExaminedByDefault(MongoId id) => templates.Items[id].Properties?.ExaminedByDefault == true;
    public bool IsWeapon(MongoId id) => MatchesFilter(id, BaseClasses.WEAPON);

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
                if (insert.HasValue && MatchesFilter(insert.Value, BaseClasses.BUILT_IN_INSERTS))
                    (inserts ??= []).Add(new(insert.Value, i, name));
            }
        return inserts == null ? parts : [.. parts, .. inserts];
    }

    public List<Item> Create(MongoId id, bool bareWeapon = false)
    {
        var result = new List<Item>();
        foreach (var part in bareWeapon ? WithIntegralInserts([new(id, -1, null)]) : Recipe(id))
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
