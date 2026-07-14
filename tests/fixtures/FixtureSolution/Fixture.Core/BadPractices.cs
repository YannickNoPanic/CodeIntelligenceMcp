namespace Fixture.Core;

// Deliberate bait for the empty-catch and throw-ex violation rules.
public class BadPractices
{
    public void Swallow()
    {
        try { Run(); } catch { }
    }

    public void Rethrow()
    {
        try { Run(); } catch (Exception ex) { throw ex; }
    }

    private static void Run() { }
}
