using System.Net.Http;

namespace Fixture.Core;

// Deliberate bait for the core-no-http and direct-instantiation violation rules.
public class HttpViolation
{
    private readonly HttpClient _client = new();

    public HttpClient Client => _client;
}
