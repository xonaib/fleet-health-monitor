using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using PrimeMonitor.Monitor;
using PrimeMonitor.Shared;
using static PrimeMonitor.Tests.TestData;

namespace PrimeMonitor.Tests;

public class SilenceWatchdogTests
{
    private static SilenceWatchdog Watchdog(MachineRegistry machines, TimeProvider time) =>
        new(machines, TestData.Alerts(),
            Options.Create(new MonitorOptions { ExpectedSeconds = 20, DeviationPercent = 20, SilenceGraceSeconds = 1 }), time);

    [Fact]
    public void Threshold_20SecondsWith20PercentAnd1sGrace_Is25Seconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(25), Watchdog(new MachineRegistry(), TimeProvider.System).Threshold);
    }

    [Fact]
    public async Task ExecuteAsync_MachineSilentPastThreshold_FlagsOnTimerTickNotBefore()
    {
        var time = new FakeTimeProvider(T0);
        var machines = new MachineRegistry();
        var state = machines.GetOrAdd("1");
        state.OnMessage(Msg(1), true, time.GetUtcNow(), Band20s);
        var watchdog = Watchdog(machines, time);
        await watchdog.StartAsync(CancellationToken.None);
        try
        {
            for (var s = 0; s < 25; s++) time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(200); // let any tick continuations run
            Assert.Equal(0, state.Snapshot().Count(AlertType.Unresponsive));

            time.Advance(TimeSpan.FromSeconds(1));
            await WaitUntil(() => state.Snapshot().Count(AlertType.Unresponsive) == 1, "UNRESPONSIVE after 26s");
        }
        finally
        {
            await watchdog.StopAsync(CancellationToken.None);
        }
    }
}
