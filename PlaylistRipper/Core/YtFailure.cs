using System;

namespace PlaylistRipper.Core;

public enum FailureKind
{
    None,
    Transient,      // network hiccup, 5xx, timeouts, etc.
    RateLimited,    // 429, “too many requests”
    AuthRequired,   // sign in / age restricted / cookies needed
    Forbidden,      // 403 / access denied (sometimes transient, sometimes not)
    Unknown
}

public static class YtFailure
{
    public static FailureKind Classify(int exitCode, string output)
    {
        if (exitCode == 0) return FailureKind.None;

        output ??= "";

        // Auth / age-gate / sign-in
        if (Contains(output, "Sign in to confirm") ||
            Contains(output, "This video may be inappropriate") ||
            Contains(output, "age-restricted") ||
            Contains(output, "cookies") && Contains(output, "required"))
            return FailureKind.AuthRequired;

        // Rate limit / too many requests
        if (Contains(output, "429") ||
            Contains(output, "too many requests") ||
            Contains(output, "rate limit") ||
            Contains(output, "quota"))
            return FailureKind.RateLimited;

        // Common transient signals
        if (Contains(output, "timed out") ||
            Contains(output, "timeout") ||
            Contains(output, "temporarily unavailable") ||
            Contains(output, "connection reset") ||
            Contains(output, "connection refused") ||
            Contains(output, "EOF") ||
            Contains(output, "502") ||
            Contains(output, "503") ||
            Contains(output, "504") ||
            Contains(output, "Internal Server Error"))
            return FailureKind.Transient;

        // Forbidden (can be a true block or a transient CDN issue)
        if (Contains(output, "403") || Contains(output, "Forbidden"))
            return FailureKind.Forbidden;

        return FailureKind.Unknown;
    }

    private static bool Contains(string haystack, string needle) =>
        haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
}
