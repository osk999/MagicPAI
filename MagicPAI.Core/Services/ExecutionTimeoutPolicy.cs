namespace MagicPAI.Core.Services;

internal static class ExecutionTimeoutPolicy
{
    private static readonly TimeSpan MinimumHardTimeoutBuffer = TimeSpan.FromMinutes(15);
    private const int HardTimeoutMultiplier = 4;

    public static TimeSpan NormalizeIdleTimeout(TimeSpan timeout) =>
        timeout > TimeSpan.Zero ? timeout : TimeSpan.FromMinutes(30);

    public static TimeSpan GetHardTimeout(TimeSpan idleTimeout)
    {
        idleTimeout = NormalizeIdleTimeout(idleTimeout);

        var buffered = idleTimeout + MinimumHardTimeoutBuffer;
        var multipliedTicks = idleTimeout.Ticks >= long.MaxValue / HardTimeoutMultiplier
            ? long.MaxValue
            : idleTimeout.Ticks * HardTimeoutMultiplier;
        var multiplied = TimeSpan.FromTicks(multipliedTicks);

        return multiplied > buffered ? multiplied : buffered;
    }

    public static void ThrowIfIdle(DateTimeOffset lastActivityUtc, TimeSpan idleTimeout)
    {
        idleTimeout = NormalizeIdleTimeout(idleTimeout);

        if (DateTimeOffset.UtcNow - lastActivityUtc >= idleTimeout)
            throw new IdleCommandTimeoutException(idleTimeout);
    }

    public static string FormatIdleTimeoutMessage(TimeSpan idleTimeout)
    {
        idleTimeout = NormalizeIdleTimeout(idleTimeout);
        return $"No stdout/stderr activity was received for {idleTimeout}. Command stopped after exceeding the inactivity timeout.";
    }

    public static string FormatHardTimeoutMessage(TimeSpan hardTimeout) =>
        $"Command exceeded the hard timeout of {hardTimeout}.";
}

internal sealed class IdleCommandTimeoutException : TimeoutException
{
    public IdleCommandTimeoutException(TimeSpan idleTimeout)
        : base(ExecutionTimeoutPolicy.FormatIdleTimeoutMessage(idleTimeout))
    {
    }
}
