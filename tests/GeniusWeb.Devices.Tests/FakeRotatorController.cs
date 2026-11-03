using System.Net;
using System.Net.Sockets;
using System.Text;
using static GeniusWeb.Devices.Tests.RotatorStatusFrames;

namespace GeniusWeb.Devices.Tests;

/// <summary>
/// A Rotator Genius as far as the library can tell: it answers "|v" with a firmware
/// version, "|h" with a two-rotator status and anything else with an acknowledgement,
/// and its connections can be cut while it keeps listening.
/// </summary>
internal sealed class FakeRotatorController : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly List<TcpClient> _clients = [];
    private int _azimuth;
    private int _accepted;

    public FakeRotatorController(int azimuth)
    {
        _azimuth = azimuth;
        _listener.Start();
        _ = Task.Run(AcceptAsync);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>How many connections have been made to this controller so far.</summary>
    public int Accepted => Volatile.Read(ref _accepted);

    /// <summary>The heading the first rotator reports from now on.</summary>
    public int Azimuth
    {
        set => Volatile.Write(ref _azimuth, value);
    }

    /// <summary>Every open connection is closed; the controller goes on listening.</summary>
    public void DropConnections()
    {
        lock (_clients)
        {
            foreach (var client in _clients) client.Dispose();
            _clients.Clear();
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        DropConnections();
        _stop.Dispose();
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                Interlocked.Increment(ref _accepted);
                lock (_clients) _clients.Add(client);
                _ = Task.Run(() => ServeAsync(client));
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
        {
            // The test is over, or the listener was stopped under it: nothing left to accept.
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        try
        {
            var stream = client.GetStream();
            var buffer = new byte[256];
            while (true)
            {
                var read = await stream.ReadAsync(buffer, _stop.Token);
                if (read == 0) return;
                var request = Encoding.ASCII.GetString(buffer, 0, read);
                await stream.WriteAsync(Encoding.ASCII.GetBytes(ReplyTo(request)), _stop.Token);
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
            // The library went away, or the link was cut under us: the conversation is over.
            // Anything else is a bug in this fake; it is left unhandled so it is not mistaken for a closed link.
        }
    }

    private string ReplyTo(string request)
    {
        if (request.StartsWith("|v")) return "v151";
        if (request.StartsWith("|h"))
        {
            var first = Volatile.Read(ref _azimuth).ToString().PadLeft(3);
            return Status(2, Block(azimuth: first, name: "RG1         "), Block(azimuth: " 50", name: "RG2         "));
        }
        return "|AK";
    }
}
