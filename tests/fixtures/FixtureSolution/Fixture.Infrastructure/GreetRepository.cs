namespace Fixture.Infrastructure;

public sealed class GreetRepository
{
    public IReadOnlyList<string> GetNames() => ["world"];
}
