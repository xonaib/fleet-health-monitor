using PrimeMonitor.Shared;

namespace PrimeMonitor.Monitor;

public sealed record MachineSnapshot(
    string MachineId,
    long LastSeq,
    long Messages,
    bool TooSlow,
    bool TooFast,
    bool Unresponsive,
    int OpenConnections,
    DateTimeOffset? LastArrival,
    long? LastIntervalMs,
    IReadOnlyDictionary<AlertType, int> AlertCounts)
{
    public int Count(AlertType type) => AlertCounts.GetValueOrDefault(type);

    public string State =>
        Unresponsive ? "UNRESPONSIVE"
        : OpenConnections == 0 ? "DISCONNECTED"
        : TooSlow ? "TOO_SLOW"
        : TooFast ? "TOO_FAST"
        : "OK";
}

// One per machine ID. Several threads touch it at once (that machine's connection, any verifier,
// a second connection claiming the same ID), so every read-modify-write goes through _gate.
// Methods return alerts instead of logging them so logging happens outside the lock.
public sealed class MachineState(string machineId)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<AlertType, int> _alertCounts = [];
    private bool _seen;
    private long _lastSeq;
    private long _messages;
    private bool _tooSlow;
    private bool _tooFast;
    private bool _unresponsive;
    private int _openConnections;
    private DateTimeOffset? _lastArrival;
    private long? _lastIntervalMs;

    public string MachineId => machineId;

    public IReadOnlyList<Alert> OnMessage(PrimeMessage message, bool firstOnConnection, DateTimeOffset now, Band band)
    {
        var raised = new List<Alert>();
        lock (_gate)
        {
            _messages++;
            if (firstOnConnection) _openConnections++;
            var previousArrival = _lastArrival;
            _lastArrival = now;

            if (_unresponsive)
            {
                _unresponsive = false;
                raised.Add(Record(AlertType.Recovered, message.Seq,
                    $"message after {(long)(now - previousArrival!.Value).TotalMilliseconds}ms of silence"));
            }

            // First message since the monitor started: any seq is a valid baseline, otherwise a monitor
            // restart would report every machine as having lost messages. seq=1 on a new connection is a
            // worker restart (workers number from 1 per run), not a replay.
            if (!_seen || (firstOnConnection && message.Seq == 1))
            {
                _seen = true;
                _lastSeq = message.Seq;
            }
            else
            {
                switch (Rules.CheckSeq(_lastSeq, message.Seq))
                {
                    case SeqVerdict.Gap:
                        raised.Add(Record(AlertType.SeqGap, message.Seq,
                            $"expected seq {_lastSeq + 1}, got {message.Seq} ({message.Seq - _lastSeq - 1} missing)"));
                        _lastSeq = message.Seq;
                        break;
                    case SeqVerdict.Duplicate:
                        // A replay of something already judged: re-running the timing checks would compare
                        // against a near-zero arrival interval and the original's duration.
                        raised.Add(Record(AlertType.SeqDuplicate, message.Seq,
                            $"seq {message.Seq} not after last seq {_lastSeq}"));
                        return raised;
                    default:
                        _lastSeq = message.Seq;
                        break;
                }
            }

            // The interval across a reconnect measures the outage, not the work, so it is not judged here.
            long? intervalMs = firstOnConnection || previousArrival is null
                ? null
                : (long)(now - previousArrival.Value).TotalMilliseconds;
            _lastIntervalMs = intervalMs;

            var reported = Rules.CheckDuration(message.DurationMs, band);
            // The monitor's own clock is checked too, so a worker can't hide slowness by under-reporting.
            var intervalTooSlow = intervalMs is { } i && Rules.CheckDuration(i, band) == DurationVerdict.TooSlow;

            var tooSlow = reported == DurationVerdict.TooSlow || intervalTooSlow;
            if (tooSlow && !_tooSlow)
            {
                raised.Add(Record(AlertType.TooSlow, message.Seq,
                    $"reported {message.DurationMs}ms, measured interval {intervalMs?.ToString() ?? "n/a"}ms, band {band}"));
            }
            _tooSlow = tooSlow;

            var tooFast = reported == DurationVerdict.TooFast;
            if (tooFast && !_tooFast)
            {
                raised.Add(Record(AlertType.TooFast, message.Seq, $"reported {message.DurationMs}ms, band {band}"));
            }
            _tooFast = tooFast;
        }
        return raised;
    }

    // Called by the watchdog. Reading _lastArrival and setting _unresponsive under the same lock as
    // OnMessage means a message that lands mid-check is either seen here (no alert) or clears the flag
    // right after (RECOVERED); the machine can't be left flagged after it has sent.
    public Alert? CheckSilence(DateTimeOffset now, TimeSpan threshold)
    {
        lock (_gate)
        {
            if (_unresponsive || _lastArrival is null) return null;
            var silence = now - _lastArrival.Value;
            if (silence <= threshold) return null;
            _unresponsive = true;
            return Record(AlertType.Unresponsive, _lastSeq,
                $"no message for {(long)silence.TotalMilliseconds}ms, threshold {(long)threshold.TotalMilliseconds}ms");
        }
    }

    public Alert OnDisconnected(string reason)
    {
        lock (_gate)
        {
            if (_openConnections > 0) _openConnections--;
            return Record(AlertType.Disconnected, _lastSeq, reason);
        }
    }

    // For one-off events raised from outside the message path (verifier, full queue, bad line).
    public Alert RecordEvent(AlertType type, long? seq, string detail)
    {
        lock (_gate)
        {
            return Record(type, seq, detail);
        }
    }

    public MachineSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new MachineSnapshot(machineId, _lastSeq, _messages, _tooSlow, _tooFast, _unresponsive,
                _openConnections, _lastArrival, _lastIntervalMs, new Dictionary<AlertType, int>(_alertCounts));
        }
    }

    private Alert Record(AlertType type, long? seq, string detail)
    {
        _alertCounts[type] = _alertCounts.GetValueOrDefault(type) + 1;
        return new Alert(type, machineId, seq, detail);
    }
}
