using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.SignalR;

sealed class RoomStore
{
    private readonly ConcurrentDictionary<string, Room> rooms = new();
    private readonly object membershipGate = new();
    public Room Create(AuthenticatedPlayer host)
    {
        lock (membershipGate)
        {
        // Reuse membership instead of creating orphan rooms on repeated clicks.
        var existing = rooms.Values.FirstOrDefault(r => r.ContainsPlayer(host.Id));
        if (existing != null) { existing.Touch(host.Id); return existing; }
        while (true)
        {
            var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            var room = new Room(code, host);
            if (rooms.TryAdd(code, room)) return room;
        }
        }
    }
    public Room? Get(string code) => rooms.GetValueOrDefault(code.ToUpperInvariant());
    public Room? Join(string code, AuthenticatedPlayer player)
    {
        lock (membershipGate)
        {
        var room = Get(code);
        if (rooms.Values.Any(r => r != room && r.ContainsPlayer(player.Id))) return null;
        return room?.TryAdd(player, 4) == true ? room : null;
        }
    }
    public Room? SetReady(string code, string id) => Get(code) is { } room && room.SetReady(id) ? room : null;
    public Room? Start(string code, string id) => Get(code) is { } room && room.Start(id) ? room : null;
    public bool Leave(string code, string id) => Get(code)?.TryLeave(id) == true;
    public IEnumerable<Room> All => rooms.Values;
    public void RemoveExpired() { foreach (var pair in rooms) if (pair.Value.IsExpired()) rooms.TryRemove(pair.Key, out _); }
}

sealed class RoomMaintenance(RoomStore rooms, IHubContext<GameHub> hub, DeviceLoginStore devices, AuthTokenStore tokens, ILogger<RoomMaintenance> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        var tick = 0;
        var publishedVersions = new Dictionary<string, long>();
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var room in rooms.All)
            {
                try
                {
                    room.Tick();
                    var version = room.Version;
                    if (!publishedVersions.TryGetValue(room.Code, out var published) || version != published)
                    {
                        await hub.Clients.Group(room.Code).SendAsync("roomChanged", room.Code, stoppingToken);
                        publishedVersions[room.Code] = version;
                    }
                }
                catch (Exception ex) { logger.LogError(ex, "Room update failed for {Code}", room.Code); }
            }
            if (++tick % 60 == 0)
            {
                rooms.RemoveExpired(); devices.Prune(); tokens.Prune();
                foreach (var code in publishedVersions.Keys.Where(code => rooms.Get(code) == null).ToArray()) publishedVersions.Remove(code);
            }
        }
    }
}
