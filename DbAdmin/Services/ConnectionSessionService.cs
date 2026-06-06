
namespace DbAdmin.Services;


/// <summary>
/// Manages open database connections keyed by a session ID.
/// Connections are created on /connect and disposed on /disconnect or timeout.
/// </summary>
public sealed class ConnectionSessionService
    : System.IAsyncDisposable
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary
        <string, SessionEntry> _sessions = new System.Collections.Concurrent
        .ConcurrentDictionary<string, SessionEntry>();

    private readonly System.TimeSpan _idleTimeout;
    private readonly System.Threading.Timer _cleanupTimer;

    public ConnectionSessionService(System.TimeSpan? idleTimeout = null)
    {
        _idleTimeout = idleTimeout ?? System.TimeSpan.FromMinutes(30);
        _cleanupTimer = new System.Threading.Timer(
            CleanupIdle,
            null,
            System.TimeSpan.FromMinutes(5),
            System.TimeSpan.FromMinutes(5)
        );
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public async System.Threading.Tasks.Task<Models.ConnectionInfo> 
        ConnectAsync(
        Models.ConnectionRequest req,  System.Threading.CancellationToken ct = default
    )
    {
        try
        {
            Providers.IDbProvider provider = Providers.ProviderFactory.Create(req);
            await provider.OpenAsync(ct);

            string id = System.Guid.NewGuid().ToString("N");

            Models.ConnectionInfo info = new Models.ConnectionInfo(
                id,
                req.Provider,
                req.Host,
                req.Port,
                req.Database,
                req.Username,
                System.DateTime.UtcNow
            );

            _sessions[id] = new SessionEntry(provider, info, System.DateTime.UtcNow);
            return info;
        }
        catch (System.Exception ex)
        {
            System.Console.WriteLine(ex.Message);
            throw;
        }
    }

    public Providers.IDbProvider GetProvider(string connectionId)
    {
        if (!_sessions.TryGetValue(connectionId, out SessionEntry? entry))
            throw new System.Collections.Generic.KeyNotFoundException($"No active session '{connectionId}'. Call /connect first.");

        entry.LastUsed = System.DateTime.UtcNow;
        return entry.Provider;
    }

    public System.Collections.Generic.IEnumerable<Models.ConnectionInfo> 
        ListConnections()
    {
        System.Collections.Generic.List<Models.ConnectionInfo> list = 
            new System.Collections.Generic.List<Models.ConnectionInfo>();

        foreach (SessionEntry entry in _sessions.Values)
            list.Add(entry.Info);

        return list;
    }

    public async System.Threading.Tasks.Task<bool> DisconnectAsync(
        string connectionId
    )
    {
        if (!_sessions.TryRemove(connectionId, out SessionEntry? entry))
            return false;

        await entry.Provider.DisposeAsync();
        return true;
    }

    // ── Idle cleanup ─────────────────────────────────────────────────────────

    private void CleanupIdle(object? _)
    {
        System.DateTime cutoff = System.DateTime.UtcNow - _idleTimeout;

        // We iterate over the collection directly. 
        // Since we are modifying the dictionary during iteration, we collect keys first.
        System.Collections.Generic.List<string> staleKeys = 
            new System.Collections.Generic.List<string>();

        foreach (System.Collections.Generic.KeyValuePair<string, SessionEntry> 
            kv in _sessions
        )
        {
            if (kv.Value.LastUsed < cutoff)
            {
                staleKeys.Add(kv.Key);
            }
        }

        foreach (string id in staleKeys)
        {
            if (_sessions.TryRemove(id, out SessionEntry? e))
            {
                e.Provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
    }


    public async System.Threading.Tasks.ValueTask DisposeAsync()
    {
        await _cleanupTimer.DisposeAsync();
        foreach (SessionEntry e in _sessions.Values)
            await e.Provider.DisposeAsync();
        _sessions.Clear();
    }

    // ── Inner type ───────────────────────────────────────────────────────────

    private sealed class SessionEntry(
        Providers.IDbProvider provider,
        Models.ConnectionInfo info,
        System.DateTime lastUsed
    )
    {
        public Providers.IDbProvider Provider = provider;
        public Models.ConnectionInfo Info = info;
        public System.DateTime LastUsed = lastUsed;
    }
}
