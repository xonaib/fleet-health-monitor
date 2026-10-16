using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrimeMonitor.Shared;

namespace PrimeMonitor.Monitor;

public sealed class MonitorRunner(
    IOptions<MonitorOptions> options,
    ConnectionHandler handler,
    ILogger<MonitorRunner> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        var listener = new TcpListener(IPAddress.Parse(opts.Host), opts.Port);
        listener.Start();
        logger.LogInformation("Monitor listening on {Host}:{Port}", opts.Host, opts.Port);

        try
        {
            while (true)
            {
                var client = await listener.AcceptTcpClientAsync(stoppingToken);
                // Not awaited: each connection gets its own read loop so one slow or silent machine never
                // holds up accepting or reading the others. HandleAsync catches its own exceptions.
                _ = handler.HandleAsync(client, stoppingToken);
            }
        }
        finally
        {
            listener.Stop();
        }
    }
}
