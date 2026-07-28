namespace Hippo.Server.Configuration;

public sealed class HippoOptions
{
    public const string SectionName = "Hippo";

    public int MaximumRecallResults { get; init; } = 12;

    public int MaximumFindingsPerWrite { get; init; } = 10;

    public int MaximumEvidenceExcerptLength { get; init; } = 2000;

    public List<ApiKeyDefinition> ApiKeys { get; init; } = [];
}

public sealed class ApiKeyDefinition
{
    public string Token { get; init; } = string.Empty;

    public string PrincipalId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string ClientName { get; init; } = string.Empty;
}
