using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using PrimeMonitor.Monitor;
using PrimeMonitor.Shared;
using Xunit.Abstractions;
using static PrimeMonitor.Tests.TestData;

namespace PrimeMonitor.Tests;

public class ConnectionHandlerTests(ITestOutputHelper output)
{
    private static Channel<PrimeMessage> Queue(int capacity = 10_000) =>
        Channel.CreateBounded<PrimeMessage>(new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.Wait });

    [Fact]
    public void ProcessLine_ValidMessage_QueuesItForVerification()
    {
        var queue = Queue();
        var handler = Handler(new MachineRegistry(), queue);

        handler.ProcessLine(new ConnectionSession("test"), Msg(1).ToJsonLine());

        Assert.True(queue.Reader.TryRead(out var queued));
        Assert.Equal(1, queued.Seq);
    }

    [Fact]
    public void ProcessLine_VerifyQueueFull_RaisesUnverified()
    {
        var machines = new MachineRegistry();
        var handler = Handler(machines, Queue(capacity: 1));
        var session = new ConnectionSession("test");

        handler.ProcessLine(session, Msg(1).ToJsonLine());
        handler.ProcessLine(session, Msg(2).ToJsonLine());

        Assert.Equal(1, machines.GetOrAdd("1").Snapshot().Count(AlertType.Unverified));
    }

    [Fact]
    public void ProcessLine_MalformedLineFromKnownMachine_RaisesMalformed()
    {
        var machines = new MachineRegistry();
        var handler = Handler(machines, Queue());
        var session = new ConnectionSession("test");
        handler.ProcessLine(session, Msg(1).ToJsonLine());

        handler.ProcessLine(session, "{not json");

        Assert.Equal(1, machines.GetOrAdd("1").Snapshot().Count(AlertType.Malformed));
    }

    private static async Task<(TcpClient worker, TcpClient server)> ConnectedPair()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var worker = new TcpClient();
            await worker.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
            return (worker, await listener.AcceptTcpClientAsync());
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task HandleAsync_WorkerClosesSocket_RaisesDisconnected()
    {
        var machines = new MachineRegistry();
        var handler = Handler(machines, Queue());
        var (worker, server) = await ConnectedPair();
        var handling = handler.HandleAsync(server, CancellationToken.None);

        await worker.GetStream().WriteAsync(Encoding.UTF8.GetBytes(Msg(1).ToJsonLine() + "\n"));
        worker.Close();
        await handling.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, machines.GetOrAdd("1").Snapshot().Count(AlertType.Disconnected));
    }

    [Fact]
    public async Task HandleAsync_MonitorShuttingDown_DoesNotRaiseDisconnected()
    {
        var machines = new MachineRegistry();
        var handler = Handler(machines, Queue());
        var (worker, server) = await ConnectedPair();
        using var shutdown = new CancellationTokenSource();
        var handling = handler.HandleAsync(server, shutdown.Token);
        await worker.GetStream().WriteAsync(Encoding.UTF8.GetBytes(Msg(1).ToJsonLine() + "\n"));
        await WaitUntil(() => machines.All.Any(), "first message processed");

        shutdown.Cancel();
        await handling.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, machines.GetOrAdd("1").Snapshot().Count(AlertType.Disconnected));
        worker.Dispose();
    }

    [Fact]
    public void ProcessLine_SameSeqFromTwoConnectionsAtOnce_RaisesExactlyOneDuplicate()
    {
        // Two connections claiming the same machine ID send the same seq at the same instant. The seq check
        // is check-then-act: without the per-machine lock both can read the old lastSeq and both pass.
        // A new machine ID per iteration also races the registry's GetOrAdd.
        const int iterations = 2000;
        var machines = new MachineRegistry();
        var handler = Handler(machines, Queue(capacity: 2 * iterations));
        var lines = Enumerable.Range(0, iterations).Select(i => Msg(5, machineId: $"m{i}").ToJsonLine()).ToArray();

        var overlaps = Race.Run(2, iterations, (i, t) => handler.ProcessLine(new ConnectionSession($"conn{t}"), lines[i]));

        output.WriteLine($"RACE: {overlaps} of {iterations} iterations had both connections inside ProcessLine at once");
        Assert.True(overlaps > 0, "race window was never hit; test proves nothing");
        var wrong = Enumerable.Range(0, iterations)
            .Select(i => machines.GetOrAdd($"m{i}").Snapshot())
            .Where(s => s.Messages != 2 || s.Count(AlertType.SeqDuplicate) != 1)
            .Select(s => $"{s.MachineId}: messages={s.Messages} duplicates={s.Count(AlertType.SeqDuplicate)}")
            .ToList();
        Assert.True(wrong.Count == 0, $"{wrong.Count} machines wrong, e.g. {string.Join("; ", wrong.Take(3))}");
    }
}
