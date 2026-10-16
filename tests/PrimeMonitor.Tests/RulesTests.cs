using PrimeMonitor.Monitor;

namespace PrimeMonitor.Tests;

public class RulesTests
{
    private static readonly Band Band20s = Rules.BandFor(20, 20);

    [Fact]
    public void BandFor_20SecondsWith20Percent_Is16000To24000Ms()
    {
        Assert.Equal(new Band(16000, 24000), Band20s);
    }

    [Fact]
    public void BandFor_2SecondsWith20Percent_HasNoFloatingPointDrift()
    {
        Assert.Equal(new Band(1600, 2400), Rules.BandFor(2, 20));
    }

    [Theory]
    [InlineData(16000)]
    [InlineData(20000)]
    [InlineData(24000)]
    public void CheckDuration_InsideOrOnBandEdge_ReturnsOk(long ms)
    {
        Assert.Equal(DurationVerdict.Ok, Rules.CheckDuration(ms, Band20s));
    }

    [Fact]
    public void CheckDuration_JustBelowBand_ReturnsTooFast()
    {
        Assert.Equal(DurationVerdict.TooFast, Rules.CheckDuration(15999, Band20s));
    }

    [Fact]
    public void CheckDuration_JustAboveBand_ReturnsTooSlow()
    {
        Assert.Equal(DurationVerdict.TooSlow, Rules.CheckDuration(24001, Band20s));
    }

    [Fact]
    public void CheckSeq_NextNumber_ReturnsOk()
    {
        Assert.Equal(SeqVerdict.Ok, Rules.CheckSeq(5, 6));
    }

    [Fact]
    public void CheckSeq_SkipsAhead_ReturnsGap()
    {
        Assert.Equal(SeqVerdict.Gap, Rules.CheckSeq(5, 7));
    }

    [Theory]
    [InlineData(5L)]
    [InlineData(3L)]
    public void CheckSeq_RepeatedOrBackwards_ReturnsDuplicate(long seq)
    {
        Assert.Equal(SeqVerdict.Duplicate, Rules.CheckSeq(5, seq));
    }
}
