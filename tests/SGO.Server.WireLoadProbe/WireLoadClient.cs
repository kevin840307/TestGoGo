using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using SGO.Server.Protocol;

internal sealed class WireLoadClient : IAsyncDisposable
{
    private static readonly string[] BootstrapTypes =
    [
        "InventorySync",
        "EquipSync",
        "PetSync",
        "HorseSync",
        "SkillSync",
        "ClubSync",
        "SyncBuffs",
        "QuestSync",
        "FestivalThemeSync"
    ];

    private readonly ClientWebSocket _socket = new();
    private readonly Channel<JsonElement> _inbox =
        Channel.CreateUnbounded<JsonElement>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true
            });
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private Task? _receiveTask;
    private Exception? _receiveFailure;
    private volatile bool _measure;
    private volatile bool _interactiveBots;
    private long _moveBroadcasts;
    private long _positionCorrections;
    private long _chatBroadcasts;
    private long _combatDeaths;
    private long _combatEvents;
    private long _combatTargetEntityId;
    private volatile bool _dropCombatNoise;
    private long _sentBytes;
    private long _receivedBytes;

    private WireLoadClient(int index)
    {
        Index = index;
    }

    public int Index { get; }
    public BotWorldState BotWorld { get; } = new();
    public long CharacterId { get; private set; }
    public int MapId { get; private set; }
    public int X { get; private set; }
    public int Y { get; private set; }

    public long MoveBroadcasts =>
        Interlocked.Read(ref _moveBroadcasts);

    public long PositionCorrections =>
        Interlocked.Read(ref _positionCorrections);

    public long ChatBroadcasts =>
        Interlocked.Read(ref _chatBroadcasts);

    public long CombatDeaths =>
        Interlocked.Read(ref _combatDeaths);

    public long CombatEvents =>
        Interlocked.Read(ref _combatEvents);

    public long SentBytes =>
        Interlocked.Read(ref _sentBytes);

    public long ReceivedBytes =>
        Interlocked.Read(ref _receivedBytes);

    public static async Task<WireLoadClient> ConnectAndEnterAsync(
        string url,
        string runId,
        int index,
        TimeSpan timeout,
        Action<int, string>? progress = null)
    {
        var client = new WireLoadClient(index);

        try
        {
            using (var connectTimeout =
                   new CancellationTokenSource(timeout))
            {
                await client._socket.ConnectAsync(
                    new Uri(url),
                    connectTimeout.Token);
            }

            progress?.Invoke(index, "websocket-connected");
            client._receiveTask =
                client.ReceiveLoopAsync();

            await client.SetupAsync(
                runId,
                timeout,
                progress);

            return client;
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }

    public void ClearInbox()
    {
        while (_inbox.Reader.TryRead(out _))
        {
        }
    }

    public void StartMeasurement(
        long combatTargetEntityId = 0,
        bool dropCombatNoise = false)
    {
        Interlocked.Exchange(
            ref _moveBroadcasts,
            0);
        Interlocked.Exchange(
            ref _positionCorrections,
            0);
        Interlocked.Exchange(
            ref _chatBroadcasts,
            0);
        Interlocked.Exchange(
            ref _combatDeaths,
            0);
        Interlocked.Exchange(
            ref _combatEvents,
            0);
        Interlocked.Exchange(
            ref _combatTargetEntityId,
            combatTargetEntityId);
        _dropCombatNoise =
            dropCombatNoise;
        Interlocked.Exchange(
            ref _sentBytes,
            0);
        Interlocked.Exchange(
            ref _receivedBytes,
            0);
        _measure = true;
    }

    public void EnableInteractiveBots()
    {
        _interactiveBots = true;
        while (_inbox.Reader.TryRead(out var packet))
            BotWorld.Observe(packet, CharacterId);
        StartMeasurement(dropCombatNoise: true);
    }

    public void StopMeasurement()
    {
        _measure = false;
        _dropCombatNoise = false;
        Interlocked.Exchange(
            ref _combatTargetEntityId,
            0);
    }

    public async Task TeleportAsync(
        int mapId,
        int x,
        int y,
        TimeSpan timeout)
    {
        await SendPacketAsync(
            new
            {
                Type = "ChatMessage",
                Channel = 0,
                Message =
                    $"/gm map {mapId} {x} {y}",
                BubbleType = 0
            },
            timeout);

        var change = await WaitForPacketAsync(
            "ChangeMap",
            root =>
                root.TryGetProperty(
                    "MapID",
                    out var foundMap) &&
                foundMap.GetInt32() ==
                    mapId,
            timeout,
            failOnGmError: true);

        MapId =
            change.GetProperty("MapID")
                .GetInt32();
        X = (int)Math.Round(
            change.GetProperty("X")
                .GetDouble());
        Y = (int)Math.Round(
            change.GetProperty("Y")
                .GetDouble());

        var ready = await RequestAsync(
            new
            {
                Type = "WorldReady",
                MapID = MapId
            },
            "WorldReadyResponse",
            timeout);

        RequireSuccess(
            ready,
            "AOI teleport WorldReady");
    }

    public async Task<bool> TryWaitForEntityPacketAsync(
        string packetType,
        long entityId,
        TimeSpan timeout)
    {
        try
        {
            await WaitForPacketAsync(
                packetType,
                root =>
                    root.TryGetProperty(
                        "EntityID",
                        out var found) &&
                    found.GetInt64() ==
                        entityId,
                timeout);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (TimeoutException)
        {
            // Waiting for absence in a distant AOI is expected: do not
            // abort the whole test when no SpawnEntity is received.
            // Other request/GM timeouts still propagate normally.
            return false;
        }
    }

    public Task SendPositionAsync(
        TimeSpan timeout,
        int offsetX = 0)
        => SendPacketAsync(
            new
            {
                Type = "SyncPosition",
                X = X + offsetX,
                Y,
                Dir = "1"
            },
            timeout);

    public Task SendStateAsync(string state, TimeSpan timeout)
        => SendPacketAsync(
            new { Type = "SyncState", State = state, Dir = "1" },
            timeout);

    public Task<JsonElement> WaitForExactMoveAsync(
        long entityId,
        int x,
        int y,
        TimeSpan timeout)
        => WaitForPacketAsync(
            "MoveEntity",
            packet =>
                packet.TryGetProperty("EntityID", out var id) &&
                id.GetInt64() == entityId &&
                packet.TryGetProperty("X", out var px) &&
                px.GetDouble() == x &&
                packet.TryGetProperty("Y", out var py) &&
                py.GetDouble() == y,
            timeout);

    public Task SendNearbyChatAsync(
        string message,
        TimeSpan timeout)
        => SendPacketAsync(
            new
            {
                Type = "ChatMessage",
                Channel = 0,
                Message = message,
                BubbleType = 0
            },
            timeout);

    public Task SendAttackAsync(
        int targetEntityId,
        TimeSpan timeout)
        => SendPacketAsync(
            new
            {
                Type = "AttackEntity",
                TargetEntityID = targetEntityId,
                TargetType = 1
            },
            timeout);

    public async Task SendBotAttackWithAnimationAsync(
        int targetEntityId,
        double targetX,
        double targetY,
        TimeSpan timeout)
    {
        // Normal Godot players play their local attack animation immediately.
        // A headless bot has no player scene, so notify observing clients of
        // its action using the existing SyncState contract as well.
        var dx = targetX - X;
        var dy = targetY - Y;
        var angle = Math.Atan2(dy, dx);
        var octant = ((int)Math.Round(angle / (Math.PI / 4)) + 8) % 8;
        // Match the actual BaseEntity direction ordering and unarmed
        // attack animation (attack_aa), not the non-existent "attack_1".
        var directions = new[] { "6", "7", "8", "1", "2", "3", "4", "5" };
        var direction = directions[octant];
        await SendPacketAsync(
            new { Type = "SyncState", State = "attack_aa", Dir = direction },
            timeout);
        await SendAttackAsync(targetEntityId, timeout);
    }

    public async Task PrepareCombatAsync(
        TimeSpan timeout,
        int level = 255)
    {
        await SendGmForTypeAsync(
            $"level {level}",
            "PlayerStatusSync",
            timeout);
        await SendGmForTypeAsync(
            "maxstats",
            "AllocateStatResponse",
            timeout);
        await SendGmForTypeAsync(
            "heal",
            "PlayerStatusSync",
            timeout);
    }

    public async Task PrepareWeakBotProfileAsync(TimeSpan timeout)
    {
        // Test-only GM command is separately gated by
        // Game__TestBotCommandsEnabled=true on an isolated server.
        // Fail explicitly instead of pretending the bot has low attack.
        await SendPacketAsync(
            new
            {
                Type = "ChatMessage",
                Channel = 0,
                Message = "/gm testbot",
                BubbleType = 0
            }, timeout);
        // Unrelated periodic status packets may race the GM response.
        // Only accept the confirmed weak test profile, never an older status.
        await WaitForPacketAsync(
            "PlayerStatusSync",
            packet =>
                packet.TryGetProperty("Attack", out var attack) &&
                attack.ValueKind == JsonValueKind.Number &&
                attack.GetInt32() is >= 0 and <= 5 &&
                packet.TryGetProperty("MaxHP", out var maxHp) &&
                maxHp.ValueKind == JsonValueKind.Number &&
                maxHp.GetInt32() >= 10000,
            timeout,
            failOnGmError: true);
    }

    public Task LearnBotSkillAsync(int skillId, TimeSpan timeout)
        => SendGmForTypeAsync(
            $"skill {skillId} 1",
            "SkillSync",
            timeout);

    public Task RefreshWeakBotProfileAsync(TimeSpan timeout)
        => SendPacketAsync(
            new
            {
                Type = "ChatMessage",
                Channel = 0,
                Message = "/gm testbot",
                BubbleType = 0
            },
            timeout);

    public Task SendBotHealAsync(TimeSpan timeout)
        => SendPacketAsync(
            new { Type = "ChatMessage", Channel = 0,
                  Message = "/gm heal", BubbleType = 0 },
            timeout);

    public async Task SendBotPositionAsync(int x, int y, TimeSpan timeout)
    {
        // Record optimistic position; the server's PositionCorrection wins
        // when a blocked step is rejected.
        var beforeX = X;
        var beforeY = Y;
        X = x;
        Y = y;
        try
        {
            await SendPacketAsync(
                new { Type = "SyncPosition", X = x, Y = y, Dir = "1" },
                timeout);
        }
        catch
        {
            X = beforeX;
            Y = beforeY;
            throw;
        }
    }

    public Task SendBotSkillAsync(
        int skillId, int targetEntityId, TimeSpan timeout)
    {
        var selfCentered = skillId == 99001;
        return SendPacketAsync(
            new
            {
                Type = "UseSkill",
                SkillID = skillId,
                TargetID = selfCentered
                    ? checked((int)CharacterId) : targetEntityId,
                TargetType = selfCentered ? 0 : 1,
                TargetX = X,
                TargetY = Y
            },
            timeout);
    }

    public async Task<int> SpawnMonsterAsync(
        int enemyId,
        int x,
        int y,
        TimeSpan timeout)
    {
        await SendPacketAsync(
            new
            {
                Type = "ChatMessage",
                Channel = 0,
                Message =
                    $"/gm spawn {enemyId} {x} {y}",
                BubbleType = 0
            },
            timeout);

        var packet = await WaitForPacketAsync(
            "SpawnEntity",
            root =>
                root.TryGetProperty(
                    "EntityType",
                    out var entityType) &&
                entityType.GetInt32() == 1 &&
                root.TryGetProperty(
                    "ExtraData",
                    out var extra) &&
                extra.TryGetProperty(
                    "EnemyID",
                    out var foundEnemyId) &&
                foundEnemyId.GetInt32() ==
                    enemyId,
            timeout,
            failOnGmError: true);

        BotWorld.Observe(packet, CharacterId);
        return packet.GetProperty(
                "EntityID")
            .GetInt32();
    }

    private async Task SetupAsync(
        string runId,
        TimeSpan timeout,
        Action<int, string>? progress = null)
    {
        var loginName =
            $"wire-load-{runId}-{Index:D3}";
        var characterName =
            $"WL{Index:D3}{runId[..5]}";
        // Bots are now permitted on the existing Server/database. Do
        // not leave predictable login credentials in persistent accounts.
        var password = Convert.ToHexString(
            RandomNumberGenerator.GetBytes(24));

        progress?.Invoke(Index, "register-account");
        var register = await RequestAsync(
            new
            {
                Type = "RegisterAccountRequest",
                LoginName = loginName,
                Password = password,
                EMAIL = $"{loginName}@example.invalid"
            },
            "RegisterAccountResponse",
            timeout);

        RequireSuccess(
            register,
            "register");

        progress?.Invoke(Index, "login");
        var login = await RequestAsync(
            new
            {
                Type = "LoginRequest",
                User = loginName,
                Pass = password
            },
            "LoginResponse",
            timeout);

        RequireSuccess(
            login,
            "login");

        var characters =
            login.GetProperty("Characters");
        if (characters.ValueKind !=
                JsonValueKind.Array ||
            characters.GetArrayLength() != 3)
        {
            throw new InvalidOperationException(
                "login did not return the three-slot character list");
        }

        var accountId =
            characters[0]
                .GetProperty("ACCID")
                .GetInt64();

        progress?.Invoke(Index, "create-character");
        var create = await RequestAsync(
            new
            {
                Type = "CreateCharacterRequest",
                ACCID = accountId,
                Name = characterName,
                Race = Index % 2,
                Hair = 1,
                HairColor = 1,
                Password = "1234",
                Str = 5,
                Dex = 5,
                Con = 5,
                Wis = 5
            },
            "CreateCharacterResponse",
            timeout);

        RequireSuccess(
            create,
            "create character");

        progress?.Invoke(Index, "relogin");
        var relogin = await RequestAsync(
            new
            {
                Type = "LoginRequest",
                User = loginName,
                Pass = password
            },
            "LoginResponse",
            timeout);

        RequireSuccess(
            relogin,
            "relogin");

        var character =
            relogin.GetProperty("Characters")
                .EnumerateArray()
                .FirstOrDefault(x =>
                    x.TryGetProperty(
                        "NAME",
                        out var name) &&
                    name.GetString() ==
                        characterName);

        if (character.ValueKind !=
            JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                "created character was not returned after relogin");
        }

        CharacterId =
            character.GetProperty("DBID")
                .GetInt64();

        progress?.Invoke(Index, "enter-world");
        var enter = await RequestAsync(
            new
            {
                Type = "SelectCharacterRequest",
                DBID = CharacterId
            },
            "EnterWorldSuccess",
            timeout);

        MapId =
            enter.GetProperty("MapID")
                .GetInt32();
        X = (int)Math.Round(
            enter.GetProperty("LocX")
                .GetDouble());
        Y = (int)Math.Round(
            enter.GetProperty("LocY")
                .GetDouble());

        progress?.Invoke(Index, "bootstrap");
        await CollectBootstrapAsync(timeout);

        var ready = await RequestAsync(
            new
            {
                Type = "WorldReady",
                MapID = MapId
            },
            "WorldReadyResponse",
            timeout);

        RequireSuccess(
            ready,
            "WorldReady");
        progress?.Invoke(Index, "entered-world");
    }

    private async Task CollectBootstrapAsync(
        TimeSpan timeout)
    {
        var pending =
            new HashSet<string>(
                BootstrapTypes,
                StringComparer.Ordinal);

        using var deadline =
            new CancellationTokenSource(timeout);

        while (pending.Count > 0)
        {
            JsonElement packet;
            try
            {
                packet = await ReadInboxAsync(deadline.Token);
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested)
            {
                throw new TimeoutException(
                    "Bootstrap packets missing: " +
                    string.Join(", ", pending.OrderBy(x => x)));
            }

            if (!packet.TryGetProperty(
                    "Type",
                    out var typeElement))
            {
                continue;
            }

            var type =
                typeElement.GetString();

            if (!string.IsNullOrWhiteSpace(type))
                pending.Remove(type);
        }
    }

    private async Task<JsonElement> RequestAsync(
        object request,
        string responseType,
        TimeSpan timeout)
    {
        await SendPacketAsync(
            request,
            timeout);

        return await WaitForPacketAsync(
            responseType,
            _ => true,
            timeout);
    }

    private async Task<JsonElement> SendGmForTypeAsync(
        string command,
        string responseType,
        TimeSpan timeout)
    {
        await SendPacketAsync(
            new
            {
                Type = "ChatMessage",
                Channel = 0,
                Message = "/gm " + command,
                BubbleType = 0
            },
            timeout);

        return await WaitForPacketAsync(
            responseType,
            _ => true,
            timeout,
            failOnGmError: true);
    }

    private async Task<JsonElement> WaitForPacketAsync(
        string responseType,
        Func<JsonElement, bool> predicate,
        TimeSpan timeout,
        bool failOnGmError = false)
    {
        using var deadline = new CancellationTokenSource(timeout);
        while (true)
        {
            JsonElement packet;
            try
            {
                packet = await ReadInboxAsync(deadline.Token);
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Waiting for {responseType} exceeded {timeout.TotalSeconds:F0}s; " +
                    "check server logs, game GM permissions and map bootstrap.");
            }
            BotWorld.Observe(packet, CharacterId);
            if (!packet.TryGetProperty("Type", out var typeElement))
                continue;
            var kind = typeElement.GetString();
            if (failOnGmError && kind == "SystemMessageChat" &&
                packet.TryGetProperty("Message", out var text) &&
                text.ValueKind == JsonValueKind.String)
            {
                var message = text.GetString() ?? "";
                if (message.Contains("未知 GM 指令", StringComparison.Ordinal) ||
                    message.Contains("GM 指令失敗", StringComparison.Ordinal) ||
                    message.Contains("GM 指令目前", StringComparison.Ordinal) ||
                    message.Contains("GM 指令需要", StringComparison.Ordinal) ||
                    message.Contains("GM 指令已關閉", StringComparison.Ordinal) ||
                    message.Contains("testbot 未啟用", StringComparison.Ordinal) ||
                    message.Contains("testbot 僅允許", StringComparison.Ordinal) ||
                    message.Contains("TestBot profile:", StringComparison.Ordinal) ||
                    message.Contains("傳送失敗", StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "Server refused GM command: " + message +
                        "; verify Game__GmCommandsEnabled and Game__TestBotCommandsEnabled.");
            }
            if (kind == responseType && predicate(packet))
                return packet;
        }
    }

    private async Task<JsonElement> ReadInboxAsync(
        CancellationToken token)
    {
        try
        {
            return await _inbox.Reader
                .ReadAsync(token);
        }
        catch (ChannelClosedException)
        {
            throw new InvalidOperationException(
                "WebSocket receive loop stopped",
                _receiveFailure);
        }
    }

    private async Task SendPacketAsync(
        object request,
        TimeSpan timeout)
    {
        var wire =
            Encoding.UTF8.GetBytes(
                ClientProtocol.Encode(request));

        using var deadline =
            CancellationTokenSource
                .CreateLinkedTokenSource(
                    _lifetime.Token);
        deadline.CancelAfter(timeout);

        await _sendGate.WaitAsync(
            deadline.Token);
        try
        {
            await _socket.SendAsync(
                wire.AsMemory(),
                WebSocketMessageType.Text,
                true,
                deadline.Token);

            if (_measure)
            {
                Interlocked.Add(
                    ref _sentBytes,
                    wire.LongLength);
            }
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private async Task ReceiveLoopAsync()
    {
        try
        {
            var buffer =
                new byte[64 * 1024];

            while (!_lifetime.IsCancellationRequested)
            {
                using var message =
                    new MemoryStream();

                WebSocketReceiveResult? last = null;
                do
                {
                    last = await _socket.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        _lifetime.Token);

                    if (last.MessageType ==
                        WebSocketMessageType.Close)
                    {
                        _inbox.Writer.TryComplete();
                        return;
                    }

                    if (last.MessageType !=
                        WebSocketMessageType.Text)
                    {
                        throw new InvalidOperationException(
                            "unexpected non-text WebSocket frame");
                    }

                    message.Write(
                        buffer,
                        0,
                        last.Count);

                    if (message.Length >
                        2_097_152)
                    {
                        throw new InvalidOperationException(
                            "received packet exceeds 2 MiB");
                    }
                }
                while (!last.EndOfMessage);

                if (_measure)
                {
                    Interlocked.Add(
                        ref _receivedBytes,
                        message.Length);
                }

                using var decoded =
                    ClientProtocol.Decode(
                        Encoding.UTF8.GetString(
                            message.ToArray()));

                var packet =
                    decoded.RootElement.Clone();
                var type =
                    packet.TryGetProperty(
                        "Type",
                        out var typeElement)
                        ? typeElement.GetString()
                        : null;

                if (type == "Ping")
                {
                    await SendPacketAsync(
                        new
                        {
                            Type = "Pong"
                        },
                        TimeSpan.FromSeconds(5));
                    continue;
                }

                if (_interactiveBots)
                {
                    BotWorld.Observe(packet, CharacterId);
                    if (type == "PositionCorrection")
                    {
                        if (packet.TryGetProperty("X", out var correctedX) &&
                            correctedX.TryGetInt32(out var valueX))
                            X = valueX;
                        if (packet.TryGetProperty("Y", out var correctedY) &&
                            correctedY.TryGetInt32(out var valueY))
                            Y = valueY;
                    }
                }

                if (_measure &&
                    type == "PositionCorrection")
                {
                    Interlocked.Increment(ref _positionCorrections);
                    continue;
                }

                if (_measure &&
                    type == "MoveEntity")
                {
                    Interlocked.Increment(
                        ref _moveBroadcasts);
                    continue;
                }

                if (_measure &&
                    type == "ChatBroadcast")
                {
                    Interlocked.Increment(
                        ref _chatBroadcasts);
                    continue;
                }

                var combatTarget =
                    Interlocked.Read(
                        ref _combatTargetEntityId);

                if (_measure &&
                    combatTarget > 0 &&
                    type == "EntityDeath" &&
                    packet.TryGetProperty(
                        "EntityID",
                        out var deathEntity) &&
                    deathEntity.GetInt64() ==
                        combatTarget)
                {
                    Interlocked.Increment(
                        ref _combatDeaths);
                    continue;
                }

                if (_measure &&
                    (combatTarget > 0 ||
                     _dropCombatNoise) &&
                    type is
                        "EntityDamage" or
                        "MonsterTarget" or
                        "SyncState")
                {
                    Interlocked.Increment(
                        ref _combatEvents);
                    continue;
                }

                if (_measure &&
                    _dropCombatNoise &&
                    type == "EntityDeath")
                {
                    Interlocked.Increment(
                        ref _combatEvents);
                    continue;
                }

                if (_interactiveBots)
                    continue;

                await _inbox.Writer.WriteAsync(
                    packet,
                    _lifetime.Token);
            }
        }
        catch (OperationCanceledException)
            when (_lifetime.IsCancellationRequested)
        {
            _inbox.Writer.TryComplete();
        }
        catch (Exception ex)
        {
            _receiveFailure = ex;
            _inbox.Writer.TryComplete(ex);
        }
    }

    private static void RequireSuccess(
        JsonElement packet,
        string operation)
    {
        if (!packet.TryGetProperty(
                "Success",
                out var success) ||
            success.ValueKind !=
                JsonValueKind.True)
        {
            throw new InvalidOperationException(
                operation +
                " failed: " +
                packet.GetRawText());
        }
    }

    public async ValueTask DisposeAsync()
    {
        _measure = false;
        _lifetime.Cancel();

        try
        {
            _socket.Abort();
        }
        catch
        {
        }

        if (_receiveTask is not null)
        {
            try
            {
                await _receiveTask;
            }
            catch
            {
            }
        }

        _socket.Dispose();
        _sendGate.Dispose();
        _lifetime.Dispose();
    }
}
