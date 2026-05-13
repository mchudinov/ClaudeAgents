namespace Foundry.Agents.TestUtils;

/// <summary>Deterministic ULID generator: emits 01J6Z8K0000000000000000001, ..002, ..003 in order.</summary>
public sealed class DeterministicUlidGenerator
{
    private int _counter;
    public string Next() => $"01J6Z8K00000000000000000{++_counter:D2}";
}
