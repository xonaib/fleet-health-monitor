using Microsoft.Extensions.Logging;

namespace PrimeMonitor.Monitor;

public enum AlertType { NotPrime, TooSlow, TooFast, SeqGap, SeqDuplicate, Malformed, Unverified, Unresponsive, Recovered, Disconnected }

public sealed record Alert(AlertType Type, string MachineId, long? Seq, string Detail);

public sealed class Alerts(ILogger<Alerts> logger)
{
    public void Raise(Alert alert)
    {
        var (level, severity) = Severity(alert.Type);
        logger.Log(level, "ALERT severity={Severity} type={Type} machine={MachineId} seq={Seq} detail=\"{Detail}\"",
            severity, WireName(alert.Type), alert.MachineId, alert.Seq?.ToString() ?? "-", alert.Detail.Replace('"', '\''));
    }

    private static (LogLevel, string) Severity(AlertType type) => type switch
    {
        AlertType.NotPrime or AlertType.TooSlow or AlertType.SeqGap or AlertType.Malformed or AlertType.Unresponsive
            => (LogLevel.Error, "error"),
        AlertType.Recovered => (LogLevel.Information, "info"),
        _ => (LogLevel.Warning, "warning"),
    };

    public static string WireName(AlertType type) => type switch
    {
        AlertType.NotPrime => "NOT_PRIME",
        AlertType.TooSlow => "TOO_SLOW",
        AlertType.TooFast => "TOO_FAST",
        AlertType.SeqGap => "SEQ_GAP",
        AlertType.SeqDuplicate => "SEQ_DUPLICATE",
        AlertType.Malformed => "MALFORMED",
        AlertType.Unverified => "UNVERIFIED",
        AlertType.Unresponsive => "UNRESPONSIVE",
        AlertType.Recovered => "RECOVERED",
        AlertType.Disconnected => "DISCONNECTED",
        _ => type.ToString(),
    };
}
