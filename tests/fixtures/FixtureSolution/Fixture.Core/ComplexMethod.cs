namespace Fixture.Core;

// Deliberate bait for the complexity analyzer: 3 ifs + 1 loop + base = complexity 5.
public sealed class ComplexMethod
{
    public int Score(int a, int b, int c)
    {
        int total = 0;
        if (a > 0)
            total += a;
        if (b > 0)
            total += b;
        if (c > 0)
            total += c;
        for (int i = 0; i < total; i++)
            total--;
        return total;
    }

    public int Simple() => 1;
}
