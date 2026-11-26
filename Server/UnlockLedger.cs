using System.Text.Json;
using System.Text.Json.Serialization;
using SPTarkov.Common.Extensions;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;

namespace Liquidwarp.UnlockGearProgression.Server;

public sealed record UnlockLedger
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; init; } = 1;
    [JsonPropertyName("unlockedTemplates")] public HashSet<string> Templates { get; init; } = [];
    [JsonPropertyName("lastSpawn")] public SpawnReceipt Receipt { get; init; } = new();

    public static UnlockLedger Read(SptProfile profile)
    {
        profile.SptData ??= new();
        var data = profile.SptData.GetExtensionData();
        if (!data.TryGetValue(MyModInfo.MOD_GUID, out var entry)) return new();
        var ledger = entry switch
        {
            UnlockLedger current => current,
            JsonElement json => json.Deserialize<UnlockLedger>(),
            _ => throw new InvalidDataException("Unknown gear catalogue format.")
        };
        if (ledger is not { SchemaVersion: 1, Templates: not null, Receipt: not null })
            throw new InvalidDataException("Unsupported gear catalogue schema.");
        return ledger;
    }

    public void Write(SptProfile profile) => profile.SptData!.GetExtensionData()[MyModInfo.MOD_GUID] = this;

    public UnlockLedger Discover(IEnumerable<MongoId> candidates, GearDefinitions definitions)
    {
        HashSet<string>? next = null;
        foreach (var tpl in candidates)
        {
            var known = next ?? Templates;
            if (known.Contains(tpl.ToString()) || !definitions.Eligible(tpl)) continue;
            next ??= new(Templates);
            next.Add(tpl.ToString());
            foreach (var part in definitions.Recipe(tpl))
                if (definitions.Eligible(part.Template)) next.Add(part.Template.ToString());
        }
        return next == null ? this : this with { Templates = next };
    }

    public UnlockLedger ReconcileWeaponParts(GearDefinitions definitions)
    {
        HashSet<string>? next = null;
        foreach (var id in Templates)
        {
            if (!MongoId.IsValidMongoId(id) || !definitions.IsWeapon(id)) continue;
            foreach (var part in definitions.Recipe(id))
            {
                var tpl = part.Template.ToString();
                if ((next ?? Templates).Contains(tpl) || !definitions.Eligible(part.Template)) continue;
                (next ??= new(Templates)).Add(tpl);
            }
        }
        return next == null ? this : this with { Templates = next };
    }
}
