using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PrimeMonitor.Shared;

namespace PrimeMonitor.Monitor;

// Runs independently of the read loops: a machine that holds its connection open but sends nothing
// produces no reads at all, so only a timer can notice it.
public sealed class SilenceWatchdog(
    MachineRegistry machines,
    Alerts alerts,
    IOptions<MonitorOptions> options,
    TimeProvider time) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);

    public TimeSpan Threshold { get; } =
        TimeSpan.FromMilliseconds(Rules.BandFor(options.Value.ExpectedSeconds, options.Value.DeviationPercent).UpperMs)
        + TimeSpan.FromSeconds(options.Value.SilenceGraceSeconds);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Tick, time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            CheckAll();
        }
    }

    public void CheckAll()
    {
        var now = time.GetUtcNow();
        foreach (var machine in machines.All)
        {
            if (machine.CheckSilence(now, Threshold) is { } alert)
                alerts.Raise(alert);
        }
    }
}
