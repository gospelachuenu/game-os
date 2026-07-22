using System.Text.Json;
using MaintenanceHub;

namespace ParentalControls;

/// <summary>A game the kid has asked a parent to approve.</summary>
public sealed class ApprovalRequest
{
    public required string GameId { get; init; }
    public required string GameName { get; init; }
    public required DateTime RequestedUtc { get; init; }
}

/// <summary>
/// The local queue of "Ask a parent" requests. When a kid presses a blocked or pending
/// game, a request lands here; the parent clears it by allowing or blocking the game
/// (behind the PIN), or — once it exists — from the web portal.
///
/// It is a LOCAL queue rather than fire-and-forget precisely so the kid's request goes
/// somewhere real: a parent standing at the console can act on it immediately without
/// the portal existing. See [[parental-controls-design]].
///
/// Persisted through the durable state store so requests survive a reboot — a kid who
/// asked before bedtime should still have their request waiting the next morning.
/// </summary>
public sealed class ApprovalQueue
{
    private const string Key = "parental.approval_queue";

    private readonly IConsoleStateStore _state;

    public ApprovalQueue(IConsoleStateStore state) => _state = state;

    public IReadOnlyList<ApprovalRequest> All()
    {
        var json = _state.Get(Key);
        if (string.IsNullOrEmpty(json))
        {
            return Array.Empty<ApprovalRequest>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<ApprovalRequest>>(json) ?? new();
        }
        catch (JsonException)
        {
            return Array.Empty<ApprovalRequest>();
        }
    }

    public int Count => All().Count;

    /// <summary>
    /// Adds a request, or refreshes the timestamp if the kid asks for the same game
    /// again. A second press must not create a duplicate — the parent should see one
    /// entry per game, not a growing pile from an impatient child.
    /// </summary>
    public void Add(string gameId, string gameName, DateTime now)
    {
        var list = All().ToList();
        list.RemoveAll(r => r.GameId == gameId);
        list.Add(new ApprovalRequest { GameId = gameId, GameName = gameName, RequestedUtc = now });
        Persist(list);
    }

    /// <summary>Removes a request — called when the parent has decided on that game.</summary>
    public void Remove(string gameId)
    {
        var list = All().ToList();
        if (list.RemoveAll(r => r.GameId == gameId) > 0)
        {
            Persist(list);
        }
    }

    public bool Contains(string gameId) => All().Any(r => r.GameId == gameId);

    private void Persist(List<ApprovalRequest> list) =>
        _state.Set(Key, JsonSerializer.Serialize(list));
}
