using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.InventoryLogic;
using Newtonsoft.Json;
using SPT.Common.Http;

namespace Liquidwarp.UnlockGearProgression.Client;

internal static class GearClient
{
    private sealed class Envelope
    {
        [JsonProperty("err")] public int Error { get; set; }
        [JsonProperty("errmsg")] public string Message { get; set; }
        [JsonProperty("data")] public Catalogue Data { get; set; }
    }

    internal static readonly HashSet<string> Unlocked = new HashSet<string>();
    internal static Dictionary<string, WeaponBuildPart[]> WeaponBuilds { get; private set; } = new();
    internal static InventoryController Inventory { get; private set; }
    internal static event Action Updated;
    private static IEftSession _session;
    private static int _revision = -1;
    private static SpawnSession _requests;
    private static Task _loading;
    internal static bool Ready => _revision >= 0 && OutOfRaid;
    internal static bool OutOfRaid => !(Singleton<AbstractGame>.Instantiated && Singleton<AbstractGame>.Instance.InRaid);

    internal static void Enter(InventoryController inventory)
    {
        if (!OutOfRaid || !Singleton<ClientApplication<IEftSession>>.Instantiated) return;
        var session = Singleton<ClientApplication<IEftSession>>.Instance.GetClientBackEndSession();
        if (!ReferenceEquals(session, _session))
        {
            _session = session;
            _revision = -1;
            _requests = new SpawnSession();
            Unlocked.Clear();
            WeaponBuilds.Clear();
            PreviewRows.Reset();
            _loading = null;
        }
        Inventory = inventory;
        Refresh();
    }

    internal static void Refresh(bool checkOnly = false)
    {
        if (_session == null || !OutOfRaid || (_loading != null && !_loading.IsCompleted)) return;
        _loading = RefreshAsync(_session, checkOnly);
    }

    internal static async Task<bool> EnsureCatalogue(InventoryController inventory)
    {
        if (!OutOfRaid) return false;
        if (!Ready || !ReferenceEquals(Inventory, inventory)) Enter(inventory);
        if (_loading != null) await _loading;
        return Ready && ReferenceEquals(Inventory, inventory);
    }

    private static async Task RefreshAsync(IEftSession session, bool checkOnly)
    {
        try
        {
            if (checkOnly && _revision >= 0)
            {
                var head = await Fetch(GearProtocol.Receipt);
                if (head.Revision == _revision) return;
            }
            var data = await Fetch(GearProtocol.Catalogue);
            if (!ReferenceEquals(session, _session)) return;
            _requests.Resume(data.Receipt.Sequence);
            if (_revision == data.Revision) return;
            Unlocked.Clear();
            foreach (var id in data.Templates) Unlocked.Add(id);
            WeaponBuilds = data.WeaponBuilds;
            _revision = data.Revision;
            Updated?.Invoke();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Could not refresh unlocked gear: {ex.Message}");
        }
    }

    private static async Task<Catalogue> Fetch(string path)
    {
        var json = await RequestHandler.GetJsonAsync(path);
        var reply = JsonConvert.DeserializeObject<Envelope>(json);
        if (reply?.Data == null || reply.Error != 0)
            throw new InvalidOperationException(reply?.Message ?? "The gear catalogue server did not respond.");
        return reply.Data;
    }

    internal static void Warn(string message) => NotificationManager.DisplayWarningNotification(message);

    internal static async void Run(Task operation)
    {
        try { await operation; }
        catch (Exception ex)
        {
            Plugin.Log.LogError(ex);
            Warn("Gear action could not finish. Any issued items are in your stash.");
        }
    }

    internal static async Task<List<Item>> Spawn(IReadOnlyList<SpawnLine> lines)
    {
        if (!Ready) return null;
        var session = _session;
        var inventory = Inventory;
        try
        {
            if (_loading != null) await _loading;
            if (!ReferenceEquals(session, _session) || !OutOfRaid) return null;
            async Task<string> Send(long sequence, IReadOnlyList<SpawnLine> request)
            {
                var done = new TaskCompletionSource<IResult>();
                session.SendOperationRightNow(new { Action = GearProtocol.Action, sequence, lines = request }, done.SetResult);
                return (await done.Task).Error;
            }
            var receipt = await _requests.Issue(lines, Send, async () => (await Fetch(GearProtocol.Receipt)).Receipt);
            if (!ReferenceEquals(session, _session)) return null;
            var found = new Dictionary<string, Item>();
            foreach (var item in inventory.Inventory.GetPlayerItems(EPlayerItems.Stash))
                found[item.Id] = item;
            var roots = new List<Item>(receipt.RootIds.Length);
            foreach (var id in receipt.RootIds)
            {
                if (!found.TryGetValue(id, out var item))
                {
                    Warn("Gear was issued to your stash. Reopen the profile to synchronize inventory.");
                    return null;
                }
                roots.Add(item);
            }
            return roots;
        }
        catch (SpawnNotice notice) { Warn(notice.Message); return null; }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning(ex);
            Warn("Could not confirm the spawn. The next attempt will check your last receipt.");
            return null;
        }
    }

    internal static Task<List<Item>> One(string template) =>
        Spawn(new[] { new SpawnLine { TemplateId = template, Quantity = 1 } });
}
