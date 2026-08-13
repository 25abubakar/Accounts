using System.Collections.Concurrent;

namespace Accounts.Services;

public sealed class ChatPresenceTracker
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, byte>> _connections = new();
    private readonly ConcurrentDictionary<string, Guid> _peopleByConnection = new();

    public bool Connect(Guid personId, string connectionId)
    {
        var connections = _connections.GetOrAdd(personId, _ => new ConcurrentDictionary<string, byte>());
        var wasOffline = connections.IsEmpty;
        connections[connectionId] = 0;
        _peopleByConnection[connectionId] = personId;
        return wasOffline;
    }

    public (Guid? PersonId, bool IsNowOffline) Disconnect(string connectionId)
    {
        if (!_peopleByConnection.TryRemove(connectionId, out var personId))
            return (null, false);

        if (!_connections.TryGetValue(personId, out var connections))
            return (personId, true);

        connections.TryRemove(connectionId, out _);
        if (!connections.IsEmpty) return (personId, false);
        _connections.TryRemove(personId, out _);
        return (personId, true);
    }

    public bool IsOnline(Guid personId) =>
        _connections.TryGetValue(personId, out var connections) && !connections.IsEmpty;
}
