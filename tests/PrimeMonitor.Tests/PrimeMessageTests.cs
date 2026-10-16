using PrimeMonitor.Shared;

namespace PrimeMonitor.Tests;

public class PrimeMessageTests
{
    [Fact]
    public void ToJsonLine_ThenTryParse_ReturnsEqualMessage()
    {
        var original = new PrimeMessage("1", 42, 1000003, 20113, new DateTime(2026, 10, 16, 10, 0, 0, DateTimeKind.Utc));

        var ok = PrimeMessage.TryParse(original.ToJsonLine(), out var parsed);

        Assert.True(ok);
        Assert.Equal(original, parsed);
    }

    [Fact]
    public void ToJsonLine_Always_UsesCamelCaseWireFormat()
    {
        var message = new PrimeMessage("1", 42, 1000003, 20113, new DateTime(2026, 10, 16, 10, 0, 0, DateTimeKind.Utc));

        var json = message.ToJsonLine();

        Assert.Equal(
            """{"machineId":"1","seq":42,"prime":1000003,"durationMs":20113,"sentAtUtc":"2026-10-16T10:00:00Z"}""",
            json);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"machineId":"","seq":1,"prime":7,"durationMs":1,"sentAtUtc":"2026-10-16T10:00:00Z"}""")]
    [InlineData("""{"machineId":"1","seq":0,"prime":7,"durationMs":1,"sentAtUtc":"2026-10-16T10:00:00Z"}""")]
    [InlineData("""{"machineId":"1","seq":1,"prime":-7,"durationMs":1,"sentAtUtc":"2026-10-16T10:00:00Z"}""")]
    public void TryParse_InvalidLine_ReturnsFalse(string line)
    {
        var ok = PrimeMessage.TryParse(line, out var parsed);

        Assert.False(ok);
        Assert.Null(parsed);
    }
}
