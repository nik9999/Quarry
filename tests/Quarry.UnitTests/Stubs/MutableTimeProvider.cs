namespace Quarry.UnitTests.Stubs;

/// <summary>
/// Deterministic <see cref="TimeProvider"/> whose clock only moves when the test
/// calls <see cref="Advance"/>.
/// </summary>
internal sealed class MutableTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public MutableTimeProvider(DateTimeOffset start) => _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now += delta;
}
