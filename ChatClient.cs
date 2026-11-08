using System.Net.Sockets;
using System.Text;

namespace YMH219.ChatApp;

internal sealed class ChatClient
{
    private TcpClient? _client;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private CancellationTokenSource? _receiveCancellation;
    private Task? _receiveTask;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public bool IsConnected { get; private set; }
    public event Action<ChatPacket>? MessageReceived;
    public event Action<string>? Disconnected;

    public async Task ConnectAsync(string host, int port, string username)
    {
        if (IsConnected)
            throw new InvalidOperationException("Zaten bir sunucuya bağlısın.");

        var client = new TcpClient { NoDelay = true };
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await client.ConnectAsync(host, port, timeout.Token);
            var stream = client.GetStream();
            var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync(ChatProtocol.Serialize(ChatProtocol.Create("join", username)));

            var responseLine = await reader.ReadLineAsync(timeout.Token);
            var response = responseLine is null ? null : ChatProtocol.Deserialize(responseLine);
            if (response is null)
                throw new IOException("Sunucu bağlantıyı kapattı.");
            if (response.Type == "error")
                throw new InvalidOperationException(response.Text);
            if (response.Type != "joined")
                throw new IOException("Sunucudan beklenmeyen yanıt alındı.");

            _client = client;
            _reader = reader;
            _writer = writer;
            _receiveCancellation = new CancellationTokenSource();
            IsConnected = true;
            MessageReceived?.Invoke(response);
            _receiveTask = ReceiveLoopAsync(_receiveCancellation.Token);
        }
        catch
        {
            client.Close();
            throw;
        }
    }

    public async Task SendAsync(string text)
    {
        if (!IsConnected || _writer is null)
            throw new InvalidOperationException("Önce bir sunucuya bağlanmalısın.");

        var packet = ChatProtocol.Create("chat", text: text.Trim());
        await _writeLock.WaitAsync();
        try { await _writer.WriteLineAsync(ChatProtocol.Serialize(packet)); }
        finally { _writeLock.Release(); }
    }

    public async Task DisconnectAsync()
    {
        if (!IsConnected && _client is null)
            return;

        IsConnected = false;
        _receiveCancellation?.Cancel();
        _client?.Close();
        if (_receiveTask is not null)
        {
            try { await _receiveTask; }
            catch (OperationCanceledException) { }
        }

        _reader?.Dispose();
        try { _writer?.Dispose(); }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        _receiveCancellation?.Dispose();
        _reader = null;
        _writer = null;
        _receiveCancellation = null;
        _receiveTask = null;
        _client = null;
        Disconnected?.Invoke("Bağlantı kesildi.");
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var reason = "Sunucu bağlantıyı kapattı.";
        try
        {
            while (!cancellationToken.IsCancellationRequested && _reader is not null)
            {
                var line = await _reader.ReadLineAsync(cancellationToken);
                if (line is null)
                    break;
                var packet = ChatProtocol.Deserialize(line);
                if (packet is not null)
                {
                    if (packet.Type == "error")
                        MessageReceived?.Invoke(ChatProtocol.Create("system", text: packet.Text));
                    else
                        MessageReceived?.Invoke(packet);
                }
            }
        }
        catch (OperationCanceledException) { reason = "Bağlantı kesildi."; }
        catch (IOException) { }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }
        catch (System.Text.Json.JsonException) { reason = "Sunucudan okunamayan veri geldi."; }
        finally
        {
            var wasConnected = IsConnected;
            IsConnected = false;
            if (wasConnected)
            {
                _client?.Close();
                _reader?.Dispose();
                try { _writer?.Dispose(); }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
                _receiveCancellation?.Dispose();
                _client = null;
                _reader = null;
                _writer = null;
                _receiveCancellation = null;
                _receiveTask = null;
                Disconnected?.Invoke(reason);
            }
        }
    }
}
