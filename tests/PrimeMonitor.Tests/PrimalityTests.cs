using PrimeMonitor.Shared;

namespace PrimeMonitor.Tests;

public class PrimalityTests
{
    [Theory]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(37UL)]
    [InlineData(41UL)]
    [InlineData(1000003UL)]
    [InlineData(4294967291UL)]          // largest 32-bit prime
    [InlineData(18446744073709551557UL)] // largest 64-bit prime
    public void IsPrime_KnownPrime_ReturnsTrue(ulong n)
    {
        Assert.True(Primality.IsPrime(n));
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(1UL)]
    [InlineData(4UL)]
    [InlineData(9UL)]
    [InlineData(561UL)]                          // Carmichael
    [InlineData(1105UL)]                         // Carmichael
    [InlineData(41041UL)]                        // Carmichael
    [InlineData(3215031751UL)]                   // strong pseudoprime to bases 2, 3, 5, 7
    [InlineData(4294967291UL * 4294967279UL)]    // product of two large primes
    [InlineData(18446744073709551615UL)]         // ulong.MaxValue
    public void IsPrime_Composite_ReturnsFalse(ulong n)
    {
        Assert.False(Primality.IsPrime(n));
    }

    [Fact]
    public void IsPrime_AllNumbersBelow100000_MatchesTrialDivision()
    {
        for (ulong n = 0; n < 100_000; n++)
        {
            Assert.True(TrialDivision(n) == Primality.IsPrime(n), $"mismatch at {n}");
        }
    }

    private static bool TrialDivision(ulong n)
    {
        if (n < 2) return false;
        for (ulong d = 2; d * d <= n; d++)
        {
            if (n % d == 0) return false;
        }
        return true;
    }
}
