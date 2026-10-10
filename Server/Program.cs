using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Repositories;
using System.Xml.Linq;
using Microsoft.Extensions.Logging.Abstractions;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
var port = Environment.GetEnvironmentVariable("PORT") ?? "5080";
builder.Services.AddHttpClient();
builder.Services.AddSingleton<RoomStore>();
builder.Services.AddSingleton<DiscordOAuth>();
builder.Services.AddSingleton<DeviceLoginStore>();
builder.Services.AddSingleton<AuthTokenStore>();
builder.Services.AddSignalR();
builder.Services.AddDistributedMemoryCache();
var dataProtectionPath = Path.Combine(builder.Environment.ContentRootPath, ".local-keys");
builder.Services.AddDataProtection().SetApplicationName("EsUn10Pero")
    .AddKeyManagementOptions(options => options.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(dataProtectionPath), NullLoggerFactory.Instance));
builder.Services.AddSession(options =>
{
    options.Cookie.Name = "esun10.session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.IdleTimeout = TimeSpan.FromHours(8);
});

var app = builder.Build();
app.UseSession();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

if (app.Environment.IsDevelopment())
{
    app.MapPost("/dev/login", (DevLoginRequest request, HttpContext context) =>
    {
        if (string.IsNullOrWhiteSpace(request.Id) || string.IsNullOrWhiteSpace(request.DisplayName))
            return Results.BadRequest(new { error = "Id y nombre son obligatorios." });
        context.Session.SetString("discord_id", "dev:" + request.Id.Trim());
        context.Session.SetString("display_name", request.DisplayName.Trim()[..Math.Min(32, request.DisplayName.Trim().Length)]);
        return Results.Ok(new { status = "ok" });
    });
}

app.MapGet("/auth/discord", (HttpContext context, DiscordOAuth oauth, string? device) =>
{
    var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    context.Session.SetString("oauth_state", state);
    if (!string.IsNullOrWhiteSpace(device)) context.Session.SetString("oauth_device", device);
    return Results.Redirect(oauth.GetAuthorizationUrl(state));
});

app.MapGet("/auth/discord/callback", async (
    string? code,
    string? state,
    HttpContext context,
    DiscordOAuth oauth,
    DeviceLoginStore devices,
    CancellationToken cancellationToken) =>
{
    var expectedState = context.Session.GetString("oauth_state");
    var device = context.Session.GetString("oauth_device");
    context.Session.Remove("oauth_state");
    context.Session.Remove("oauth_device");
    byte[] expectedBytes, actualBytes;
    try
    {
        expectedBytes = expectedState == null ? [] : Convert.FromHexString(expectedState);
        actualBytes = state == null ? [] : Convert.FromHexString(state);
    }
    catch (FormatException) { return Results.BadRequest("OAuth state or code is invalid."); }
    if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state) ||
        expectedState == null || !CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes))
    {
        return Results.BadRequest("OAuth state or code is invalid.");
    }

    var identity = await oauth.ExchangeCodeAsync(code, cancellationToken);
    context.Session.SetString("discord_id", identity.Id);
    context.Session.SetString("display_name", identity.DisplayName);
    if (identity.AvatarUrl != null) context.Session.SetString("avatar_url", identity.AvatarUrl);
    if (device != null) devices.Authorize(device, identity);
    return Results.Redirect("/");
});

// Unity opens the returned URL in the system browser; Discord's client secret stays on this server.
app.MapPost("/auth/unity/device", (HttpRequest request, DeviceLoginStore devices) =>
{
    var device = devices.Create();
    var baseUrl = $"{request.Scheme}://{request.Host}";
    return Results.Ok(new { deviceId = device.Id, loginUrl = $"{baseUrl}/auth/discord?device={Uri.EscapeDataString(device.Id)}", expiresIn = 300 });
});

app.MapGet("/auth/unity/device/{deviceId}", (string deviceId, DeviceLoginStore devices, AuthTokenStore tokens) =>
{
    var result = devices.TakeAuthorized(deviceId);
    if (result.IsExpired) return Results.NotFound();
    if (result.Identity == null) return Results.Ok(new { status = "pending" });
    var token = tokens.Issue(result.Identity);
    return Results.Ok(new { status = "authorized", accessToken = token.Value, expiresIn = 86400,
        player = new { id = result.Identity.Id, displayName = result.Identity.DisplayName, avatarUrl = result.Identity.AvatarUrl } });
});

app.MapGet("/auth/me", (HttpContext context) =>
{
    var id = context.Session.GetString("discord_id");
    var name = context.Session.GetString("display_name");
    var avatar = context.Session.GetString("avatar_url");
    return id == null
        ? Results.Json(new { error = "not_authenticated", loginUrl = "/auth/discord" }, statusCode: StatusCodes.Status401Unauthorized)
        : Results.Ok(new { id, displayName = name, avatarUrl = avatar });
});

app.MapPost("/rooms", async (HttpContext context, RoomStore rooms, AuthTokenStore tokens, IHubContext<GameHub> hub) =>
{
    var player = RequirePlayer(context, tokens);
    if (player == null) return Results.Unauthorized();
    var room = rooms.Create(player);
    await hub.Clients.Group(room.Code).SendAsync("roomUpdated", room.ToView(player.Id));
    return Results.Ok(room.ToView(player.Id));
});

app.MapPost("/rooms/{code}/join", async (string code, HttpContext context, RoomStore rooms, AuthTokenStore tokens, IHubContext<GameHub> hub) =>
{
    var player = RequirePlayer(context, tokens);
    if (player == null) return Results.Unauthorized();
    return rooms.Join(code, player) is { } room
        ? await NotifyRoom(hub, room, player.Id)
        : Results.BadRequest("Room does not exist, is full, or the game has started.");
});

app.MapGet("/rooms/{code}", (string code, HttpContext context, RoomStore rooms, AuthTokenStore tokens) =>
{
    var player = RequirePlayer(context, tokens);
    if (player == null) return Results.Unauthorized();
    return rooms.Get(code) is { } room && room.ContainsPlayer(player.Id)
        ? Results.Ok(room.ToView(player.Id))
        : Results.NotFound();
});

app.MapPost("/rooms/{code}/ready", async (string code, HttpContext context, RoomStore rooms, AuthTokenStore tokens, IHubContext<GameHub> hub) =>
{
    var player = RequirePlayer(context, tokens);
    if (player == null) return Results.Unauthorized();
    return rooms.SetReady(code, player.Id) is { } room
        ? await NotifyRoom(hub, room, player.Id)
        : Results.BadRequest("Invalid room or player.");
});

app.MapPost("/rooms/{code}/leave", (string code, HttpContext context, RoomStore rooms, AuthTokenStore tokens) =>
{
    var player = RequirePlayer(context, tokens);
    if (player == null) return Results.Unauthorized();
    return rooms.Leave(code, player.Id)
        ? Results.Ok(new { status = "left" })
        : Results.BadRequest(new { error = "No se puede salir de esta mesa en este momento." });
});

app.MapPost("/rooms/{code}/start", async (string code, HttpContext context, RoomStore rooms, AuthTokenStore tokens, IHubContext<GameHub> hub) =>
{
    var player = RequirePlayer(context, tokens);
    if (player == null) return Results.Unauthorized();
    return rooms.Start(code, player.Id) is { } room
        ? await NotifyRoom(hub, room, player.Id)
        : Results.BadRequest("Only the host can start a ready room.");
});

app.MapPost("/rooms/{code}/guess", async (string code, GuessRequest request, HttpContext context, RoomStore rooms, AuthTokenStore tokens, IHubContext<GameHub> hub) =>
{
    var player = RequirePlayer(context, tokens);
    if (player == null) return Results.Unauthorized();
    var room = rooms.Get(code);
    if (room == null || !room.ContainsPlayer(player.Id)) return Results.NotFound();
    var action = room.Guess(player.Id, request.Number);
    if (!action.Accepted) return Results.BadRequest(new { error = action.Message });
    await hub.Clients.Group(room.Code).SendAsync("roomChanged");
    return Results.Ok(new { status = action.Message, room = room.ToView(player.Id) });
});

app.MapPost("/rooms/{code}/clue", async (string code, ClueRequest request, HttpContext context, RoomStore rooms, AuthTokenStore tokens, IHubContext<GameHub> hub) =>
{
    var player = RequirePlayer(context, tokens);
    if (player == null) return Results.Unauthorized();
    var room = rooms.Get(code);
    if (room == null || !room.ContainsPlayer(player.Id)) return Results.NotFound();
    var action = room.AddClue(player.Id, request.Text);
    if (!action.Accepted) return Results.BadRequest(new { error = action.Message });
    await hub.Clients.Group(room.Code).SendAsync("roomChanged");
    return Results.Ok(room.ToView(player.Id));
});

app.MapPost("/rooms/{code}/hint", async (string code, HttpContext context, RoomStore rooms, AuthTokenStore tokens, IHubContext<GameHub> hub) =>
{
    var player = RequirePlayer(context, tokens);
    if (player == null) return Results.Unauthorized();
    var room = rooms.Get(code);
    if (room == null || !room.ContainsPlayer(player.Id)) return Results.NotFound();
    var action = room.GetHint(player.Id);
    if (!action.Accepted) return Results.BadRequest(new { error = action.Message });
    await hub.Clients.Group(room.Code).SendAsync("roomChanged");
    return Results.Ok(new { suggestion = action.Message, room = room.ToView(player.Id) });
});

app.MapHub<GameHub>("/realtime");

app.Run($"http://0.0.0.0:{port}");

static AuthenticatedPlayer? RequirePlayer(HttpContext context, AuthTokenStore tokens)
{
    var auth = context.Request.Headers.Authorization.ToString();
    if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) && tokens.Resolve(auth[7..]) is { } tokenPlayer)
        return tokenPlayer;
    var id = context.Session.GetString("discord_id");
    var name = context.Session.GetString("display_name");
    var avatar = context.Session.GetString("avatar_url");
    return id == null || name == null ? null : new AuthenticatedPlayer(id, name, avatar);
}

static async Task<IResult> NotifyRoom(IHubContext<GameHub> hub, Room room, string viewerId)
{
    var view = room.ToView(viewerId);
    await hub.Clients.Group(room.Code).SendAsync("roomUpdated", view);
    return Results.Ok(view);
}

sealed class DeviceLoginStore
{
    private readonly ConcurrentDictionary<string, DeviceLogin> pending = new();
    public DeviceLogin Create()
    {
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var item = new DeviceLogin(DateTimeOffset.UtcNow.AddMinutes(5));
        pending[id] = item;
        return item with { Id = id };
    }
    public void Authorize(string id, DiscordIdentity identity)
    {
        if (pending.TryGetValue(id, out var item) && item.ExpiresAt > DateTimeOffset.UtcNow)
            pending[id] = item with { Identity = new AuthenticatedPlayer(identity.Id, identity.DisplayName, identity.AvatarUrl) };
    }
    public DevicePollResult TakeAuthorized(string id)
    {
        if (!pending.TryGetValue(id, out var item)) return DevicePollResult.ExpiredOrUnknown;
        if (item.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            pending.TryRemove(id, out _);
            return DevicePollResult.ExpiredOrUnknown;
        }
        if (item.Identity == null) return new DevicePollResult(null);
        return pending.TryRemove(id, out var consumed) ? new DevicePollResult(consumed.Identity) : new DevicePollResult(null);
    }
}
record DeviceLogin(DateTimeOffset ExpiresAt, AuthenticatedPlayer? Identity = null, string Id = "");
record DevicePollResult(AuthenticatedPlayer? Identity, bool IsExpired = false)
{
    public static DevicePollResult ExpiredOrUnknown { get; } = new(null, true);
}

sealed class AuthTokenStore
{
    private readonly ConcurrentDictionary<string, (AuthenticatedPlayer Player, DateTimeOffset ExpiresAt)> tokens = new();
    public (string Value, DateTimeOffset ExpiresAt) Issue(AuthenticatedPlayer player)
    {
        var value = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var expires = DateTimeOffset.UtcNow.AddHours(24);
        tokens[value] = (player, expires);
        return (value, expires);
    }
    public AuthenticatedPlayer? Resolve(string value) => tokens.TryGetValue(value, out var entry) && entry.ExpiresAt > DateTimeOffset.UtcNow ? entry.Player : null;
}

sealed class GameHub(AuthTokenStore tokens, RoomStore rooms) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var raw = GetAccessToken();
        if (!raw.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || tokens.Resolve(raw[7..]) == null)
        {
            Context.Abort();
            return;
        }
        await base.OnConnectedAsync();
    }

    public async Task JoinRoom(string code)
    {
        var raw = GetAccessToken();
        var player = raw.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? tokens.Resolve(raw[7..]) : null;
        if (player == null || rooms.Get(code)?.ContainsPlayer(player.Id) != true) throw new HubException("No perteneces a esta sala.");
        await Groups.AddToGroupAsync(Context.ConnectionId, code.ToUpperInvariant());
    }

    private string GetAccessToken()
    {
        var request = Context.GetHttpContext()?.Request;
        var raw = request?.Headers.Authorization.ToString() ?? "";
        return raw.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? raw
            : "Bearer " + (request?.Query["access_token"].ToString() ?? "");
    }
}

record GuessRequest(int Number);
record ClueRequest(string Text);
record DevLoginRequest(string Id, string DisplayName);
record GameAction(bool Accepted, string Message);
record AuthenticatedPlayer(string Id, string DisplayName, string? AvatarUrl);
record DiscordIdentity(string Id, string DisplayName, string? AvatarUrl);

sealed class DiscordOAuth(IConfiguration configuration, IHttpClientFactory clients)
{
    private readonly IConfigurationSection settings = configuration.GetSection("Discord");

    public string GetAuthorizationUrl(string state)
    {
        var clientId = settings["ClientId"];
        var redirectUri = settings["RedirectUri"];
        if (string.IsNullOrWhiteSpace(clientId) ||
            clientId == "REPLACE_WITH_DISCORD_CLIENT_ID" ||
            string.IsNullOrWhiteSpace(redirectUri))
        {
            throw new InvalidOperationException(
                "Discord no está configurado. Crea Server/appsettings.json con ClientId, ClientSecret y RedirectUri.");
        }

        var query = $"client_id={Uri.EscapeDataString(clientId)}" +
                    "&response_type=code&scope=identify" +
                    $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                    $"&state={Uri.EscapeDataString(state)}";
        return "https://discord.com/oauth2/authorize?" + query;
    }

    public async Task<DiscordIdentity> ExchangeCodeAsync(string code, CancellationToken cancellationToken)
    {
        using var client = clients.CreateClient();
        using var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = settings["ClientId"] ?? "",
            ["client_secret"] = settings["ClientSecret"] ?? "",
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = settings["RedirectUri"] ?? ""
        });
        using var tokenResponse = await client.PostAsync("https://discord.com/api/oauth2/token", tokenRequest, cancellationToken);
        tokenResponse.EnsureSuccessStatusCode();
        using var tokenJson = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync(cancellationToken));
        var token = tokenJson.RootElement.GetProperty("access_token").GetString();
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("Discord returned no access token.");

        using var userRequest = new HttpRequestMessage(HttpMethod.Get, "https://discord.com/api/users/@me");
        userRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var userResponse = await client.SendAsync(userRequest, cancellationToken);
        userResponse.EnsureSuccessStatusCode();
        using var userJson = JsonDocument.Parse(await userResponse.Content.ReadAsStringAsync(cancellationToken));
        var root = userJson.RootElement;
        var id = root.GetProperty("id").GetString() ?? throw new InvalidOperationException("Discord user has no id.");
        var username = root.GetProperty("username").GetString() ?? "Jugador";
        var displayName = root.GetProperty("global_name").GetString() ?? username;
        var avatarHash = root.TryGetProperty("avatar", out var avatar) ? avatar.GetString() : null;
        var avatarUrl = avatarHash == null ? null : $"https://cdn.discordapp.com/avatars/{id}/{avatarHash}.png?size=128";
        return new DiscordIdentity(id, displayName, avatarUrl);
    }
}

sealed class RoomStore
{
    private const int MaxPlayers = 4;
    private readonly ConcurrentDictionary<string, Room> rooms = new();

    public Room Create(AuthenticatedPlayer host)
    {
        string code;
        do code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        while (rooms.ContainsKey(code));
        var room = new Room(code, host);
        rooms[code] = room;
        return room;
    }

    public Room? Get(string code) => rooms.TryGetValue(code.ToUpperInvariant(), out var room) ? room : null;

    public Room? Join(string code, AuthenticatedPlayer player)
    {
        var room = Get(code);
        return room != null && room.TryAdd(player, MaxPlayers) ? room : null;
    }

    public Room? SetReady(string code, string playerId)
    {
        var room = Get(code);
        return room != null && room.SetReady(playerId) ? room : null;
    }

    public Room? Start(string code, string playerId)
    {
        var room = Get(code);
        return room != null && room.Start(playerId) ? room : null;
    }

    public bool Leave(string code, string playerId)
    {
        var normalizedCode = code.ToUpperInvariant();
        var room = Get(normalizedCode);
        if (room == null || !room.TryLeave(playerId)) return false;
        if (room.HostId != playerId) return true;
        return ((ICollection<KeyValuePair<string, Room>>)rooms)
            .Remove(new KeyValuePair<string, Room>(normalizedCode, room));
    }
}

sealed class Room
{
    private readonly object gate = new();
    private readonly Random random = new();
    private readonly List<string> clues = new();
    private readonly HashSet<HintCard> usedHints = new();
    private HintCard? previousHint;
    private string lastResult = "";
    private DateTimeOffset turnStartedAt;
    private DateTimeOffset? countdownEndsAt;
    private int activePlayerIndex;
    private int guessesRemaining = 3;
    private int roundNumber = 1;
    public string Code { get; }
    public string HostId { get; }
    public string State { get; private set; } = "lobby";
    public List<RoomPlayer> Players { get; } = new();

    public Room(string code, AuthenticatedPlayer host)
    {
        Code = code;
        HostId = host.Id;
        Players.Add(new RoomPlayer(host.Id, host.DisplayName, host.AvatarUrl));
    }

    public Room Add(AuthenticatedPlayer player)
    {
        lock (gate)
        {
            if (Players.All(p => p.Id != player.Id)) Players.Add(new RoomPlayer(player.Id, player.DisplayName, player.AvatarUrl));
            return this;
        }
    }

    public bool TryAdd(AuthenticatedPlayer player, int maximumPlayers)
    {
        lock (gate)
        {
            if (State != "lobby") return false;
            if (Players.Any(p => p.Id == player.Id)) return true;
            if (Players.Count >= maximumPlayers) return false;
            Players.Add(new RoomPlayer(player.Id, player.DisplayName, player.AvatarUrl));
            UpdateCountdown();
            return true;
        }
    }

    public bool SetReady(string playerId)
    {
        lock (gate)
        {
            var player = Players.FirstOrDefault(p => p.Id == playerId);
            if (player == null || State != "lobby") return false;
            player.Ready = !player.Ready;
            UpdateCountdown();
            return true;
        }
    }

    public bool TryLeave(string playerId)
    {
        lock (gate)
        {
            if (State != "lobby") return false;
            var player = Players.FirstOrDefault(p => p.Id == playerId);
            if (player == null) return false;
            if (playerId != HostId) Players.Remove(player);
            if (playerId == HostId) countdownEndsAt = null;
            else UpdateCountdown();
            return true;
        }
    }

    public bool Start(string playerId)
    {
        lock (gate)
        {
            if (playerId != HostId || Players.Count < 2 || Players.Any(p => !p.Ready) || State != "lobby" ||
                countdownEndsAt == null || DateTimeOffset.UtcNow < countdownEndsAt.Value) return false;
            State = "playing";
            countdownEndsAt = null;
            activePlayerIndex = 0;
            roundNumber = 1;
            guessesRemaining = 3;
            turnStartedAt = DateTimeOffset.UtcNow;
            Players[activePlayerIndex].SecretNumber = random.Next(1, 11);
            clues.Clear();
            return true;
        }
    }

    // Called only under gate. A changed deadline invalidates any previous countdown.
    private void UpdateCountdown()
    {
        if (State != "lobby" || Players.Count < 2 || Players.Any(p => !p.Ready))
        {
            countdownEndsAt = null;
            return;
        }
        if (countdownEndsAt != null) return;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        countdownEndsAt = deadline;
        _ = StartAfterCountdown(deadline);
    }

    private async Task StartAfterCountdown(DateTimeOffset deadline)
    {
        var delay = deadline - DateTimeOffset.UtcNow;
        if (delay > TimeSpan.Zero) await Task.Delay(delay);
        lock (gate)
        {
            if (countdownEndsAt == deadline) Start(HostId);
        }
    }

    public GameAction Guess(string playerId, int number)
    {
        lock (gate)
        {
            if (State != "playing" || Players[activePlayerIndex].Id != playerId) return new(false, "No es tu turno.");
            if (number is < 1 or > 10) return new(false, "Elige un número del 1 al 10.");
            if (TurnExpired()) AdvanceTurn("Se acabó el tiempo. Turno omitido.");
            if (Players[activePlayerIndex].Id != playerId) return new(false, "Se acabó el tiempo. El turno avanzó.");
            guessesRemaining--;
            var secret = Players[activePlayerIndex].SecretNumber;
            if (number == secret || guessesRemaining == 0)
            {
                var points = number == secret ? 3 : Math.Abs(secret - number) == 1 ? 1 : 0;
                var guesser = Players[activePlayerIndex];
                guesser.Score += points;
                lastResult = number == secret ? $"¡Correcto! {guesser.DisplayName} suma {points} puntos." : $"La carta era {secret}. {guesser.DisplayName} suma {points} puntos.";
                AdvanceTurn(null);
                return new(true, lastResult);
            }
            lastResult = $"No era {number}. Quedan {guessesRemaining} intentos.";
            return new(true, lastResult);
        }
    }

    public GameAction AddClue(string playerId, string text)
    {
        lock (gate)
        {
            if (State != "playing") return new(false, "La partida no está activa.");
            if (Players[activePlayerIndex].Id == playerId) return new(false, "Quien adivina no puede dar pistas.");
            if (TurnExpired()) return new(false, "Se acabó el tiempo de este turno.");
            if (string.IsNullOrWhiteSpace(text)) return new(false, "Escribe una pista.");
            var safe = text.Trim();
            if (safe.Length > 120) safe = safe[..120];
            clues.Add($"{Players.First(p => p.Id == playerId).DisplayName}: Es un 10, pero {safe}");
            return new(true, "Pista enviada.");
        }
    }

    public GameAction GetHint(string playerId)
    {
        lock (gate)
        {
            if (State != "playing" || Players[activePlayerIndex].Id == playerId) return new(false, "Solo quienes dan pistas pueden pedir ayuda.");
            if (TurnExpired()) return new(false, "Se acabó el tiempo de este turno.");
            var secret = Players[activePlayerIndex].SecretNumber;
            // Select on the server using the active card, never a number/category from the browser.
            var cards = HintCatalog.Cards.Where(card => card.Number == secret).ToArray();
            var available = cards.Where(card => !usedHints.Contains(card)).ToArray();
            if (available.Length == 0)
            {
                usedHints.RemoveWhere(card => card.Number == secret);
                available = cards.Where(card => card != previousHint).ToArray();
            }
            var categories = available.Select(card => card.Category).Distinct().ToArray();
            var category = categories[random.Next(categories.Length)];
            var options = available.Where(card => card.Category == category).ToArray();
            var hint = options[random.Next(options.Length)];
            usedHints.Add(hint);
            previousHint = hint;
            clues.Add($"{Players.First(p => p.Id == playerId).DisplayName}: Es un 10, pero {hint.Text}");
            return new(true, hint.Text);
        }
    }

    public object ToView(string viewerId)
    {
        lock (gate)
        {
            if (countdownEndsAt != null && DateTimeOffset.UtcNow >= countdownEndsAt.Value) Start(HostId);
            if (State == "playing" && TurnExpired()) AdvanceTurn("Se acabó el tiempo. Turno omitido.");
            var guesserId = State == "playing" ? Players[activePlayerIndex].Id : null;
            return new
            {
                code = Code,
                state = State,
                countdownEndsAt,
                hostId = HostId,
                round = roundNumber,
                activePlayerId = guesserId,
                guessesRemaining,
                turnStartedAt,
                deadline = State == "playing" ? turnStartedAt.AddSeconds(90) : (DateTimeOffset?)null,
                lastResult,
                clues = clues.ToArray(),
                viewerIsGuesser = viewerId == guesserId,
                secretNumber = viewerId == guesserId ? (int?)null : State == "playing" ? Players[activePlayerIndex].SecretNumber : (int?)null,
                players = Players.Select(p => new { id = p.Id, displayName = p.DisplayName, avatarUrl = p.AvatarUrl, ready = p.Ready, score = p.Score }).ToArray()
            };
        }
    }

    private bool TurnExpired() => DateTimeOffset.UtcNow >= turnStartedAt.AddSeconds(90);

    private void AdvanceTurn(string? result)
    {
        if (result != null) lastResult = result;
        if (activePlayerIndex == Players.Count - 1) roundNumber++;
        activePlayerIndex = (activePlayerIndex + 1) % Players.Count;
        Players[activePlayerIndex].SecretNumber = random.Next(1, 11);
        guessesRemaining = 3;
        turnStartedAt = DateTimeOffset.UtcNow;
        clues.Clear();
    }

    public bool ContainsPlayer(string playerId)
    {
        lock (gate) return Players.Any(p => p.Id == playerId);
    }
}

sealed class RoomPlayer(string id, string displayName, string? avatarUrl)
{
    public string Id { get; } = id;
    public string DisplayName { get; } = displayName;
    public string? AvatarUrl { get; } = avatarUrl;
    public bool Ready { get; set; }
    public int Score { get; set; }
    public int SecretNumber { get; set; }
}
