using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Chess.Net;

/// <summary>
/// The real <see cref="ISessionTransport"/>: a TCP listener on an OS-assigned port for game sessions,
/// plus the dialer for outbound invites. The port is what the LAN.Lib discovery beacon advertises as
/// our service port, so a peer that sees us knows exactly where to dial.
///
/// <para>The UDP half this class used to own moved out to <c>LAN.Lib.UdpLanTransport</c> when
/// discovery was extracted — including the two socket-setup details that mattered (<c>ReuseAddress</c>
/// so several instances on one host can share the discovery port, <c>EnableBroadcast</c> for
/// 255.255.255.255).</para>
/// </summary>
public sealed class TcpSessionTransport : ISessionTransport
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();

    public int ListenPort { get; }

    public event Action<ILanConnection>? ConnectionAccepted;

    public TcpSessionTransport()
    {
        _listener = new TcpListener(IPAddress.Any, 0);
        _listener.Start();
        ListenPort = ((IPEndPoint)_listener.LocalEndpoint).Port;

        _ = AcceptLoopAsync(_cts.Token);
    }

    public async Task<ILanConnection> ConnectAsync(IPEndPoint endPoint, CancellationToken ct = default)
    {
        var client = new TcpClient();
        await client.ConnectAsync(endPoint.Address, endPoint.Port, ct);
        return new TcpLanConnection(client);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(ct);
                ConnectionAccepted?.Invoke(new TcpLanConnection(client));
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { /* transient — keep accepting */ }
        }
    }

    public ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
        _cts.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>A real TCP-backed <see cref="ILanConnection"/> — one background reader raising a line at
/// a time, and a locked writer with newline framing.</summary>
internal sealed class TcpLanConnection : ILanConnection
{
    private readonly TcpClient _client;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _writeLock = new();
    private int _closed;
    private int _reading;

    public IPEndPoint RemoteEndPoint { get; }
    public bool IsConnected => _client.Connected && _closed == 0;

    public event Action<string>? LineReceived;
    public event Action? Closed;

    public TcpLanConnection(TcpClient client)
    {
        _client = client;
        _client.NoDelay = true; // moves are tiny and latency-sensitive — don't Nagle-buffer them
        RemoteEndPoint = (IPEndPoint)client.Client.RemoteEndPoint!;
        var stream = client.GetStream();
        _reader = new StreamReader(stream, Encoding.UTF8);
        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        // NOT started here — see ILanConnection.StartReceiving.
    }

    public void StartReceiving()
    {
        if (Interlocked.Exchange(ref _reading, 1) == 0)
            _ = ReadLoopAsync(_cts.Token);
    }

    public void Send(string line)
    {
        try
        {
            lock (_writeLock)
            {
                _writer.WriteLine(line);
            }
        }
        catch
        {
            RaiseClosed();
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await _reader.ReadLineAsync(ct);
                if (line is null) break; // peer closed the stream
                LineReceived?.Invoke(line);
            }
        }
        catch { /* socket error == peer gone */ }
        finally { RaiseClosed(); }
    }

    private void RaiseClosed()
    {
        if (Interlocked.Exchange(ref _closed, 1) == 0)
            Closed?.Invoke();
    }

    public void Dispose()
    {
        _cts.Cancel();
        RaiseClosed();
        try { _client.Dispose(); } catch { }
        _cts.Dispose();
    }
}
