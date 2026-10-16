using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrimeMonitor.Shared;

namespace PrimeMonitor.Worker;

public sealed class WorkerRunner(
    IOptions<MonitorOptions> options,
    WorkerSettings settings,
    ILogger<WorkerRunner> logger) : BackgroundService
{
    private static readonly TimeSpan InitialBackoff = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        var machineId = settings.MachineId;
        var backoff = InitialBackoff;
        long lastSentSeq = 0;

        // A message whose send failed is kept and resent after reconnecting, so a dropped connection
        // does not show up at the monitor as lost work (seq gap). The cost is a possible duplicate if
        // the failed write had in fact reached the monitor; a duplicate is the safer error.
        PrimeMessage? pending = null;

        while (true)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(opts.Host, opts.Port, stoppingToken);
                logger.LogInformation("Machine {MachineId} connected to {Host}:{Port}", machineId, opts.Host, opts.Port);
                backoff = InitialBackoff;

                await using var writer = new StreamWriter(client.GetStream(), new UTF8Encoding(false)) { NewLine = "\n" };
                while (true)
                {
                    pending ??= await ProduceAsync(machineId, lastSentSeq + 1, opts.ExpectedSeconds, stoppingToken);
                    await writer.WriteLineAsync(pending.ToJsonLine().AsMemory(), stoppingToken);
                    await writer.FlushAsync(stoppingToken);
                    logger.LogInformation("Machine {MachineId} sent seq={Seq} prime={Prime} durationMs={DurationMs}",
                        machineId, pending.Seq, pending.Prime, pending.DurationMs);
                    lastSentSeq = pending.Seq;
                    pending = null;
                }
            }
            catch (Exception ex) when (ex is SocketException or IOException)
            {
                logger.LogWarning("Machine {MachineId} connection to {Host}:{Port} failed ({Error}); retrying in {BackoffMs}ms",
                    machineId, opts.Host, opts.Port, ex.Message, (int)backoff.TotalMilliseconds);
                await Task.Delay(backoff, stoppingToken);
                backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
            }
        }
    }

    private static async Task<PrimeMessage> ProduceAsync(string machineId, long seq, double expectedSeconds, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var start = (ulong)Random.Shared.NextInt64(1L << 31, 1L << 32);
        var prime = Primality.NextPrime(start);

        // Finding a 32-bit prime takes microseconds, so pad up to the expected generation time (±10% jitter)
        // to simulate a fixed workload.
        var target = TimeSpan.FromSeconds(expectedSeconds * (0.9 + Random.Shared.NextDouble() * 0.2));
        var remaining = target - stopwatch.Elapsed;
        if (remaining > TimeSpan.Zero)
            await Task.Delay(remaining, ct);

        return new PrimeMessage(machineId, seq, prime, stopwatch.ElapsedMilliseconds, DateTime.UtcNow);
    }
}
