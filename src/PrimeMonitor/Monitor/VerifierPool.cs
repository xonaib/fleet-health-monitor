using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrimeMonitor.Shared;

namespace PrimeMonitor.Monitor;

public sealed class VerifierPool(
    Channel<PrimeMessage> verifyQueue,
    MachineRegistry machines,
    Alerts alerts,
    IOptions<MonitorOptions> options,
    ILogger<VerifierPool> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(Enumerable.Range(0, Math.Max(1, options.Value.VerifierCount))
            .Select(_ => RunVerifierAsync(stoppingToken)));

    private async Task RunVerifierAsync(CancellationToken ct)
    {
        await foreach (var message in verifyQueue.Reader.ReadAllAsync(ct))
        {
            Verify(message);
        }
    }

    public void Verify(PrimeMessage message)
    {
        if (Primality.IsPrime(message.Prime))
        {
            logger.LogDebug("Verified machine={MachineId} seq={Seq} prime={Prime}", message.MachineId, message.Seq, message.Prime);
            return;
        }
        alerts.Raise(machines.GetOrAdd(message.MachineId)
            .RecordEvent(AlertType.NotPrime, message.Seq, $"{message.Prime} is composite"));
    }
}
