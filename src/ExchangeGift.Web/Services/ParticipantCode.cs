using System.Security.Cryptography;

namespace ExchangeGift.Web.Services;

public static class ParticipantCode
{
    public const int Length = 6;
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    /// <summary>Cryptographically secure, unbiased six-character uppercase alphanumeric code.</summary>
    public static string Generate()
    {
        Span<char> chars = stackalloc char[Length];
        for (var i = 0; i < Length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(chars);
    }

    public static bool IsWellFormed(string? code)
    {
        if (code is null || code.Length != Length) return false;
        foreach (var c in code)
            if (!(c is >= 'A' and <= 'Z' || c is >= '0' and <= '9')) return false;
        return true;
    }

    /// <summary>Uppercases a user-typed code; returns null when it is not a valid shape.</summary>
    public static string? Normalize(string? raw)
    {
        var code = raw?.Trim().ToUpperInvariant();
        return IsWellFormed(code) ? code : null;
    }
}
