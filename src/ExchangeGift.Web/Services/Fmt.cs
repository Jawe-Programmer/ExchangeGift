namespace ExchangeGift.Web.Services;

public static class Fmt
{
    /// <summary>ISO-8601 UTC timestamp for &lt;time datetime&gt;; the browser localises it for display.</summary>
    public static string Iso(this DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

    /// <summary>Fallback text (before JavaScript localises it).</summary>
    public static string Plain(this DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm") + " UTC";
}
