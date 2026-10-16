namespace PrimeMonitor.Monitor;

public readonly record struct Band(long LowerMs, long UpperMs)
{
    public override string ToString() => $"{LowerMs}-{UpperMs}ms";
}

public enum DurationVerdict { Ok, TooFast, TooSlow }

public enum SeqVerdict { Ok, Gap, Duplicate }

public static class Rules
{
    public static Band BandFor(double expectedSeconds, double deviationPercent)
    {
        var expectedMs = expectedSeconds * 1000;
        var deviation = deviationPercent / 100;
        // Rounded so floating-point noise (2 * 0.8 = 1.6000000000000001) can't move a boundary by 1ms.
        return new Band(
            (long)Math.Round(expectedMs * (1 - deviation)),
            (long)Math.Round(expectedMs * (1 + deviation)));
    }

    // Both band edges count as healthy.
    public static DurationVerdict CheckDuration(long ms, Band band) =>
        ms < band.LowerMs ? DurationVerdict.TooFast
        : ms > band.UpperMs ? DurationVerdict.TooSlow
        : DurationVerdict.Ok;

    public static SeqVerdict CheckSeq(long lastSeq, long seq) =>
        seq == lastSeq + 1 ? SeqVerdict.Ok
        : seq > lastSeq + 1 ? SeqVerdict.Gap
        : SeqVerdict.Duplicate;
}
