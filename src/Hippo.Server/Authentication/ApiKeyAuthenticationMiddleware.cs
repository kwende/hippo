using System.Security.Cryptography;
using System.Text;
using Hippo.Server.Configuration;
using Microsoft.Extensions.Options;

namespace Hippo.Server.Authentication;

public sealed class ApiKeyAuthenticationMiddleware(
    RequestDelegate next,
    IOptions<HippoOptions> options,
    ILogger<ApiKeyAuthenticationMiddleware> logger)
{
    private readonly IReadOnlyList<ApiKeyDefinition> _keys = options.Value.ApiKeys;

    public async Task InvokeAsync(
        HttpContext context,
        CallerIdentityAccessor callerIdentityAccessor)
    {
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await next(context);
            return;
        }

        var token = ReadBearerToken(context.Request.Headers.Authorization);

        if (token is null)
        {
            await RejectAsync(context, "missing_bearer_token");
            return;
        }

        var match = _keys.FirstOrDefault(key =>
            FixedTimeEquals(token, key.Token));

        if (match is null)
        {
            logger.LogWarning(
                "Rejected an MCP request with an invalid bearer token.");
            await RejectAsync(context, "invalid_bearer_token");
            return;
        }

        callerIdentityAccessor.Current = new CallerIdentity(
            match.PrincipalId,
            match.DisplayName,
            match.ClientName);

        await next(context);
    }

    private static string? ReadBearerToken(string? authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader))
        {
            return null;
        }

        const string prefix = "Bearer ";

        return authorizationHeader.StartsWith(
            prefix,
            StringComparison.OrdinalIgnoreCase)
            ? authorizationHeader[prefix.Length..].Trim()
            : null;
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftHash = SHA256.HashData(Encoding.UTF8.GetBytes(left));
        var rightHash = SHA256.HashData(Encoding.UTF8.GetBytes(right));

        return CryptographicOperations.FixedTimeEquals(leftHash, rightHash);
    }

    private static async Task RejectAsync(
        HttpContext context,
        string errorCode)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Bearer";
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsJsonAsync(new
        {
            type = "about:blank",
            title = "Unauthorized",
            status = StatusCodes.Status401Unauthorized,
            code = errorCode
        });
    }
}
