using Fixture.Core;

namespace Fixture.Web;

public sealed class Caller(IGreetUseCase useCase)
{
    public string Invoke(string name) => useCase.Greet(name);

    public string InvokeConcrete(string name) => new GreetUseCase().Greet(name);
}
