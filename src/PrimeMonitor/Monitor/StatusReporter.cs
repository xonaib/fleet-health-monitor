using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PrimeMonitor.Monitor;

public sealed class StatusReporter(MachineRegistry machines, TimeProvider time, ILogger<StatusReporter> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var row in BuildRows())
            {
                logger.LogInformation("STATUS machine={MachineId} state={State} lastSeen={LastSeen} msgs={Messages} alerts={Alerts}",
                    row.MachineId, row.State, row.LastSeen, row.Messages, row.Alerts);
            }
        }
    }

    // One log line per machine rather than one multi-line table: the single-line console format
    // would flatten a table, and every line then carries its machine ID.
    public IReadOnlyList<StatusRow> BuildRows()
    {
        var now = time.GetUtcNow();
        return machines.All
            .Select(m => m.Snapshot())
            .OrderBy(s => s.MachineId, StringComparer.Ordinal)
            .Select(s => new StatusRow(
                s.MachineId,
                s.State.PadRight(12),
                (s.LastArrival is { } at ? $"{(now - at).TotalSeconds:F1}s" : "never").PadRight(7),
                s.Messages,
                s.AlertCounts.Count == 0
                    ? "-"
                    : string.Join(",", s.AlertCounts.OrderBy(a => a.Key).Select(a => $"{Alerts.WireName(a.Key)}={a.Value}"))))
            .ToList();
    }
}

public sealed record StatusRow(string MachineId, string State, string LastSeen, long Messages, string Alerts);
