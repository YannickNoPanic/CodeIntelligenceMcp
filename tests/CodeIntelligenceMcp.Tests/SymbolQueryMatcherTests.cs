using CodeIntelligenceMcp.Roslyn;
using FluentAssertions;
using Xunit;

namespace CodeIntelligenceMcp.Tests;

public sealed class SymbolQueryMatcherTests
{
    [Theory]
    [InlineData("Get*UseCase", "GetHowTosUseCase", true)]
    [InlineData("Get*UseCase", "CreateHowToUseCase", false)]
    [InlineData("I?seCase", "IUseCase", true)]
    [InlineData("usecase", "GetHowTosUseCase", true)]
    [InlineData("usecase", "Repository", false)]
    [InlineData("*Repository", "OrderRepository", true)]
    [InlineData("Order*", "OrderRepository", true)]
    public void Matches_WildcardAndSubstring_BehavesAsDocumented(string query, string candidate, bool expected)
    {
        SymbolQueryMatcher.Matches(query, candidate).Should().Be(expected);
    }

    [Theory]
    [InlineData("Get*UseCase", true)]
    [InlineData("I?seCase", true)]
    [InlineData("usecase", false)]
    public void HasWildcards_DetectsGlobCharacters(string query, bool expected)
    {
        SymbolQueryMatcher.HasWildcards(query).Should().Be(expected);
    }
}
