using NUnit.Framework;

namespace Liquidwarp.BottomlessRack.Tests;

public sealed class ClientDecisionTests
{
    [Test]
    public void LoadoutIssuesMissingWeaponsBeforeRecheckingLoosePartsAndLockedItems()
    {
        HashSet<string> unlocked = ["weapon", "factory part", "suppressor", "magazine"];
        var first = GearProtocol.PresetSupplies(
            [("factory part", 1, false), ("weapon", 1, true), ("suppressor", 1, false), ("locked", 1, false)], unlocked);
        Assert.That(first.Select(line => (line.TemplateId, line.Quantity)), Is.EqualTo(new[] { ("weapon", 1) }));

        // EFT matched the factory part and one owned magazine when it simulated the loadout again.
        var second = GearProtocol.PresetSupplies(
            [("suppressor", 1, false), ("magazine", 2, false), ("locked", 1, false), ("magazine", 1, false)], unlocked);
        Assert.That(second.Select(line => (line.TemplateId, line.Quantity)),
            Is.EqualTo(new[] { ("magazine", 3), ("suppressor", 1) }));
        Assert.That(GearProtocol.PresetSupplies([("locked", 1, false)], unlocked), Is.Empty);
        Assert.That(GearProtocol.PresetSupplies([], unlocked), Is.Empty);
    }

    private static readonly SpawnLine[] Lines = [new() { TemplateId = "part", Quantity = 1 }];
    private static SpawnReceipt Receipt(long sequence) => new() { Sequence = sequence, Lines = Lines, RootIds = ["real-stash-id"] };

    [Test]
    public async Task AcknowledgedRootOnlyIsPassedToEquipOrInstall()
    {
        var queue = new TaskCompletionSource<List<string>>();
        var applied = new List<string>();
        var click = StashSelection.IssueAndUse("part", _ => queue.Task, item => { applied.Add(item); return true; });
        Assert.That(applied, Is.Empty);
        queue.SetResult(["real-stash-id"]);
        Assert.That(await click, Is.EqualTo(StashUseResult.Applied));
        Assert.That(applied, Is.EqualTo(new[] { "real-stash-id" }));
    }

    [Test]
    public async Task ClosedSelectorLeavesIssuedGearInStashAndRejectedActionDoesNotEquip()
    {
        var stash = new List<string> { "real-stash-id" };
        Assert.That(await StashSelection.IssueAndUse("part", _ => Task.FromResult(stash), _ => false), Is.EqualTo(StashUseResult.InStash));
        Assert.That(stash, Has.Count.EqualTo(1));
        Assert.That(await StashSelection.IssueAndUse<string>("part", _ => Task.FromResult(new List<string>()),
            _ => throw new AssertionException("No item was issued")), Is.EqualTo(StashUseResult.NotIssued));
    }

    [Test]
    public async Task LostResponseReconcilesBeforeSendingAnotherCommand()
    {
        var session = new SpawnSession();
        session.Resume(3);
        var pending = new List<long>();
        Task<string> Send(long sequence, IReadOnlyList<SpawnLine> lines)
        {
            pending.Add(sequence);
            return Task.FromResult("");
        }
        Assert.ThrowsAsync<IOException>(async () => await session.Issue(Lines, Send, () => throw new IOException()));
        session.Resume(4); // Opening another selector must preserve the pending receipt check.
        Assert.ThrowsAsync<SpawnNotice>(async () => await session.Issue(Lines, Send, () => Task.FromResult(Receipt(4))));
        Assert.That(pending, Is.EqualTo(new[] { 4L }));
        var second = await session.Issue(Lines, Send, () => Task.FromResult(Receipt(5)));
        Assert.That(second.Sequence, Is.EqualTo(5));
        Assert.That(pending, Is.EqualTo(new[] { 4L, 5L }));
    }

    [Test]
    public async Task ConcurrentClickSendsOneCommandAndMismatchedReceiptsCannotEquip()
    {
        var session = new SpawnSession();
        var ack = new TaskCompletionSource<string>();
        var first = session.Issue(Lines, (_, _) => ack.Task, () => Task.FromResult(Receipt(1)));
        Assert.ThrowsAsync<SpawnNotice>(async () => await session.Issue(Lines, (_, _) => throw new AssertionException("Duplicate command"),
            () => Task.FromResult(Receipt(1))));
        ack.SetResult("");
        Assert.That((await first).RootIds, Is.EqualTo(new[] { "real-stash-id" }));
        var different = Receipt(2);
        different.Lines = [new() { TemplateId = "other", Quantity = 1 }];
        Assert.ThrowsAsync<SpawnNotice>(async () => await session.Issue(Lines, (_, _) => Task.FromResult(""), () => Task.FromResult(different)));
    }
}
