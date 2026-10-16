using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrimeMonitor.Shared;

namespace PrimeMonitor.Monitor;

public sealed class ConnectionSession(string remote)
{
    public string Remote => remote;

    // Unknown until the first valid message arrives on this connection.
    public string? MachineId { get; set; }
}

public sealed class ConnectionHandler(
    MachineRegistry machines,
    Alerts alerts,
    Channel<PrimeMessage> verifyQueue,
    IOptions<MonitorOptions> options,
    TimeProvider time,
    ILogger<ConnectionHandler> logger)
{
    private readonly Band _band = Rules.BandFor(options.Value.ExpectedSeconds, options.Value.DeviationPercent);

    public async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        var session = new ConnectionSession(client.Client.RemoteEndPoint?.ToString() ?? "unknown");
        string reason;

        try
        {
            using (client)
            using (var reader = new StreamReader(client.GetStream(), Encoding.UTF8))
            {
                logger.LogInformation("Connection opened from {Remote}", session.Remote);
                while (await reader.ReadLineAsync(ct) is { } line)
                {
                    ProcessLine(session, line);
                }
            }
            reason = "socket closed by machine";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The monitor is shutting down; the machine did nothing wrong.
            return;
        }
        catch (Exception ex) when (ex is IOException or SocketException)
        {
            reason = $"connection lost: {ex.Message}";
        }
        catch (Exception ex)
        {
            // Fire-and-forget task: an unobserved exception here would vanish silently.
            logger.LogError(ex, "Unexpected error on connection from machine {MachineId} at {Remote}", session.MachineId ?? "unknown", session.Remote);
            reason = $"monitor error: {ex.Message}";
        }

        if (session.MachineId is { } machineId)
        {
            alerts.Raise(machines.GetOrAdd(machineId).OnDisconnected($"{reason} ({session.Remote})"));
        }
        else
        {
            logger.LogInformation("Connection from {Remote} closed before identifying a machine: {Reason}", session.Remote, reason);
        }
    }

    // Synchronous on purpose: only cheap checks run here, so the read loop is never held up.
    public void ProcessLine(ConnectionSession session, string line)
    {
        if (!PrimeMessage.TryParse(line, out var message))
        {
            var detail = $"unparseable line: {(line.Length > 200 ? line[..200] + "..." : line)}";
            alerts.Raise(session.MachineId is { } knownId
                ? machines.GetOrAdd(knownId).RecordEvent(AlertType.Malformed, null, detail)
                : new Alert(AlertType.Malformed, $"unknown@{session.Remote}", null, detail));
            return;
        }

        var firstOnConnection = session.MachineId is null;
        session.MachineId = message.MachineId;
        var state = machines.GetOrAdd(message.MachineId);

        logger.LogDebug("Received machine={MachineId} seq={Seq} prime={Prime} durationMs={DurationMs}",
            message.MachineId, message.Seq, message.Prime, message.DurationMs);

        foreach (var alert in state.OnMessage(message, firstOnConnection, time.GetUtcNow(), _band))
        {
            alerts.Raise(alert);
        }

        // TryWrite, never WriteAsync: waiting for queue space would stall this read loop, and a stalled loop
        // makes a healthy machine look slow or silent. An unchecked prime is reported instead of hidden.
        if (!verifyQueue.Writer.TryWrite(message))
        {
            alerts.Raise(state.RecordEvent(AlertType.Unverified, message.Seq,
                $"verifier queue full, prime {message.Prime} not checked"));
        }
    }
}
