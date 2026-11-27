using System.Collections.Concurrent;
using System.Text.Json;
using SPTarkov.Common.Extensions;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace Liquidwarp.BottomlessRack.Server;

internal sealed class WeaponRecipes(TemplateTable templates, GearDefinitions gear)
{
    // Merchant IDs only identify parent links within one assort. None are part of a recipe or its rank.
    private sealed class Source(List<Item> items)
    {
        public readonly ILookup<string?, Item> Children = items.OfType<Item>().ToLookup(i => i.ParentId);
        public readonly HashSet<MongoId> DuplicateIds = items.OfType<Item>().GroupBy(i => i.Id)
            .Where(g => g.Skip(1).Any()).Select(g => g.Key).ToHashSet();

        public bool IsWholePreset(Item root)
        {
            var seen = new HashSet<MongoId>();
            var pending = new Stack<Item>();
            pending.Push(root);
            while (pending.TryPop(out var item))
            {
                if (!seen.Add(item.Id)) return false;
                foreach (var child in Children[item.Id.ToString()]) pending.Push(child);
            }
            return seen.Count == items.Count;
        }
    }

    public ConcurrentDictionary<MongoId, RecipePart[]> Load(GlobalTable globals, TradersTable traders)
    {
        var recipes = new ConcurrentDictionary<MongoId, RecipePart[]>();
        foreach (var preset in globals.ItemPresets.Values)
        {
            if (preset?.Encyclopedia is not { } id || recipes.ContainsKey(id) || !gear.IsWeapon(id) ||
                preset.Items is not { Count: > 0 } items) continue;
            var source = new Source(items);
            var roots = source.Children[null].ToArray();
            if (roots.Length == 1 && roots[0].Template == id && source.IsWholePreset(roots[0]) &&
                Read(roots[0], source) is { } build)
                recipes[id] = gear.WithIntegralInserts(build);
        }

        var offers = new Dictionary<MongoId, (RecipePart[] Parts, int Loyalty, string Key)>();
        foreach (var (traderId, trader) in traders)
        {
            if (traderId == Traders.FENCE || trader?.Assort is not { Items.Count: > 0, BarterScheme: not null } assort)
                continue;
            var source = new Source(assort.Items);
            foreach (var root in source.Children["hideout"])
            {
                var id = root.Template;
                if (recipes.ContainsKey(id) || !gear.IsWeapon(id) ||
                    !assort.BarterScheme.TryGetValue(root.Id, out var prices) || prices?.Any(p => p is { Count: > 0 }) != true ||
                    Read(root, source) is not { } build) continue;
                var loyalty = assort.LoyalLevelItems?.GetValueOrDefault(root.Id, int.MaxValue) ?? int.MaxValue;
                var key = SlotPathKey(build);
                if (!offers.TryGetValue(id, out var best) || build.Length < best.Parts.Length ||
                    (build.Length == best.Parts.Length && (loyalty < best.Loyalty ||
                     (loyalty == best.Loyalty && string.CompareOrdinal(key, best.Key) < 0))))
                    offers[id] = (build, loyalty, key);
            }
        }
        foreach (var (id, offer) in offers) recipes[id] = gear.WithIntegralInserts(offer.Parts);
        return recipes;
    }

    private static string SlotPathKey(RecipePart[] parts)
    {
        var paths = new string[parts.Length];
        var keys = new List<string>(parts.Length - 1);
        for (var i = 1; i < parts.Length; i++)
        {
            var part = parts[i];
            paths[i] = paths[part.ParentIndex] + "/" + part.Slot;
            keys.Add(paths[i] + "\0" + part.Template);
        }
        keys.Sort(StringComparer.Ordinal);
        return string.Join("\n", keys);
    }

    internal static bool NeedsPart(TemplateItem item, HashSet<string> fitted) =>
        item.Properties?.Slots?.Any(s => s?.Name == null || (s.Required == true && !fitted.Contains(s.Name))) == true ||
        item.Properties?.Chambers?.Any(s => s == null || s.Required == true) == true;

    private RecipePart[]? Read(Item root, Source source)
    {
        var parts = new List<RecipePart>();
        var merged = new List<int>();
        var seen = new HashSet<MongoId>();
        bool Visit(Item item, int parent, int group, int depth)
        {
            if (depth >= 64 || item.Id.IsEmpty || source.DuplicateIds.Contains(item.Id) ||
                !seen.Add(item.Id) || !gear.Eligible(item.Template)) return false;
            var template = templates.Items[item.Template];
            var props = template.Properties!;
            var index = parts.Count;
            merged.Add(group);
            parts.Add(new(item.Template, parent, parent < 0 ? null : item.SlotId));
            var fitted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var child in source.Children[item.Id.ToString()].OrderBy(i => i.SlotId, StringComparer.Ordinal))
            {
                var name = child.SlotId;
                if (string.IsNullOrEmpty(name)) return false;
                var slot = props.Slots?.FirstOrDefault(s => s?.Name == name);
                if (slot == null)
                {
                    // Strip entire cargo and ammunition subtrees, even if their contents would be eligible loose gear.
                    if (props.Grids?.Any(g => g?.Name == name) == true || props.Chambers?.Any(s => s?.Name == name) == true ||
                        props.Cartridges?.Any(s => s?.Name == name) == true || props.StackSlots?.Any(s => s?.Name == name) == true)
                        continue;
                    return false;
                }
                if (!templates.Items.ContainsKey(child.Template)) return false;
                // Optional supplies, including rounds held in camora slots, do not enter a gear recipe.
                if (!gear.Eligible(child.Template)) continue;
                var start = parts.Count;
                var childGroup = slot.MergeSlotWithChildren == true || props.MergesWithChildren == true ? group : start;
                if (!fitted.Add(name) || !Visit(child, index, childGroup, depth + 1) || !Accepted(slot, parts, start))
                    return false;
            }
            return !NeedsPart(template, fitted);
        }

        if (!Visit(root, -1, 0, 0)) return null;
        for (var i = 0; i < parts.Count; i++)
            if (templates.Items[parts[i].Template].Properties?.ConflictingItems is { Count: > 0 } conflicts)
                for (var j = 0; j < parts.Count; j++)
                    if (i != j && merged[i] == merged[j] && conflicts.Contains(parts[j].Template)) return null;
        return parts.ToArray();
    }

    private bool Accepted(Slot slot, List<RecipePart> parts, int child)
    {
        foreach (var filter in slot.Properties?.Filters ?? [])
        {
            if (filter?.Filter == null || !gear.MatchesFilter(parts[child].Template, filter.Filter)) return false;
            // SPT 4.1 carries attachment-slot ExcludedFilter through its JSON extension data.
            if (!filter.TryGetExtensionData(out var extra) || !extra!.TryGetValue("ExcludedFilter", out var value) ||
                value == null) continue;
            JsonElement excluded;
            try { excluded = value is JsonElement json ? json : JsonSerializer.SerializeToElement(value); }
            catch (Exception ex) when (ex is JsonException or NotSupportedException) { return false; }
            if (excluded.ValueKind == JsonValueKind.Null) continue;
            if (excluded.ValueKind != JsonValueKind.Array) return false;
            foreach (var entry in excluded.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.String || entry.GetString() is not { } valueId ||
                    !MongoId.IsValidMongoId(valueId)) return false;
                var id = new MongoId(valueId);
                for (var i = child; i < parts.Count; i++)
                    if (gear.MatchesFilter(parts[i].Template, id)) return false;
            }
        }
        return true;
    }
}
