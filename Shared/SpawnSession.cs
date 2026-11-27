using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Liquidwarp.BottomlessRack;

// One instance per connected client profile. The server's durable receipt decides the outcome,
// even when the inventory callback was lost or reported a transport error.
public sealed class SpawnSession
{
    private long _lastSequence;
    private bool _uncertain;
    private bool _busy;

    public void Resume(long sequence)
    {
        if (!_busy && !_uncertain) _lastSequence = Math.Max(_lastSequence, sequence);
    }

    public async Task<SpawnReceipt> Issue(IReadOnlyList<SpawnLine> lines,
        Func<long, IReadOnlyList<SpawnLine>, Task<string>> send, Func<Task<SpawnReceipt>> readReceipt)
    {
        if (_busy) throw new SpawnNotice("A gear request is still pending.");
        _busy = true;
        try
        {
            if (_uncertain)
            {
                var recovered = await readReceipt();
                var landed = recovered.Sequence > _lastSequence;
                _lastSequence = recovered.Sequence;
                _uncertain = false;
                if (landed) throw new SpawnNotice("Your previous request reached the stash. Reopen the profile if the items are missing.");
            }

            var sequence = checked(_lastSequence + 1);
            _uncertain = true;
            var error = await send(sequence, lines);
            var receipt = await readReceipt();
            _lastSequence = receipt.Sequence;
            _uncertain = false;
            if (receipt.Sequence >= sequence && (receipt.Sequence != sequence || !SameRequest(lines, receipt.Lines)))
                throw new SpawnNotice("The receipt belongs to another gear request. Check your stash before trying again.");
            if (receipt.Sequence != sequence)
                throw new SpawnNotice(string.IsNullOrEmpty(error) ? "Gear was not issued. Check the space in your stash and try again." : error);
            return receipt;
        }
        finally { _busy = false; }
    }

    private static bool SameRequest(IReadOnlyList<SpawnLine> requested, SpawnLine[] receipt)
    {
        if (requested.Count != receipt.Length) return false;
        // UI requests contain one sorted line per template, just like the server receipt.
        for (var i = 0; i < receipt.Length; i++)
            if (requested[i].TemplateId != receipt[i].TemplateId || requested[i].Quantity != receipt[i].Quantity ||
                requested[i].BareWeapon != receipt[i].BareWeapon) return false;
        return true;
    }
}

public sealed class SpawnNotice : Exception
{
    public SpawnNotice(string message) : base(message) { }
}

public enum StashUseResult { NotIssued, Applied, InStash }

public static class StashSelection
{
    public static async Task<StashUseResult> IssueAndUse<T>(string template, Func<string, Task<List<T>>> issue, Func<T, bool> use)
    {
        var items = await issue(template);
        if (items == null || items.Count == 0) return StashUseResult.NotIssued;
        return items.Count == 1 && use(items[0]) ? StashUseResult.Applied : StashUseResult.InStash;
    }
}
