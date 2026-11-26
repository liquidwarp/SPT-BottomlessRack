#nullable enable
using System;
using System.Collections.Generic;
#if NET10_0_OR_GREATER
using WireName = System.Text.Json.Serialization.JsonPropertyNameAttribute;
#else
using WireName = Newtonsoft.Json.JsonPropertyAttribute;
#endif

namespace Liquidwarp.UnlockGearProgression;

public static class GearProtocol
{
    public const string Catalogue = "/unlockgearprogression/catalogue";
    public const string Receipt = "/unlockgearprogression/receipt";
    public const string Action = "UnlockGearProgressionSpawn";

    // A newly issued weapon includes its factory parts. Let EFT match those against the
    // preset before requesting loose gear from the next inventory simulation.
    public static List<SpawnLine> PresetSupplies(IEnumerable<(string TemplateId, int Quantity, bool Weapon)> missing,
        ISet<string> unlocked)
    {
        var weapons = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var gear = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (id, quantity, weapon) in missing)
        {
            if (!unlocked.Contains(id) || quantity <= 0) continue;
            var counts = weapon ? weapons : gear;
            counts.TryGetValue(id, out var count);
            counts[id] = checked(count + quantity);
        }
        var batch = new List<SpawnLine>();
        foreach (var pair in weapons.Count > 0 ? weapons : gear)
            batch.Add(new SpawnLine { TemplateId = pair.Key, Quantity = pair.Value });
        return batch;
    }

    // Subtract physical instances one at a time. Build plans may use a template twice.
    public static List<SpawnLine> Missing(IEnumerable<string> required, IEnumerable<string> owned)
    {
        var available = new Dictionary<string, int>();
        foreach (var id in owned)
            available[id] = available.TryGetValue(id, out var count) ? count + 1 : 1;
        var missing = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in required)
        {
            if (available.TryGetValue(id, out var count) && count > 0)
                available[id] = count - 1;
            else
                missing[id] = missing.TryGetValue(id, out count) ? count + 1 : 1;
        }
        var result = new List<SpawnLine>(missing.Count);
        foreach (var pair in missing)
            result.Add(new SpawnLine { TemplateId = pair.Key, Quantity = pair.Value });
        return result;
    }
}

public sealed class SpawnLine
{
    [WireName("templateId")] public string TemplateId { get; set; } = "";
    [WireName("quantity")] public int Quantity { get; set; }
}

public sealed class SpawnReceipt
{
    [WireName("sequence")] public long Sequence { get; set; }
    [WireName("lines")] public SpawnLine[] Lines { get; set; } = Array.Empty<SpawnLine>();
    [WireName("rootIds")] public string[] RootIds { get; set; } = Array.Empty<string>();
}

public sealed class Catalogue
{
    [WireName("revision")] public int Revision { get; set; }
    [WireName("templates")] public string[] Templates { get; set; } = Array.Empty<string>();
    [WireName("weaponBuilds")] public Dictionary<string, WeaponBuildPart[]> WeaponBuilds { get; set; } = new();
    [WireName("receipt")] public SpawnReceipt Receipt { get; set; } = new SpawnReceipt();
}

// Parent precedes child. The root has index -1 and no slot. Item IDs belong to each new copy.
public sealed class WeaponBuildPart
{
    [WireName("templateId")] public string TemplateId { get; set; } = "";
    [WireName("parentIndex")] public int ParentIndex { get; set; }
    [WireName("slotId")] public string? SlotId { get; set; }
}
