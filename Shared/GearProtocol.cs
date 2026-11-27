#nullable enable
using System;
using System.Collections.Generic;
#if NET10_0_OR_GREATER
using WireName = System.Text.Json.Serialization.JsonPropertyNameAttribute;
#else
using WireName = Newtonsoft.Json.JsonPropertyAttribute;
#endif

namespace Liquidwarp.BottomlessRack;

public static class GearProtocol
{
    public const string Catalogue = "/bottomlessrack/catalogue";
    public const string Receipt = "/bottomlessrack/receipt";
    public const string Action = "BottomlessRackSpawn";

    // Both sides use one line per template, sorted by template ID.
    public static bool SameRequest(IReadOnlyList<SpawnLine> requested, SpawnLine[] recorded)
    {
        if (requested.Count != recorded.Length) return false;
        for (var i = 0; i < recorded.Length; i++)
        {
            var line = requested[i];
            var previous = recorded[i];
            if (line.TemplateId != previous.TemplateId || line.Quantity != previous.Quantity ||
                line.BareWeapon != previous.BareWeapon) return false;
        }
        return true;
    }

    // EFT simulates missing items as empty bodies. Supply the same bodies and loose shortages
    // together so assembly never has to strip factory furniture from a new weapon.
    public static List<SpawnLine> PresetSupplies(IEnumerable<(string TemplateId, int Quantity, bool Weapon)> missing,
        ISet<string> unlocked)
    {
        var lines = new SortedDictionary<string, SpawnLine>(StringComparer.Ordinal);
        foreach (var (id, quantity, weapon) in missing)
        {
            if (!unlocked.Contains(id) || quantity <= 0) continue;
            if (!lines.TryGetValue(id, out var line))
                lines.Add(id, line = new SpawnLine { TemplateId = id, BareWeapon = weapon });
            line.Quantity = checked(line.Quantity + quantity);
        }
        return new List<SpawnLine>(lines.Values);
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
    [WireName("bareWeapon")] public bool BareWeapon { get; set; }
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
