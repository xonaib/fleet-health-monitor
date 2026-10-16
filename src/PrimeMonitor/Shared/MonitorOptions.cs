namespace PrimeMonitor.Shared;

public sealed class MonitorOptions
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 5050;
    public double ExpectedSeconds { get; set; } = 20;
    public double DeviationPercent { get; set; } = 20;
    public double SilenceGraceSeconds { get; set; } = 1;
    public int QueueCapacity { get; set; } = 100;
    public int VerifierCount { get; set; } = 2;
}
