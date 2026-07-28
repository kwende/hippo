using Hippo.Server.Authentication;
using Hippo.Server.Configuration;
using Hippo.Server.Data;
using Hippo.Server.Services;
using ModelContextProtocol.Server;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<HippoOptions>()
    .Bind(builder.Configuration.GetSection(HippoOptions.SectionName))
    .Validate(
        options => options.ApiKeys.Count > 0,
        "At least one Hippo API key must be configured.")
    .Validate(
        options => options.ApiKeys.All(key =>
            !string.IsNullOrWhiteSpace(key.Token) &&
            !string.IsNullOrWhiteSpace(key.PrincipalId) &&
            !string.IsNullOrWhiteSpace(key.DisplayName) &&
            !string.IsNullOrWhiteSpace(key.ClientName)),
        "Every Hippo API key requires token, principal, display name, and client.")
    .ValidateOnStart();

builder.Services.AddSingleton(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var connectionString =
        configuration.GetConnectionString("Hippo")
        ?? throw new InvalidOperationException(
            "ConnectionStrings:Hippo is required.");

    return new NpgsqlDataSourceBuilder(connectionString).Build();
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CallerIdentityAccessor>();
builder.Services.AddScoped<MemoryRepository>();
builder.Services.AddSingleton<FindingCanonicalizer>();
builder.Services.AddSingleton<FindingValidator>();

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInstructions =
            """
            Hippo provides shared operational memory for deployed machines,
            customers, incidents, software versions, hardware revisions, and
            components.

            When diagnostic work concerns identifiable operational entities,
            recall context before finalizing conclusions. Treat returned items
            as evidence with status and confidence, not unquestionable truth.

            After deriving novel durable evidence-backed information, record a
            small number of structured findings. Do not store conversations,
            raw logs, credentials, routine plans, or unsupported speculation.
            Do not narrate routine Hippo calls unless the user asks.
            """;
    })
    .WithHttpTransport(options =>
    {
        options.Stateless = true;
    })
    .WithToolsFromAssembly();

var app = builder.Build();

app.UseMiddleware<ApiKeyAuthenticationMiddleware>();

app.MapGet(
    "/health/live",
    () => Results.Ok(new
    {
        status = "live",
        service = "hippo"
    }));

app.MapGet(
    "/health/ready",
    async (NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
    {
        await using var command = dataSource.CreateCommand("SELECT 1;");
        await command.ExecuteScalarAsync(cancellationToken);

        return Results.Ok(new
        {
            status = "ready",
            service = "hippo"
        });
    });

app.MapMcp("/mcp");

app.Run();

public partial class Program;
