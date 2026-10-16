namespace PrimeMonitor.Shared;

public static class Primality
{
    // The first 12 primes as Miller-Rabin bases give an exact answer for every n < 3.3e24,
    // which covers the whole ulong range: no probabilistic false "prime".
    private static readonly ulong[] Bases = [2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37];

    public static bool IsPrime(ulong n)
    {
        if (n < 2) return false;
        foreach (var p in Bases)
        {
            if (n % p == 0) return n == p;
        }

        var d = n - 1;
        var r = 0;
        while ((d & 1) == 0)
        {
            d >>= 1;
            r++;
        }

        foreach (var a in Bases)
        {
            var x = PowMod(a, d, n);
            if (x == 1 || x == n - 1) continue;

            var witnessesComposite = true;
            for (var i = 1; i < r; i++)
            {
                x = MulMod(x, x, n);
                if (x == n - 1)
                {
                    witnessesComposite = false;
                    break;
                }
            }
            if (witnessesComposite) return false;
        }
        return true;
    }

    public static ulong NextPrime(ulong start)
    {
        var n = start;
        while (!IsPrime(n)) n++;
        return n;
    }

    // UInt128 because a * b overflows ulong for 64-bit moduli.
    private static ulong MulMod(ulong a, ulong b, ulong m) => (ulong)((UInt128)a * b % m);

    private static ulong PowMod(ulong b, ulong e, ulong m)
    {
        ulong result = 1;
        b %= m;
        while (e > 0)
        {
            if ((e & 1) == 1) result = MulMod(result, b, m);
            b = MulMod(b, b, m);
            e >>= 1;
        }
        return result;
    }
}
