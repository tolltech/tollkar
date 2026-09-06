namespace Tollkar.Web.Tests;

/// <summary>Moves the clock forward so expiration boundaries are tested without waiting for them.</summary>
public sealed class AdjustableTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset current = now;

    public override TimeZoneInfo LocalTimeZone { get; } = TimeZoneInfo.CreateCustomTimeZone(
        "Test", now.Offset, "Test", "Test");

    public override DateTimeOffset GetUtcNow() => current.ToUniversalTime();

    public void Advance(TimeSpan interval) => current = current.Add(interval);
}
