namespace Hippo.Server.Authentication;

public sealed record CallerIdentity(
    string PrincipalId,
    string DisplayName,
    string ClientName);

public sealed class CallerIdentityAccessor
{
    public CallerIdentity? Current { get; set; }

    public CallerIdentity RequireCurrent() =>
        Current
        ?? throw new InvalidOperationException(
            "The current MCP caller has not been authenticated.");
}
