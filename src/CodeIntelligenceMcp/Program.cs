using CodeIntelligenceMcp.Config;
using CodeIntelligenceMcp.Logging;
using CodeIntelligenceMcp.Tools;
using CodeIntelligenceMcp.Workspaces;

string logPath = Path.Combine(Path.GetTempPath(), "CodeIntelligenceMcp.log");

FileLoggerProvider fileLogger;
try
{
    fileLogger = new FileLoggerProvider(logPath);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[CodeIntelligenceMcp] FATAL: could not create log file at '{logPath}': {ex.Message}");
    Environment.Exit(1);
    return;
}

ILogger startupLogger = fileLogger.CreateLogger("Startup");
bool useSse = args.Contains("--sse");

startupLogger.LogInformation("PID={Pid} args={Args}", Environment.ProcessId, string.Join(" ", args));
startupLogger.LogInformation("Mode={Mode}", useSse ? "SSE" : "stdio");

try
{
    // Config discovery: --config CLI arg, then CODEINTEL_CONFIG env var, then next to the binary.
    // Kept separate from the host configuration system (Mcp:Port) on purpose.
    static string ResolveConfigPath(string[] args)
    {
        int i = Array.IndexOf(args, "--config");
        if (i >= 0 && i + 1 < args.Length)
            return Path.GetFullPath(args[i + 1]);

        return Environment.GetEnvironmentVariable("CODEINTEL_CONFIG") is { Length: > 0 } env
            ? Path.GetFullPath(env)
            : Path.Combine(AppContext.BaseDirectory, "mcp-config.json");
    }

    string configPath = ResolveConfigPath(args);
    startupLogger.LogInformation("Loading config from '{ConfigPath}'", configPath);
    McpConfig config = McpConfigLoader.Load(configPath, startupLogger);

    startupLogger.LogInformation(
        "Config loaded — {Count} workspace(s): {Names}",
        config.Workspaces.Count,
        string.Join(", ", config.Workspaces.Select(w => $"{w.Name} ({w.Type})")));

    Dictionary<string, CleanArchitectureNames> cleanArchConfig = config.Workspaces
        .Where(w => w.Type == "dotnet" && w.Solution is not null)
        .ToDictionary(
            w => w.Name,
            w => w.CleanArchitecture is not null
                ? new CleanArchitectureNames(
                    w.CleanArchitecture.CoreProject,
                    w.CleanArchitecture.InfraProject,
                    w.CleanArchitecture.WebProject)
                : new CleanArchitectureNames(string.Empty, string.Empty, string.Empty));

    void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton(config);
        services.AddSingleton(new McpConfigSource(configPath));
        services.AddSingleton<WorkspaceCatalog>();
        services.AddSingleton(new CleanArchRegistry(cleanArchConfig));
        services.AddSingleton<IWorkspaceProvider<RoslynWorkspaceIndex>, RoslynWorkspaceProvider>();
        services.AddSingleton<IWorkspaceProvider<AspIndex>>(sp =>
            new FileWalkWorkspaceProvider<AspIndex>(
                sp.GetRequiredService<WorkspaceCatalog>(),
                sp.GetRequiredService<ILoggerFactory>().CreateLogger("AspWorkspace"),
                "asp-classic", AspIndex.Build, i => i.FileCount));
        services.AddSingleton<IWorkspaceProvider<PowerShellIndex>>(sp =>
            new FileWalkWorkspaceProvider<PowerShellIndex>(
                sp.GetRequiredService<WorkspaceCatalog>(),
                sp.GetRequiredService<ILoggerFactory>().CreateLogger("PowerShellWorkspace"),
                "powershell", PowerShellIndex.Build, i => i.FileCount));
        services.AddSingleton<IWorkspaceProvider<PythonIndex>>(sp =>
            new FileWalkWorkspaceProvider<PythonIndex>(
                sp.GetRequiredService<WorkspaceCatalog>(),
                sp.GetRequiredService<ILoggerFactory>().CreateLogger("PythonWorkspace"),
                "python", PythonIndex.Build, i => i.FileCount));
        services.AddSingleton<IWorkspaceProvider<JsIndex>>(sp =>
            new FileWalkWorkspaceProvider<JsIndex>(
                sp.GetRequiredService<WorkspaceCatalog>(),
                sp.GetRequiredService<ILoggerFactory>().CreateLogger("JsWorkspace"),
                "javascript", JsIndex.Build, i => i.FileCount));
    }

    if (useSse)
    {
        var builder = WebApplication.CreateBuilder(args);
        int port = builder.Configuration.GetValue<int>("Mcp:Port", 5100);
        builder.WebHost.UseUrls($"http://localhost:{port}");

        builder.Logging.AddProvider(fileLogger);

        RegisterServices(builder.Services);

        builder.Services
            .AddMcpServer(options => options.ServerInstructions = ServerInstructions.Text)
            .WithHttpTransport()
            .WithTools<CSharpTools>()
            .WithTools<AspClassicTools>()
            .WithTools<SqlTools>()
            .WithTools<CodebaseWikiTool>()
            .WithTools<DiagnosticsTool>()
            .WithTools<ChangeAnalysisTool>()
            .WithTools<PowerShellTools>()
            .WithTools<PythonTools>()
            .WithTools<JsTools>()
            .WithTools<WorkspaceManagementTool>();

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

        Console.Error.WriteLine($"[CodeIntelligenceMcp] SSE mode — http://localhost:{port}/sse — log: {logPath}");
        startupLogger.LogInformation("Listening on http://localhost:{Port}/sse", port);

        await app.RunAsync();
    }
    else
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Logging.AddProvider(fileLogger);

        RegisterServices(builder.Services);

        builder.Services
            .AddMcpServer(options => options.ServerInstructions = ServerInstructions.Text)
            .WithStdioServerTransport()
            .WithTools<CSharpTools>()
            .WithTools<AspClassicTools>()
            .WithTools<SqlTools>()
            .WithTools<CodebaseWikiTool>()
            .WithTools<DiagnosticsTool>()
            .WithTools<ChangeAnalysisTool>()
            .WithTools<PowerShellTools>()
            .WithTools<PythonTools>()
            .WithTools<JsTools>()
            .WithTools<WorkspaceManagementTool>();

        Console.Error.WriteLine($"[CodeIntelligenceMcp] Stdio mode — log: {logPath}");

        var app = builder.Build();
        await app.RunAsync();
    }
}
catch (Exception ex)
{
    startupLogger.LogCritical(ex, "Startup failed: {Message}", ex.Message);
    Console.Error.WriteLine($"[CodeIntelligenceMcp] FATAL: {ex.Message} (see {logPath})");
    Environment.ExitCode = 1;
    throw;
}
