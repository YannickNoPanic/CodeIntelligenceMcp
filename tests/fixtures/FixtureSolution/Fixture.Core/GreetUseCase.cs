namespace Fixture.Core;

public sealed class GreetUseCase : IGreetUseCase
{
    public string Greet(string name) => $"Hello, {name}";
}
