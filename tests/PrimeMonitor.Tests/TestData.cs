using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PrimeMonitor.Monitor;
using PrimeMonitor.Shared;

namespace PrimeMonitor.Tests;

internal static class TestData
{
    public static readonly Band Band20s = Rules.BandFor(20, 20);
    public static readonly DateTimeOffset T0 = new(2026, 10, 16, 10, 0, 0, TimeSpan.Zero);

    public static PrimeMessage Msg(long seq, long durationMs = 20000, string machineId = "1", ulong prime = 1000003) =>
        new(machineId, seq, prime, durationMs, T0.UtcDateTime);

    public static readonly TimeSpan Threshold20s = TimeSpan.FromSeconds(25); // band upper 24s + 1s grace

    public static Alerts Alerts() => new(NullLogger<Alerts>.Instance);

    public static async Task WaitUntil(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"timed out waiting for: {what}");
            await Task.Delay(10);
        }
    }

    public static ConnectionHandler Handler(MachineRegistry machines, Channel<PrimeMessage> queue) =>
        new(machines, Alerts(), queue, Options.Create(new MonitorOptions { ExpectedSeconds = 20, DeviationPercent = 20 }),
            TimeProvider.System, NullLogger<ConnectionHandler>.Instance);
}

internal static class Race
{
    // Runs body on `threads` dedicated threads, `iterations` times each. A barrier releases all threads
    // together every iteration so their calls collide. Returns how many calls started while another
    // thread was still inside its call: proof the overlap actually happened, not just could have.
    public static int Run(int threads, int iterations, Action<int, int> body)
    {
        using var barrier = new Barrier(threads);
        var inFlight = 0;
        var overlaps = 0;
        var failures = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        var workers = Enumerable.Range(0, threads).Select(t => new Thread(() =>
        {
            for (var i = 0; i < iterations; i++)
            {
                barrier.SignalAndWait();
                if (Interlocked.Increment(ref inFlight) > 1) Interlocked.Increment(ref overlaps);
                // Caught rather than left to escape: an unhandled exception on a raw thread kills the test
                // host, and the loop must keep reaching the barrier or the other threads deadlock.
                try { body(i, t); }
                catch (Exception ex) { failures.Enqueue(ex); }
                Interlocked.Decrement(ref inFlight);
            }
        })).ToList();
        workers.ForEach(w => w.Start());
        workers.ForEach(w => w.Join());
        if (!failures.IsEmpty)
            throw new AggregateException($"{failures.Count} calls threw during the race", failures.Take(3));
        return overlaps;
    }
}
