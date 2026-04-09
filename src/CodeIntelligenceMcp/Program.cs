using System.Diagnostics;
using CodeIntelligenceMcp.Config;
using CodeIntelligenceMcp.Tools;

RoslynLoader.RegisterMSBuild();

string configPath = Path.Combine(AppContext.BaseDirectory, "mcp-config.json");
McpConfig config = McpConfigLoader.Load(configPath);

var roslynIndexes = new Dictionary<string, RoslynWorkspaceIndex>();
var aspIndexes = new Dictionary<string, AspIndex>();
var psIndexes = new Dictionary<string, PowerShellIndex>();
var cleanArchConfig = new Dictionary<string, CleanArchitectureNames>();
var solutionPaths = new Dictionary<string, string>();

var totalStopwatch = Stopwatch.StartNew();

foreach (WorkspaceConfig ws in config.Workspaces)
{
    if (ws.Type == "dotnet" && ws.Solution is not null)
    {
        try
        {
            CleanArchitectureNames cleanArch = ws.CleanArchitecture is not null
                ? new CleanArchitectureNames(
                    ws.CleanArchitecture.CoreProject,
                    ws.CleanArchitecture.InfraProject,
                    ws.CleanArchitecture.WebProject)
                : new CleanArchitectureNames(string.Empty, string.Empty, string.Empty);

            Console.Error.WriteLine($"[info] Loading dotnet workspace '{ws.Name}'...");
            var sw = Stopwatch.StartNew();
            RoslynWorkspaceIndex index = await RoslynLoader.LoadAsync(ws.Solution, cleanArch);
            sw.Stop();
            roslynIndexes[ws.Name] = index;
            cleanArchConfig[ws.Name] = cleanArch;
            solutionPaths[ws.Name] = ws.Solution;
            Console.Error.WriteLine($"[info] Workspace '{ws.Name}' loaded — {index.TypeCount} types in {sw.Elapsed.TotalSeconds:F1}s");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[warn] Failed to load dotnet workspace '{ws.Name}': {ex.Message}");
        }
    }
    else if (ws.Type == "asp-classic" && ws.RootPath is not null)
    {
        try
        {
            Console.Error.WriteLine($"[info] Loading asp-classic workspace '{ws.Name}'...");
            var sw = Stopwatch.StartNew();
            AspIndex index = AspIndex.Build(ws.RootPath, msg => Console.Error.WriteLine(msg));
            sw.Stop();
            aspIndexes[ws.Name] = index;
            Console.Error.WriteLine($"[info] Workspace '{ws.Name}' loaded — {index.FileCount} files in {sw.Elapsed.TotalSeconds:F1}s");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[warn] Failed to load asp-classic workspace '{ws.Name}': {ex.Message}");
        }
    }
    else if (ws.Type == "powershell" && ws.RootPath is not null)
    {
        try
        {
            Console.Error.WriteLine($"[info] Loading powershell workspace '{ws.Name}'...");
            var sw = Stopwatch.StartNew();
            PowerShellIndex index = PowerShellIndex.Build(ws.RootPath, msg => Console.Error.WriteLine(msg));
            sw.Stop();
            psIndexes[ws.Name] = index;
            Console.Error.WriteLine($"[info] Workspace '{ws.Name}' loaded — {index.FileCount} files in {sw.Elapsed.TotalSeconds:F1}s");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[warn] Failed to load powershell workspace '{ws.Name}': {ex.Message}");
        }
    }
}

totalStopwatch.Stop();
Console.Error.WriteLine($"[info] All workspaces loaded in {totalStopwatch.Elapsed.TotalSeconds:F1}s");

bool useSse = args.Contains("--sse");

var builder = WebApplication.CreateBuilder(args);

if (useSse)
{
    int port = builder.Configuration.GetValue<int>("Mcp:Port", 5100);
    builder.WebHost.UseUrls($"http://localhost:{port}");

    builder.Services.AddSingleton(new RoslynIndexRegistry(roslynIndexes));
    builder.Services.AddSingleton(new AspIndexRegistry(aspIndexes));
    builder.Services.AddSingleton(new PowerShellIndexRegistry(psIndexes));
    builder.Services.AddSingleton(new CleanArchRegistry(cleanArchConfig));
    builder.Services.AddSingleton(new SolutionPathRegistry(solutionPaths));

    builder.Services
        .AddMcpServer()
        .WithHttpTransport()
        .WithTools<CSharpTools>()
        .WithTools<AspClassicTools>()
        .WithTools<SqlTools>()
        .WithTools<CodebaseWikiTool>()
        .WithTools<PowerShellTools>();

    var app = builder.Build();

    // Minimal OAuth server so Claude Code can complete its auth flow for local MCP connections.
    // No tokens are validated — these endpoints exist only to satisfy the OAuth discovery dance.
    app.MapGet("/.well-known/oauth-protected-resource", () => Results.Json(new
    {
        resource = $"http://localhost:{port}",
        authorization_servers = new[] { $"http://localhost:{port}" }
    }));

    app.MapGet("/.well-known/oauth-authorization-server", () => Results.Json(new
    {
        issuer = $"http://localhost:{port}",
        authorization_endpoint = $"http://localhost:{port}/oauth/authorize",
        token_endpoint = $"http://localhost:{port}/oauth/token",
        registration_endpoint = $"http://localhost:{port}/register",
        response_types_supported = new[] { "code" },
        grant_types_supported = new[] { "authorization_code" },
        code_challenge_methods_supported = new[] { "S256" }
    }));

    app.MapPost("/register", async (HttpRequest req) =>
    {
        var body = await req.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var redirectUris = body.TryGetProperty("redirect_uris", out var uris)
            ? uris.EnumerateArray().Select(u => u.GetString()).ToArray()
            : [];
        return Results.Json(new
        {
            client_id = "local-mcp-client",
            client_id_issued_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            redirect_uris = redirectUris,
            grant_types = new[] { "authorization_code" },
            response_types = new[] { "code" },
            token_endpoint_auth_method = "none"
        }, statusCode: 201);
    });

    app.MapGet("/oauth/authorize", (string? redirect_uri, string? state) =>
    {
        if (string.IsNullOrEmpty(redirect_uri))
            return Results.BadRequest("redirect_uri required");
        var sep = redirect_uri.Contains('?') ? '&' : '?';
        var location = $"{redirect_uri}{sep}code={Uri.EscapeDataString("local-dev-code")}";
        if (!string.IsNullOrEmpty(state))
            location += $"&state={Uri.EscapeDataString(state)}";
        return Results.Redirect(location);
    });

    app.MapPost("/oauth/token", () => Results.Json(new
    {
        access_token = "local-dev-token",
        token_type = "Bearer",
        expires_in = 86400
    }));

    app.MapMcp();

    Console.Error.WriteLine($"[CodeIntelligenceMcp] SSE mode — http://localhost:{port}/sse");

    await app.RunAsync();
}
else
{
    builder.Services.AddSingleton(new RoslynIndexRegistry(roslynIndexes));
    builder.Services.AddSingleton(new AspIndexRegistry(aspIndexes));
    builder.Services.AddSingleton(new PowerShellIndexRegistry(psIndexes));
    builder.Services.AddSingleton(new CleanArchRegistry(cleanArchConfig));
    builder.Services.AddSingleton(new SolutionPathRegistry(solutionPaths));

    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithTools<CSharpTools>()
        .WithTools<AspClassicTools>()
        .WithTools<SqlTools>()
        .WithTools<CodebaseWikiTool>()
        .WithTools<PowerShellTools>();

    Console.Error.WriteLine("[CodeIntelligenceMcp] Stdio mode");

    var app = builder.Build();
    await app.RunAsync();
}
