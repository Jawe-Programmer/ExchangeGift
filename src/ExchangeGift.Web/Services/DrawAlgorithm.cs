using System.Security.Cryptography;

namespace ExchangeGift.Web.Services;

public static class DrawAlgorithm
{
    /// <summary>
    /// Returns a uniformly random derangement p of 0..n-1 (p[i] != i for every i, each value used once).
    /// Uses cryptographic randomness with rejection sampling, so every derangement is equally likely.
    /// </summary>
    public static int[] GenerateDerangement(int n)
    {
        if (n < 2) throw new ArgumentOutOfRangeException(nameof(n), "At least two participants are required.");

        var perm = new int[n];
        // Expected ~e (≈2.7) attempts; the cap is only a guard that can never realistically be hit.
        for (var attempt = 0; attempt < 10_000; attempt++)
        {
            for (var i = 0; i < n; i++) perm[i] = i;
            Shuffle(perm);
            if (IsDerangement(perm)) return perm;
        }

        // Fallback: Sattolo's algorithm always yields a single n-cycle, which is a valid derangement.
        for (var i = 0; i < n; i++) perm[i] = i;
        for (var i = n - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i);
            (perm[i], perm[j]) = (perm[j], perm[i]);
        }
        return perm;
    }

    public static bool IsDerangement(IReadOnlyList<int> perm)
    {
        var seen = new bool[perm.Count];
        for (var i = 0; i < perm.Count; i++)
        {
            var v = perm[i];
            if (v < 0 || v >= perm.Count || v == i || seen[v]) return false;
            seen[v] = true;
        }
        return true;
    }

    private static void Shuffle(int[] a)
    {
        for (var i = a.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (a[i], a[j]) = (a[j], a[i]);
        }
    }
}
