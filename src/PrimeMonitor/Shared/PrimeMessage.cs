using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace PrimeMonitor.Shared;

public sealed record PrimeMessage(string MachineId, long Seq, ulong Prime, long DurationMs, DateTime SentAtUtc)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string ToJsonLine() => JsonSerializer.Serialize(this, JsonOptions);

    public static bool TryParse(string line, [NotNullWhen(true)] out PrimeMessage? message)
    {
        message = null;
        try
        {
            var parsed = JsonSerializer.Deserialize<PrimeMessage>(line, JsonOptions);
            // The serializer leaves missing properties at their defaults, so a line like {} would otherwise "parse".
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.MachineId) || parsed.Seq < 1)
                return false;
            message = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
