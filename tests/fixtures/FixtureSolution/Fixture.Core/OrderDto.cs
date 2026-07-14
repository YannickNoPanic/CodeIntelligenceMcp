namespace Fixture.Core;

// Deliberate bait for the dto-in-core violation rule.
public sealed record OrderDto(int Id, string Name);
