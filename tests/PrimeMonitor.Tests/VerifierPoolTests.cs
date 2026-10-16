using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PrimeMonitor.Monitor;
using PrimeMonitor.Shared;
using static PrimeMonitor.Tests.TestData;

namespace PrimeMonitor.Tests;

public class VerifierPoolTests
{
    private static VerifierPool Pool(MachineRegistry machines) =>
        new(Channel.CreateBounded<PrimeMessage>(10), machines, TestData.Alerts(),
            Options.Create(new MonitorOptions()), NullLogger<VerifierPool>.Instance);

    [Fact]
    public void Verify_CompositeNumber_RaisesNotPrime()
    {
        var machines = new MachineRegistry();

        Pool(machines).Verify(Msg(1, prime: 561));

        Assert.Equal(1, machines.GetOrAdd("1").Snapshot().Count(AlertType.NotPrime));
    }

    [Fact]
    public void Verify_PrimeNumber_RaisesNothing()
    {
        var machines = new MachineRegistry();

        Pool(machines).Verify(Msg(1, prime: 1000003));

        Assert.Equal(0, machines.GetOrAdd("1").Snapshot().Count(AlertType.NotPrime));
    }
}
