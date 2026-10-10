using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Repositories;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging.Abstractions;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
var port = Environment.GetEnvironmentVariable("PORT") ?? "5080";
builder.Services.AddHttpClient();
builder.Services.AddSingleton<RoomStore>();
builder.Services.AddSingleton<DiscordOAuth>();
builder.Services.AddSingleton<RememberLogin>();
builder.Services.AddSingleton<DeviceLoginStore>();
builder.Services.AddSingleton<AuthTokenStore>();
builder.Services.AddSignalR(options => options.MaximumReceiveMessageSize = 4096);
builder.Services.AddHostedService<RoomMaintenance>();
builder.Services.AddRateLimiter(options => {
    options.RejectionStatusCode = 429;
    options.AddPolicy("api", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Session.GetString("discord_id") ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
});
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
var instanceId = Guid.NewGuid().ToString("N");
app.UseSession();
app.Use(async (context, next) => { context.RequestServices.GetRequiredService<RememberLogin>().Restore(context); await next(context); });
app.UseRateLimiter();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "ok", instanceId }));

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
}).RequireRateLimiting("api");

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
    context.RequestServices.GetRequiredService<RememberLogin>().Issue(context, new(identity.Id, identity.DisplayName, identity.AvatarUrl));
    context.Session.SetString("discord_id", identity.Id);
    context.Session.SetString("display_name", identity.DisplayName);
    if (identity.AvatarUrl != null) context.Session.SetString("avatar_url", identity.AvatarUrl);
    if (device != null) devices.Authorize(device, identity);
    return Results.Redirect("/");
}).RequireRateLimiting("api");

// Unity opens the returned URL in the system browser; Discord's client secret stays on this server.
app.MapPost("/auth/unity/device", (HttpRequest request, DeviceLoginStore devices) =>
{
    var device = devices.Create();
    var baseUrl = $"{request.Scheme}://{request.Host}";
    return Results.Ok(new { deviceId = device.Id, loginUrl = $"{baseUrl}/auth/discord?device={Uri.EscapeDataString(device.Id)}", expiresIn = 300 });
}).RequireRateLimiting("api");

app.MapGet("/auth/unity/device/{deviceId}", (string deviceId, DeviceLoginStore devices, AuthTokenStore tokens) =>
{
    var result = devices.TakeAuthorized(deviceId);
    if (result.IsExpired) return Results.NotFound();
    if (result.Identity == null) return Results.Ok(new { status = "pending" });
    var token = tokens.Issue(result.Identity);
    return Results.Ok(new { status = "authorized", accessToken = token.Value, expiresIn = 86400,
        player = new { id = result.Identity.Id, displayName = result.Identity.DisplayName, avatarUrl = result.Identity.AvatarUrl } });
}).RequireRateLimiting("api");

app.MapGet("/auth/me", (HttpContext context) =>
{
    var id = context.Session.GetString("discord_id");
    var name = context.Session.GetString("display_name");
    var avatar = context.Session.GetString("avatar_url");
    return id == null
        ? Results.Json(new { error = "not_authenticated", loginUrl = "/auth/discord" }, statusCode: StatusCodes.Status401Unauthorized)
        : Results.Ok(new { id, displayName = name, avatarUrl = avatar });
}).RequireRateLimiting("api");

app.MapPost("/auth/logout", (HttpContext context, RememberLogin remembered) => {
    context.Session.Clear(); remembered.Forget(context); return Results.Ok(new { status = "signed_out" });
}).RequireRateLimiting("api");

app.MapPost("/rooms", async (HttpContext context, RoomStore rooms, AuthTokenStore tokens, IHubContext<GameHub> hub) =>
{
    var player = RequirePlayer(context, tokens);
    if (player == null) return Results.Unauthorized();
    var room = rooms.Create(player);
    await hub.Clients.Group(room.Code).SendAsync("roomChanged", room.Code);
    return Results.Ok(room.ToView(player.Id));
}).RequireRateLimiting("api");

app.MapPost("/rooms/{code}/join", async (string code, HttpContext context, RoomStore rooms, AuthTokenStore tokens, IHubContext<GameHub> hub) =>
{
    var player = RequirePlayer(context, tokens);
    if (player == null) return Results.Unauthorized();
    return rooms.Join(code, player) is { } room
        ? await NotifyRoom(hub, room, player.Id)
        : Results.BadRequest("Room does not exist, is full, or the game has started.");
}).RequireRateLimiting("api");

app.MapGet("/rooms/{code}", (string code, HttpContext context, RoomStore rooms, AuthTokenStore tokens) =>
{
    var player = RequirePlayer(context, tokens);
    if (player == null) return Results.Unauthorized();
    return rooms.Get(code) is { } room && room.Touch(player.Id)
        ? Results.Ok(room.ToView(player.Id))
        : Results.NotFound();
}).RequireRateLimiting("api");

app.MapPost("/rooms/{code}/ready", async (string code, HttpContext context, RoomStore rooms, AuthTokenStore tokens, IHubContext<GameHub> hub) =>
{
    var player = RequirePlayer(context, tokens);
    if (player == null) return Results.Unauthorized();
    return rooms.SetReady(code, player.Id) is { } room
        ? await NotifyRoom(hub, room, player.Id)
        : Results.BadRequest("Invalid room or player.");
}).RequireRateLimiting("api");

app.MapPost("/rooms/{code}/leave", async (string code, HttpContext context, RoomStore rooms, AuthTokenStore tokens, IHubContext<GameHub> hub) =>
{
    var player = RequirePlayer(context, tokens);
    if (player == null) return Results.Unauthorized();
    if (!rooms.Leave(code, player.Id)) return Results.BadRequest(new { error = "La mesa ya no está disponible." });
    await hub.Clients.Group(code).SendAsync("roomChanged", code);
    return Results.Ok(new { status = "left" });
}).RequireRateLimiting("api");

app.MapPost("/rooms/{code}/start", async (string code, HttpContext context, RoomStore rooms, AuthTokenStore tokens, IHubContext<GameHub> hub) =>
{
    var player = RequirePlayer(context, tokens);
    if (player == null) return Results.Unauthorized();
    return rooms.Start(code, player.Id) is { } room
        ? await NotifyRoom(hub, room, player.Id)
        : Results.BadRequest("Only the host can start a ready room.");
}).RequireRateLimiting("api");

app.MapPost("/rooms/{code}/guess", async (string code, GuessRequest request, HttpContext context, RoomStore rooms, AuthTokenStore tokens, IHubContext<GameHub> hub) =>
{
    var player = RequirePlayer(context, tokens);
    if (player == null) return Results.Unauthorized();
    var room = rooms.Get(code);
    if (room == null || !room.ContainsPlayer(player.Id)) return Results.NotFound();
    var action = room.Guess(player.Id, request.Number, request.TurnId, request.RequestId);
    if (!action.Accepted) return Results.BadRequest(new { error = action.Message });
    await hub.Clients.Group(room.Code).SendAsync("roomChanged", room.Code);
    return Results.Ok(new { status = action.Message, room = room.ToView(player.Id) });
}).RequireRateLimiting("api");

app.MapPost("/rooms/{code}/clue", async (string code, ClueRequest request, HttpContext context, RoomStore rooms, AuthTokenStore tokens, IHubContext<GameHub> hub) =>
{
    var player = RequirePlayer(context, tokens);
    if (player == null) return Results.Unauthorized();
    var room = rooms.Get(code);
    if (room == null || !room.ContainsPlayer(player.Id)) return Results.NotFound();
    var action = room.AddClue(player.Id, request.Text, request.TurnId);
    if (!action.Accepted) return Results.BadRequest(new { error = action.Message });
    await hub.Clients.Group(room.Code).SendAsync("roomChanged", room.Code);
    return Results.Ok(room.ToView(player.Id));
}).RequireRateLimiting("api");

app.MapPost("/rooms/{code}/hint", async (string code, TurnRequest request, HttpContext context, RoomStore rooms, AuthTokenStore tokens, IHubContext<GameHub> hub) =>
{
    var player = RequirePlayer(context, tokens);
    if (player == null) return Results.Unauthorized();
    var room = rooms.Get(code);
    if (room == null || !room.ContainsPlayer(player.Id)) return Results.NotFound();
    var action = room.GetHint(player.Id, request.TurnId);
    if (!action.Accepted) return Results.BadRequest(new { error = action.Message });
    await hub.Clients.Group(room.Code).SendAsync("roomChanged", room.Code);
    return Results.Ok(new { suggestion = action.Message, room = room.ToView(player.Id) });
}).RequireRateLimiting("api");

app.MapPost("/rooms/{code}/rematch", async (string code, HttpContext context, RoomStore rooms, AuthTokenStore tokens, IHubContext<GameHub> hub) => {
    var player = RequirePlayer(context, tokens);
    if (player == null) return Results.Unauthorized();
    var room = rooms.Get(code);
    if (room?.Rematch(player.Id) != true) return Results.BadRequest(new { error = "Solo el anfitrión puede preparar la revancha al terminar." });
    return await NotifyRoom(hub, room, player.Id);
}).RequireRateLimiting("api");
app.MapPost("/rooms/{code}/hint-rating", (string code, HintRatingRequest request, HttpContext context, RoomStore rooms, AuthTokenStore tokens, ILoggerFactory logs) => {
    var player = RequirePlayer(context, tokens);
    if (player == null) return Results.Unauthorized();
    var room = rooms.Get(code);
    var cards = room?.RateHints(player.Id, request.TurnId);
    if (cards == null) return Results.BadRequest(new { error = "Esta valoración ya se envió o su turno terminó." });
    // Catalog IDs and a vote only: no account names or private messages in feedback logs.
    foreach (var card in cards) logs.CreateLogger("HintFeedback").LogInformation("Hint {CardId}: helpful={Helpful}", card, request.Helpful);
    return Results.Ok(room!.ToView(player.Id));
}).RequireRateLimiting("api");

app.MapHub<GameHub>("/realtime").RequireRateLimiting("api");

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
    await hub.Clients.Group(room.Code).SendAsync("roomChanged", room.Code);
    return Results.Ok(view);
}

sealed class DeviceLoginStore
{
    private readonly ConcurrentDictionary<string, DeviceLogin> pending = new();
    public void Prune() { foreach (var p in pending) if (p.Value.ExpiresAt <= DateTimeOffset.UtcNow) pending.TryRemove(p.Key, out _); }
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
    public void Prune() { foreach (var p in tokens) if (p.Value.ExpiresAt <= DateTimeOffset.UtcNow) tokens.TryRemove(p.Key, out _); }
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
    private string? PlayerId()
    {
        var context = Context.GetHttpContext();
        var raw = context?.Request.Headers.Authorization.ToString() ?? "";
        var token = raw.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? raw[7..] : context?.Request.Query["access_token"].ToString();
        return !string.IsNullOrEmpty(token) ? tokens.Resolve(token)?.Id : context?.Session.GetString("discord_id");
    }
    public override Task OnConnectedAsync()
    {
        var id = PlayerId();
        if (id == null) { Context.Abort(); return Task.CompletedTask; }
        Context.Items["playerId"] = id;
        return base.OnConnectedAsync();
    }
    public async Task JoinRoom(string code)
    {
        var id = Context.Items["playerId"] as string;
        if (id == null || rooms.Get(code)?.Touch(id) != true) throw new HubException("La mesa ya no está disponible.");
        if (Context.Items["room"] is string previous) await Groups.RemoveFromGroupAsync(Context.ConnectionId, previous);
        Context.Items["room"] = code.ToUpperInvariant();
        await Groups.AddToGroupAsync(Context.ConnectionId, code.ToUpperInvariant());
        await Clients.Group(code.ToUpperInvariant()).SendAsync("roomChanged", code.ToUpperInvariant());
    }
    public Task LeaveRoom() => Context.Items["room"] is string code ? Groups.RemoveFromGroupAsync(Context.ConnectionId, code) : Task.CompletedTask;
    public void Pulse(string code)
    {
        var id = Context.Items["playerId"] as string;
        if (id == null || rooms.Get(code)?.Touch(id) != true) throw new HubException("La mesa ya no está disponible.");
    }
}

record GuessRequest(int Number, string? TurnId, string? RequestId);
record ClueRequest(string? Text, string? TurnId);
record TurnRequest(string? TurnId);
record HintRatingRequest(string? TurnId, bool Helpful);
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
