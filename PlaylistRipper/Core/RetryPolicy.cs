using System;
using System.Threading.Tasks;

namespace PlaylistRipper.Core;

public class RetryPolicy
{
    private readonly ConsoleUi _ui;

    public RetryPolicy(ConsoleUi ui) => _ui = ui;

    public async Task<(int exitCode, string output)> RunWithRetriesAsync(
        Func<Task<(int exitCode, string output)>> action,
        int maxAttempts,
        int baseDelaySeconds,
        int maxDelaySeconds,
        Func<int, string, FailureKind> classify)
    {
        maxAttempts = Math.Max(1, maxAttempts);
        baseDelaySeconds = Math.Max(1, baseDelaySeconds);
        maxDelaySeconds = Math.Max(baseDelaySeconds, maxDelaySeconds);

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var (exit, output) = await action();
            if (exit == 0) return (exit, output);

            var kind = classify(exit, output);

            // AuthRequired: don't retry blindly, it won't fix itself.
            if (kind == FailureKind.AuthRequired)
                return (exit, output);

            // Unknown: retry a little, but not forever
            bool retryable =
                kind == FailureKind.Transient ||
                kind == FailureKind.RateLimited ||
                kind == FailureKind.Forbidden ||
                kind == FailureKind.Unknown;

            if (!retryable || attempt == maxAttempts)
                return (exit, output);

            int delay = ComputeDelaySeconds(attempt, baseDelaySeconds, maxDelaySeconds);
            _ui.WriteLine($"   ⏳ Attempt {attempt}/{maxAttempts} failed ({kind}). Waiting {delay}s then retrying...");
            await Task.Delay(TimeSpan.FromSeconds(delay));
        }

        // Should never hit here
        return (-1, "RetryPolicy: unexpected fallthrough");
    }

    private static int ComputeDelaySeconds(int attempt, int baseDelaySeconds, int maxDelaySeconds)
    {
        // exponential-ish: base * 2^(attempt-1), capped
        double d = baseDelaySeconds * Math.Pow(2, attempt - 1);
        if (d > maxDelaySeconds) d = maxDelaySeconds;
        return (int)Math.Round(d);
    }
}
