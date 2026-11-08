using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace YMH219.ChatApp;

internal sealed class ChatServer
{
    private const int MaxUsernameLength = 24;
    private const int MaxMessageLength = 2000;
    private readonly ConcurrentDictionary<Guid, ClientSession> _clients = new();
    private readonly object _joinLock = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _shutdown;
    private Task? _acceptTask;

    public bool IsRunning => _listener is not null;
    public event Action<ChatPacket>? MessageReceived;
    public event Action<IReadOnlyList<string>>? ClientsChanged;
    public event Action<string>? StatusChanged;

    public void Start(int port)
    {
        if (IsRunning)
            throw new InvalidOperationException("Sunucu zaten çalışıyor.");
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "Port 1 ile 65535 arasında olmalı.");

        _shutdown = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, port);
        try
        {
            _listener.Start();
            _acceptTask = AcceptClientsAsync(_shutdown.Token);
            StatusChanged?.Invoke($"Sunucu çalışıyor · Port {port}");
        }
        catch
        {
            _listener.Stop();
            _listener = null;
            _shutdown.Dispose();
            _shutdown = null;
            throw;
        }
    }

    public async Task StopAsync()
    {
        var listener = _listener;
        var shutdown = _shutdown;
        if (listener is null || shutdown is null)
            return;

        _listener = null;
        _shutdown = null;
        shutdown.Cancel();
        listener.Stop();
        foreach (var session in _clients.Values)
            session.Close();

        if (_acceptTask is not null)
        {
            try { await _acceptTask; }
            catch (OperationCanceledException) { }
            catch (SocketException) { }
        }

        _clients.Clear();
        ClientsChanged?.Invoke(Array.Empty<string>());
        shutdown.Dispose();
        _acceptTask = null;
        StatusChanged?.Invoke("Sunucu durduruldu.");
    }

    private async Task AcceptClientsAsync(CancellationToken cancellationToken)
    {
        var listener = _listener!;
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) when (cancellationToken.IsCancellationRequested) { break; }

            client.NoDelay = true;
            _ = HandleClientAsync(client, cancellationToken);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var session = new ClientSession(client);
        var registered = false;
        try
        {
            var firstLine = await session.Reader.ReadLineAsync(cancellationToken);
            if (firstLine is null)
                return;

            var join = ChatProtocol.Deserialize(firstLine);
            if (join?.Type != "join")
            {
                await session.SendAsync(ChatProtocol.Create("error", text: "Önce kullanıcı adıyla katılmalısınız."));
                return;
            }

            var username = join.Username.Trim();
            if (username.Length is < 1 or > MaxUsernameLength || username.Any(char.IsControl))
            {
                await session.SendAsync(ChatProtocol.Create("error", text: $"Kullanıcı adı 1-{MaxUsernameLength} karakter olmalı."));
                return;
            }

            lock (_joinLock)
            {
                if (!_clients.Values.Any(existing => string.Equals(existing.Username, username, StringComparison.OrdinalIgnoreCase)))
                {
                    session.Username = username;
                    _clients[session.Id] = session;
                    registered = true;
                }
            }

            if (!registered)
            {
                await session.SendAsync(ChatProtocol.Create("error", text: "Bu kullanıcı adı şu anda kullanımda."));
                return;
            }

            await session.SendAsync(ChatProtocol.Create("joined", username, "Sohbete bağlandın."));
            await BroadcastAsync(ChatProtocol.Create("system", text: $"{username} sohbete katıldı."));
            PublishClients();

            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await session.Reader.ReadLineAsync(cancellationToken);
                if (line is null)
                    break;

                var packet = ChatProtocol.Deserialize(line);
                if (packet?.Type != "chat")
                    continue;

                var text = packet.Text.Trim();
                if (text.Length == 0)
                    continue;
                if (text.Length > MaxMessageLength)
                {
                    await session.SendAsync(ChatProtocol.Create("error", text: $"Mesaj en fazla {MaxMessageLength} karakter olabilir."));
                    continue;
                }

                await BroadcastAsync(ChatProtocol.Create("chat", session.Username, text));
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }
        catch (System.Text.Json.JsonException)
        {
            if (registered)
                MessageReceived?.Invoke(ChatProtocol.Create("system", text: $"{session.Username} geçersiz veri gönderdi; bağlantı kapatıldı."));
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"İstemci bağlantı hatası: {ex.Message}");
        }
        finally
        {
            if (registered && _clients.TryRemove(session.Id, out _))
            {
                PublishClients();
                await BroadcastAsync(ChatProtocol.Create("system", text: $"{session.Username} sohbetten ayrıldı."));
            }
            session.Close();
        }
    }

    private async Task BroadcastAsync(ChatPacket packet)
    {
        MessageReceived?.Invoke(packet);
        foreach (var session in _clients.Values)
        {
            try { await session.SendAsync(packet); }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException) { }
        }
    }

    private void PublishClients() =>
        ClientsChanged?.Invoke(_clients.Values.Select(client => client.Username).OrderBy(name => name).ToArray());

    private sealed class ClientSession(TcpClient client)
    {
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly NetworkStream _stream = client.GetStream();
        public Guid Id { get; } = Guid.NewGuid();
        public string Username { get; set; } = string.Empty;
        public StreamReader Reader { get; } = new(client.GetStream(), Encoding.UTF8, leaveOpen: true);
        private StreamWriter Writer { get; } = new(client.GetStream(), new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

        public async Task SendAsync(ChatPacket packet)
        {
            await _writeLock.WaitAsync();
            try { await Writer.WriteLineAsync(ChatProtocol.Serialize(packet)); }
            finally { _writeLock.Release(); }
        }

        public void Close()
        {
            try { _stream.Close(); }
            catch (IOException) { }
            client.Close();
        }
    }
}
