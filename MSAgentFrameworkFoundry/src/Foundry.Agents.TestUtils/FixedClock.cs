namespace Foundry.Agents.TestUtils;

public sealed class FixedClock : TimeProvider
{
    private DateTimeOffset _now;
    public FixedClock(DateTimeOffset start) => _now = start;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan delta) => _now = _now.Add(delta);
}
