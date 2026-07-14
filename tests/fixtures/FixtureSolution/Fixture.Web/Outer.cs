namespace Fixture.Web;

// Second link in the caller chain: Outer.Run -> Caller.InvokeConcrete -> GreetUseCase.Greet.
// Exists for the transitive find_callers (depth=2) integration test.
public sealed class Outer(Caller caller)
{
    public string Run() => caller.InvokeConcrete("world");
}
