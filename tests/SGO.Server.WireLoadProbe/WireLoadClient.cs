using System.Net.WebSockets;
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
    private long _moveBroadcasts;
    private long _chatBroadcasts;
    private long _sentBytes;
    private long _receivedBytes;

    private WireLoadClient(int index)
    {
        Index = index;
    }

    public int Index { get; }
    public long CharacterId { get; private set; }
    public int MapId { get; private set; }
    public int X { get; private set; }
    public int Y { get; private set; }

    public long MoveBroadcasts =>
        Interlocked.Read(ref _moveBroadcasts);

    public long ChatBroadcasts =>
        Interlocked.Read(ref _chatBroadcasts);

    public long SentBytes =>
        Interlocked.Read(ref _sentBytes);

    public long ReceivedBytes =>
        Interlocked.Read(ref _receivedBytes);

    public static async Task<WireLoadClient> ConnectAndEnterAsync(
        string url,
        string runId,
        int index,
        TimeSpan timeout)
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

            client._receiveTask =
                client.ReceiveLoopAsync();

            await client.SetupAsync(
                runId,
                timeout);

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

    public void StartMeasurement()
    {
        Interlocked.Exchange(
            ref _moveBroadcasts,
            0);
        Interlocked.Exchange(
            ref _chatBroadcasts,
            0);
        Interlocked.Exchange(
            ref _sentBytes,
            0);
        Interlocked.Exchange(
            ref _receivedBytes,
            0);
        _measure = true;
    }

    public void StopMeasurement()
        => _measure = false;

    public Task SendPositionAsync(
        TimeSpan timeout)
        => SendPacketAsync(
            new
            {
                Type = "SyncPosition",
                X,
                Y,
                Dir = "1"
            },
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

    private async Task SetupAsync(
        string runId,
        TimeSpan timeout)
    {
        var loginName =
            $"wire-load-{runId}-{Index:D3}";
        var characterName =
            $"WL{Index:D3}{runId[..5]}";
        const string password =
            "WireLoad-123";

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
            var packet =
                await ReadInboxAsync(
                    deadline.Token);

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

        using var deadline =
            new CancellationTokenSource(timeout);

        while (true)
        {
            var packet =
                await ReadInboxAsync(
                    deadline.Token);

            if (packet.TryGetProperty(
                    "Type",
                    out var typeElement) &&
                typeElement.GetString() ==
                    responseType)
            {
                return packet;
            }
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
