using Microsoft.Extensions.Logging.Abstractions;
using PrimeMonitor.Monitor;
using static PrimeMonitor.Tests.TestData;

namespace PrimeMonitor.Tests;

public class StatusReporterTests
{
    [Fact]
    public void BuildRows_UnresponsiveMachine_ShowsStateAndAlertCounts()
    {
        var machines = new MachineRegistry();
        var state = machines.GetOrAdd("7");
        state.OnMessage(Msg(1, machineId: "7"), true, T0, Band20s);
        state.CheckSilence(T0.AddSeconds(30), Threshold20s);
        var reporter = new StatusReporter(machines, TimeProvider.System, NullLogger<StatusReporter>.Instance);

        var row = Assert.Single(reporter.BuildRows());

        Assert.Equal("7", row.MachineId);
        Assert.Equal("UNRESPONSIVE", row.State.Trim());
        Assert.Equal("UNRESPONSIVE=1", row.Alerts);
    }
}
