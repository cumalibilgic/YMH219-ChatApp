using System.Net.Sockets;

namespace YMH219.ChatApp;

public sealed class ChatForm : Form
{
    private readonly ChatServer _server = new();
    private readonly ChatClient _client = new();
    private readonly NumericUpDown _serverPort = NewPortInput();
    private readonly NumericUpDown _clientPort = NewPortInput();
    private readonly TextBox _username = new() { Text = "Cumali", MaxLength = 24, Width = 112 };
    private readonly TextBox _host = new() { Text = "127.0.0.1", Width = 132 };
    private readonly Button _startServer = new() { Text = "Sunucuyu Başlat", Width = 128 };
    private readonly Button _stopServer = new() { Text = "Sunucuyu Durdur", Width = 128, Enabled = false };
    private readonly Button _connect = new() { Text = "Bağlan", Width = 88 };
    private readonly Button _disconnect = new() { Text = "Bağlantıyı Kes", Width = 112, Enabled = false };
    private readonly Label _serverStatus = new() { Text = "Sunucu kapalı", AutoSize = true };
    private readonly Label _status = new() { Text = "Hazır. Önce bir sunucu başlatın veya çalışan bir sunucuya bağlanın.", AutoSize = true };
    private readonly RichTextBox _messages = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        BackColor = Color.FromArgb(249, 251, 253),
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Segoe UI", 10f),
        DetectUrls = false
    };
    private readonly TextBox _messageInput = new() { Dock = DockStyle.Fill, MaxLength = 2000, PlaceholderText = "Mesajını yaz..." };
    private readonly Button _send = new() { Text = "Gönder", Dock = DockStyle.Fill, Enabled = false };
    private readonly ListBox _onlineClients = new() { Dock = DockStyle.Fill };
    private bool _closing;

    public ChatForm()
    {
        Text = "YMH219 Chat Uygulaması";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1000, 700);
        MinimumSize = new Size(820, 580);
        Font = new Font("Segoe UI", 9f);
        BackColor = Color.FromArgb(239, 244, 248);
        BuildLayout();
        WireEvents();
        UpdateControls();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 1,
            RowCount = 3,
            BackColor = BackColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        var connectionPanels = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        connectionPanels.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        connectionPanels.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        connectionPanels.Controls.Add(BuildServerPanel(), 0, 0);
        connectionPanels.Controls.Add(BuildClientPanel(), 1, 0);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel2,
            BackColor = BackColor
        };
        split.Panel1.Controls.Add(BuildChatPanel());
        split.Panel2.Controls.Add(BuildClientListPanel());

        var statusPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4, 6, 0, 0) };
        _status.ForeColor = Color.FromArgb(53, 72, 89);
        statusPanel.Controls.Add(_status);

        root.Controls.Add(connectionPanels, 0, 0);
        root.Controls.Add(split, 0, 1);
        root.Controls.Add(statusPanel, 0, 2);
        Controls.Add(root);
        Shown += (_, _) =>
        {
            var maxDistance = split.ClientSize.Width - split.Panel2MinSize;
            if (maxDistance >= split.Panel1MinSize)
                split.SplitterDistance = Math.Clamp(split.ClientSize.Width - 230, split.Panel1MinSize, maxDistance);
        };
    }

    private Control BuildServerPanel()
    {
        var group = new GroupBox { Text = "Sunucu", Dock = DockStyle.Fill, Padding = new Padding(10) };
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true };
        layout.Controls.Add(new Label { Text = "Port", AutoSize = true, Margin = new Padding(4, 9, 2, 0) });
        layout.Controls.Add(_serverPort);
        layout.Controls.Add(_startServer);
        layout.Controls.Add(_stopServer);
        layout.Controls.Add(_serverStatus);
        _serverStatus.Margin = new Padding(8, 9, 2, 0);
        group.Controls.Add(layout);
        return group;
    }

    private Control BuildClientPanel()
    {
        var group = new GroupBox { Text = "İstemci bağlantısı", Dock = DockStyle.Fill, Padding = new Padding(10) };
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true };
        layout.Controls.Add(new Label { Text = "Kullanıcı adı", AutoSize = true, Margin = new Padding(4, 9, 2, 0) });
        layout.Controls.Add(_username);
        layout.Controls.Add(new Label { Text = "Sunucu", AutoSize = true, Margin = new Padding(6, 9, 2, 0) });
        layout.Controls.Add(_host);
        layout.Controls.Add(new Label { Text = "Port", AutoSize = true, Margin = new Padding(6, 9, 2, 0) });
        layout.Controls.Add(_clientPort);
        layout.Controls.Add(_connect);
        layout.Controls.Add(_disconnect);
        group.Controls.Add(layout);
        return group;
    }

    private Control BuildChatPanel()
    {
        var group = new GroupBox { Text = "Sohbet", Dock = DockStyle.Fill, Padding = new Padding(10) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(2) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.Controls.Add(_messages, 0, 0);
        layout.SetColumnSpan(_messages, 2);
        layout.Controls.Add(_messageInput, 0, 1);
        layout.Controls.Add(_send, 1, 1);
        group.Controls.Add(layout);
        return group;
    }

    private Control BuildClientListPanel()
    {
        var group = new GroupBox { Text = "Bağlı kullanıcılar", Dock = DockStyle.Fill, Padding = new Padding(10) };
        group.Controls.Add(_onlineClients);
        return group;
    }

    private void WireEvents()
    {
        _startServer.Click += async (_, _) => await StartServerAsync();
        _stopServer.Click += async (_, _) => await StopServerAsync();
        _connect.Click += async (_, _) => await ConnectAsync();
        _disconnect.Click += async (_, _) => await DisconnectAsync();
        _send.Click += async (_, _) => await SendAsync();
        _messageInput.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.SuppressKeyPress = true;
                await SendAsync();
            }
        };

        _server.MessageReceived += packet => OnUi(() => AppendPacket(packet));
        _server.ClientsChanged += names => OnUi(() =>
        {
            _onlineClients.BeginUpdate();
            _onlineClients.Items.Clear();
            _onlineClients.Items.AddRange(names.Cast<object>().ToArray());
            _onlineClients.EndUpdate();
        });
        _server.StatusChanged += message => OnUi(() =>
        {
            _serverStatus.Text = message;
            _serverStatus.ForeColor = _server.IsRunning ? Color.FromArgb(31, 132, 93) : Color.FromArgb(95, 108, 119);
            _status.Text = message;
            UpdateControls();
        });
        _client.MessageReceived += packet => OnUi(() =>
        {
            // When this instance hosts the server, its server event already
            // displays the broadcast and avoids showing the same packet twice.
            if (!_server.IsRunning)
                AppendPacket(packet);
        });
        _client.Disconnected += message => OnUi(() =>
        {
            _status.Text = message;
            UpdateControls();
        });
        FormClosing += HandleFormClosing;
    }

    private async Task StartServerAsync()
    {
        try { _server.Start((int)_serverPort.Value); }
        catch (SocketException ex) { ShowError($"Port açılamadı. Başka bir uygulama kullanıyor olabilir.\n\n{ex.Message}"); }
        catch (Exception ex) { ShowError(ex.Message); }
        await Task.CompletedTask;
    }

    private async Task StopServerAsync()
    {
        try { await _server.StopAsync(); }
        catch (Exception ex) { ShowError($"Sunucu durdurulurken hata oluştu: {ex.Message}"); }
    }

    private async Task ConnectAsync()
    {
        var username = _username.Text.Trim();
        var host = _host.Text.Trim();
        if (username.Length == 0 || host.Length == 0)
        {
            ShowError("Kullanıcı adı ve sunucu adresi boş bırakılamaz.");
            return;
        }

        _status.Text = "Sunucuya bağlanılıyor...";
        UpdateControls(connecting: true);
        try
        {
            await _client.ConnectAsync(host, (int)_clientPort.Value, username);
            _status.Text = $"{host}:{_clientPort.Value} adresine bağlandı.";
        }
        catch (Exception ex) when (ex is SocketException or IOException or InvalidOperationException or ArgumentException or OperationCanceledException)
        {
            _status.Text = "Bağlantı kurulamadı.";
            ShowError($"Sunucuya bağlanılamadı. Adresi, portu ve sunucunun açık olduğunu kontrol et.\n\n{ex.Message}");
        }
        finally { UpdateControls(); }
    }

    private async Task DisconnectAsync()
    {
        await _client.DisconnectAsync();
        UpdateControls();
    }

    private async Task SendAsync()
    {
        var text = _messageInput.Text.Trim();
        if (text.Length == 0)
            return;
        try
        {
            await _client.SendAsync(text);
            _messageInput.Clear();
            _messageInput.Focus();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or SocketException)
        {
            _status.Text = "Mesaj gönderilemedi.";
            ShowError(ex.Message);
            UpdateControls();
        }
    }

    private void AppendPacket(ChatPacket packet)
    {
        var time = packet.Timestamp?.ToLocalTime().ToString("HH:mm:ss") ?? DateTime.Now.ToString("HH:mm:ss");
        var line = packet.Type == "chat"
            ? $"[{time}] {packet.Username}: {packet.Text}"
            : $"[{time}] • {packet.Text}";
        _messages.AppendText(line + Environment.NewLine);
        _messages.SelectionStart = _messages.TextLength;
        _messages.ScrollToCaret();
    }

    private void OnUi(Action action)
    {
        if (IsDisposed || !IsHandleCreated)
            return;
        if (InvokeRequired)
        {
            try { BeginInvoke(action); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
            return;
        }
        action();
    }

    private void UpdateControls(bool connecting = false)
    {
        _startServer.Enabled = !_server.IsRunning;
        _stopServer.Enabled = _server.IsRunning;
        _serverPort.Enabled = !_server.IsRunning;
        _connect.Enabled = !_client.IsConnected && !connecting;
        _disconnect.Enabled = _client.IsConnected;
        _send.Enabled = _client.IsConnected;
        _username.Enabled = !_client.IsConnected && !connecting;
        _host.Enabled = !_client.IsConnected && !connecting;
        _clientPort.Enabled = !_client.IsConnected && !connecting;
    }

    private async void HandleFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_closing)
            return;
        e.Cancel = true;
        _closing = true;
        Enabled = false;
        try
        {
            await _client.DisconnectAsync();
            await _server.StopAsync();
        }
        finally { Close(); }
    }

    private void ShowError(string message) => MessageBox.Show(this, message, "Chat Uygulaması", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private static NumericUpDown NewPortInput() => new()
    {
        Minimum = 1,
        Maximum = 65535,
        Value = 5050,
        Width = 72,
        Margin = new Padding(3, 3, 8, 3)
    };
}
