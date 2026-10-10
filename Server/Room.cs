using System.Security.Cryptography;

sealed class Room
{
    private readonly object gate = new();
    private readonly TimeProvider time;
    private readonly List<RoomPlayer> players = new();
    private readonly List<string> clues = new();
    private readonly HashSet<HintCard> usedHints = new();
    private readonly HashSet<int> tried = new();
    private readonly HashSet<string> requests = new();
    private readonly List<int> turnHints = new();
    private readonly HashSet<string> feedbackVoters = new();
    private readonly Queue<int> numberDeck = new();
    private HintCard? previousHint;
    private int activeIndex, secret, round = 1, hintsUsed;
    private int turnSeconds = 90, targetScore;
    private long version = 1;
    private DateTimeOffset turnStartedAt, lastHintAt = DateTimeOffset.MinValue;
    private DateTimeOffset? countdownEndsAt, revealEndsAt;
    private string state = "lobby", turnId = "", result = "";
    private DateTimeOffset Now => time.GetUtcNow();
    public string Code { get; }
    public string HostId { get; private set; }
    public DateTimeOffset LastActivity { get; private set; }
    public long Version { get { lock (gate) return version; } }

    public Room(string code, AuthenticatedPlayer host, TimeProvider? clock = null)
    {
        Code = code; HostId = host.Id; time = clock ?? TimeProvider.System;
        LastActivity = Now; players.Add(new(host, Now));
    }
    public bool IsExpired() { lock (gate) return players.All(p => p.Left) || Now - LastActivity > TimeSpan.FromMinutes(30); }
    public bool ContainsPlayer(string id) { lock (gate) return players.Any(p => p.Id == id && !p.Left); }
    public bool Touch(string id)
    {
        lock (gate)
        {
            var p = players.FirstOrDefault(p => p.Id == id && !p.Left);
            if (p == null) return false;
            p.LastSeen = LastActivity = Now;
            if (!p.Online) { p.Online = true; version++; }
            return true;
        }
    }
    public bool TryAdd(AuthenticatedPlayer player, int max)
    {
        lock (gate)
        {
            if (ContainsPlayer(player.Id)) return Touch(player.Id);
            if (state != "lobby" || players.Count >= max) return false;
            players.Add(new(player, Now)); LastActivity = Now; TransferHost(); UpdateCountdown(); version++;
            return true;
        }
    }
    public bool SetReady(string id)
    {
        lock (gate)
        {
            Tick();
            if (state != "lobby" || !Touch(id)) return false;
            var p = players.First(p => p.Id == id); p.Ready = !p.Ready;
            UpdateCountdown(); version++; return true;
        }
    }
    public GameAction Configure(string id, int seconds, int points)
    {
        lock (gate)
        {
            Tick();
            if (state != "lobby" || id != HostId || !ContainsPlayer(id))
                return new(false, "Solo el anfitrión puede configurar la mesa antes de empezar.");
            if (seconds is < 30 or > 300 || points is < 0 or > 100)
                return new(false, "El tiempo debe estar entre 30 y 300 segundos y la meta entre 1 y 100 puntos (0 para tres vueltas).");
            if (turnSeconds == seconds && targetScore == points) return new(true, "Ajustes guardados.");
            turnSeconds = seconds; targetScore = points; countdownEndsAt = null;
            foreach (var player in players) player.Ready = false;
            Touch(id); version++;
            return new(true, "Ajustes guardados. Todos deben volver a marcarse listos.");
        }
    }
    public bool TryLeave(string id)
    {
        lock (gate)
        {
            var p = players.FirstOrDefault(p => p.Id == id && !p.Left);
            if (p == null) return false;
            p.Left = true; p.Online = false; p.Ready = false;
            if (state is "lobby" or "finished") players.Remove(p);
            TransferHost(); UpdateCountdown(); version++;
            if (state is "playing" or "reveal")
            {
                if (players.Count(x => !x.Left) < 2) Finish("La mesa terminó: quedan menos de dos jugadores.");
                else if (state == "playing" && players[activeIndex] == p) Reveal("El jugador abandonó la mesa. Turno omitido.", 0);
            }
            return true;
        }
    }
    private void TransferHost()
    {
        if (!players.Any(p => p.Id == HostId && !p.Left && p.Online))
            HostId = players.FirstOrDefault(p => !p.Left && p.Online)?.Id ?? players.FirstOrDefault(p => !p.Left)?.Id ?? "";
    }
    private void UpdateCountdown()
    {
        if (state != "lobby") return;
        if (players.Count >= 2 && players.All(p => p.Ready && p.Online && !p.Left)) countdownEndsAt ??= Now.AddSeconds(10);
        else countdownEndsAt = null;
    }
    public bool Start(string id)
    {
        lock (gate)
        {
            if (id != HostId || state != "lobby" || countdownEndsAt == null || Now < countdownEndsAt || !players.All(p => p.Ready && p.Online) || players.Count < 2) return false;
            countdownEndsAt = null; activeIndex = 0; round = 1; BeginTurn(); return true;
        }
    }
    public void Tick()
    {
        lock (gate)
        {
            foreach (var p in players)
            {
                var online = !p.Left && Now - p.LastSeen < TimeSpan.FromSeconds(25);
                if (online != p.Online) { p.Online = online; if (!online && state == "lobby") p.Ready = false; version++; }
            }
            var previousHost = HostId; TransferHost(); if (previousHost != HostId) version++;
            if (state == "lobby")
            {
                if (players.RemoveAll(p => Now - p.LastSeen > TimeSpan.FromMinutes(2)) > 0) version++;
                var oldHost = HostId; TransferHost(); if (oldHost != HostId) version++;
                var oldCountdown = countdownEndsAt; UpdateCountdown(); if (oldCountdown != countdownEndsAt) version++;
                if (countdownEndsAt <= Now) Start(HostId);
            }
            if (state is "playing" or "reveal")
            {
                var available = players.Count(p => !p.Left && Now - p.LastSeen < TimeSpan.FromSeconds(60));
                if (available < 2) { Finish("La mesa terminó por desconexión. Vuelve a reunir al grupo para la revancha."); return; }
            }
            if (state == "playing")
            {
                if (Now - players[activeIndex].LastSeen >= TimeSpan.FromSeconds(60)) Reveal("No volvió a tiempo. Turno omitido.", 0);
                else if (Now >= turnStartedAt.AddSeconds(turnSeconds)) Reveal("Se acabó el tiempo.", 0);
            }
            if (state == "reveal" && revealEndsAt <= Now) NextTurn();
        }
    }
    private void BeginTurn()
    {
        state = "playing"; turnId = Guid.NewGuid().ToString("N"); secret = DrawNumber();
        turnStartedAt = Now; revealEndsAt = null; result = ""; hintsUsed = 0; lastHintAt = DateTimeOffset.MinValue;
        tried.Clear(); requests.Clear(); clues.Clear(); turnHints.Clear(); feedbackVoters.Clear(); version++;
    }
    private int DrawNumber()
    {
        // Keep the deck across rematches. Each batch uses all ten numbers once.
        if (numberDeck.Count == 0)
        {
            var numbers = Enumerable.Range(1, 10).ToArray();
            for (var i = numbers.Length - 1; i > 0; i--)
            {
                var j = RandomNumberGenerator.GetInt32(i + 1);
                (numbers[i], numbers[j]) = (numbers[j], numbers[i]);
            }
            // Avoid repeating the last card at the boundary between two decks.
            if (numbers[0] == secret)
            {
                var j = RandomNumberGenerator.GetInt32(1, numbers.Length);
                (numbers[0], numbers[j]) = (numbers[j], numbers[0]);
            }
            foreach (var number in numbers) numberDeck.Enqueue(number);
        }
        return numberDeck.Dequeue();
    }
    private void Reveal(string message, int points)
    {
        players[activeIndex].Score += points;
        result = $"{message} La carta era {secret}. {players[activeIndex].DisplayName} suma {points} puntos.";
        state = "reveal"; revealEndsAt = Now.AddSeconds(4); version++;
    }
    private void NextTurn()
    {
        var winner = players.FirstOrDefault(p => !p.Left && targetScore > 0 && p.Score >= targetScore);
        if (winner != null) { Finish($"¡{winner.DisplayName} alcanzó la meta de {targetScore} puntos!"); return; }
        do
        {
            activeIndex++;
            if (activeIndex >= players.Count) { activeIndex = 0; round++; }
            if (targetScore == 0 && round > 3) { Finish("¡Partida terminada! Tres vueltas completas."); return; }
        } while (players[activeIndex].Left || Now - players[activeIndex].LastSeen >= TimeSpan.FromSeconds(60));
        BeginTurn();
    }
    private void Finish(string message) { state = "finished"; result = message; countdownEndsAt = revealEndsAt = null; version++; }
    public bool Rematch(string id)
    {
        lock (gate)
        {
            Tick();
            if (state != "finished" || id != HostId || !Touch(id)) return false;
            players.RemoveAll(p => p.Left || Now - p.LastSeen >= TimeSpan.FromSeconds(60));
            foreach (var p in players) { p.Score = 0; p.Ready = false; }
            TransferHost(); state = "lobby"; round = 1; turnId = ""; result = ""; clues.Clear(); version++;
            return true;
        }
    }
    public GameAction Guess(string id, int number, string? expectedTurn, string? requestId)
    {
        lock (gate)
        {
            Tick();
            if (!Touch(id) || state != "playing" || players[activeIndex].Id != id || expectedTurn != turnId) return new(false, "Ese turno ya terminó o no te corresponde.");
            if (number is < 1 or > 10 || !Guid.TryParse(requestId, out _)) return new(false, "Intento no válido.");
            if (requests.Contains(requestId!) || tried.Contains(number)) return new(false, "Ese intento ya se procesó. Elige otro número.");
            requests.Add(requestId!); tried.Add(number);
            if (number == secret || tried.Count == 3)
            {
                var points = Math.Abs(secret - number) switch { 0 => 3, 1 => 2, 2 => 1, _ => 0 };
                Reveal(number == secret ? "¡Correcto!" : "Se agotaron los intentos.", points);
            }
            else { result = $"No era {number}. Quedan {3 - tried.Count} intentos."; version++; }
            return new(true, result);
        }
    }
    public GameAction AddClue(string id, string? text, string? expectedTurn)
    {
        lock (gate)
        {
            Tick();
            if (!CanClue(id, expectedTurn)) return new(false, "Solo quien da pistas puede enviarlas durante este turno.");
            var p = players.First(p => p.Id == id);
            if (Now - p.LastClue < TimeSpan.FromSeconds(2) || clues.Count >= 40) return new(false, "Espera un momento antes de enviar otra pista.");
            if (string.IsNullOrWhiteSpace(text)) return new(false, "Escribe una pista.");
            p.LastClue = Now; var safe = text.Trim(); if (safe.Length > 120) safe = safe[..120];
            clues.Add($"{p.DisplayName}: Es un 10, pero {safe}"); version++; return new(true, "Pista enviada.");
        }
    }
    private bool CanClue(string id, string? expectedTurn) => Touch(id) && state == "playing" && players[activeIndex].Id != id && expectedTurn == turnId;
    public GameAction GetHint(string id, string? expectedTurn)
    {
        lock (gate)
        {
            Tick();
            if (!CanClue(id, expectedTurn)) return new(false, "Solo quien da pistas puede pedir ayuda durante este turno.");
            if (hintsUsed >= 3) return new(false, "Ya usasteis las tres ayudas de este turno.");
            if (Now - lastHintAt < TimeSpan.FromSeconds(8)) return new(false, "Deja ocho segundos entre ayudas para escuchar al grupo.");
            var cards = HintCatalog.Cards.Where(c => c.Number == secret).ToArray();
            var available = cards.Where(c => !usedHints.Contains(c)).ToArray();
            if (available.Length == 0) { usedHints.RemoveWhere(c => c.Number == secret); available = cards.Where(c => c != previousHint).ToArray(); }
            var categories = available.Select(c => c.Category).Distinct().ToArray();
            var category = categories[RandomNumberGenerator.GetInt32(categories.Length)];
            var options = available.Where(c => c.Category == category).ToArray();
            var hint = options[RandomNumberGenerator.GetInt32(options.Length)];
            usedHints.Add(hint); previousHint = hint; hintsUsed++; lastHintAt = Now;
            turnHints.Add(Array.IndexOf(HintCatalog.Cards, hint));
            clues.Add($"{players.First(p => p.Id == id).DisplayName}: Es un 10, pero {hint.Text}");
            version++; return new(true, hint.Text);
        }
    }
    public int[]? RateHints(string id, string? expectedTurn)
    {
        lock (gate)
        {
            if (state != "reveal" || expectedTurn != turnId || players[activeIndex].Id != id || turnHints.Count == 0 || !feedbackVoters.Add(id)) return null;
            version++; return turnHints.ToArray();
        }
    }
    public object ToView(string viewerId)
    {
        lock (gate)
        {
            Tick();
            var active = state is "playing" or "reveal" ? players[activeIndex].Id : null;
            return new {
                code = Code, state, version, serverTime = Now, hostId = HostId, round = targetScore == 0 ? Math.Min(round, 3) : round, totalRounds = targetScore == 0 ? (int?)3 : null,
                settings = new { turnSeconds, targetScore },
                turnId, countdownEndsAt, revealEndsAt, activePlayerId = active, viewerIsGuesser = viewerId == active,
                guessesRemaining = 3 - tried.Count, triedNumbers = tried.ToArray(), turnStartedAt,
                deadline = state == "playing" ? turnStartedAt.AddSeconds(turnSeconds) : (DateTimeOffset?)null,
                secretNumber = state == "reveal" || (state == "playing" && viewerId != active) ? (int?)secret : null,
                lastResult = result, clues = clues.ToArray(), hintsRemaining = 3 - hintsUsed,
                nextHintAt = lastHintAt == DateTimeOffset.MinValue ? (DateTimeOffset?)null : lastHintAt.AddSeconds(8),
                canRateHints = state == "reveal" && viewerId == active && turnHints.Count > 0 && !feedbackVoters.Contains(viewerId),
                players = players.Select(p => new { id = p.Id, displayName = p.DisplayName, avatarUrl = p.AvatarUrl, ready = p.Ready, score = p.Score, online = p.Online, left = p.Left }).ToArray()
            };
        }
    }
}

sealed class RoomPlayer(AuthenticatedPlayer player, DateTimeOffset now)
{
    public string Id { get; } = player.Id;
    public string DisplayName { get; } = player.DisplayName;
    public string? AvatarUrl { get; } = player.AvatarUrl;
    public bool Ready { get; set; }
    public int Score { get; set; }
    public bool Online { get; set; } = true;
    public bool Left { get; set; }
    public DateTimeOffset LastSeen { get; set; } = now;
    public DateTimeOffset LastClue { get; set; } = DateTimeOffset.MinValue;
}
