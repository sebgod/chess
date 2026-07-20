using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Chess.Net;

/// <summary>
/// The real <see cref="ILanTransport"/>: a UDP socket bound to <see cref="LanProtocol.DiscoveryPort"/>
/// for broadcast discovery, and a TCP listener on an OS-assigned port for game sessions. Two socket
/// setup details matter (learned from tianwen's AlpacaDeviceSource): <c>ReuseAddress</c> so several
/// instances on one host (and our own send+receive) can share the discovery port, and
/// <c>EnableBroadcast</c> so sending to 255.255.255.255 is permitted.
/// </summary>
public sealed class UdpTcpLanTransport : ILanTransport
{
    private readonly UdpClient _udp;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();

    public int ListenPort { get; }

    public event Action<DiscoveryDatagram>? DatagramReceived;
    public event Action<ILanConnection>? ConnectionAccepted;

    public UdpTcpLanTransport()
    {
        _udp = new UdpClient();
        _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _udp.Client.Bind(new IPEndPoint(IPAddress.Any, LanProtocol.DiscoveryPort));
        _udp.EnableBroadcast = true;

        _listener = new TcpListener(IPAddress.Any, 0);
        _listener.Start();
        ListenPort = ((IPEndPoint)_listener.LocalEndpoint).Port;

        _ = ReceiveLoopAsync(_cts.Token);
        _ = AcceptLoopAsync(_cts.Token);
    }

    public void Broadcast(string text)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            _udp.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, LanProtocol.DiscoveryPort));
        }
        catch
        {
            // No network / broadcast unavailable — discovery simply shows no peers, never crashes.
        }
    }

    public async Task<ILanConnection> ConnectAsync(IPEndPoint endPoint, CancellationToken ct = default)
    {
        var client = new TcpClient();
        await client.ConnectAsync(endPoint.Address, endPoint.Port, ct);
        return new TcpLanConnection(client);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _udp.ReceiveAsync(ct);
                var text = Encoding.UTF8.GetString(result.Buffer);
                DatagramReceived?.Invoke(new DiscoveryDatagram(text, result.RemoteEndPoint.Address));
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { /* transient — keep listening */ }
        }
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
        try { _udp.Dispose(); } catch { }
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
