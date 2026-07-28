using System.ComponentModel;
using System.Text.Json;

namespace Hippo.Server.Contracts;

public sealed record RecallRequest
{
    [Description(
        "Machine IDs, customer/site IDs, incident numbers, software versions, " +
        "hardware revisions, or other exact operational identifiers.")]
    public required IReadOnlyList<string> EntityIdentifiers { get; init; }

    [Description("Optional beginning of the operational time window.")]
    public DateTimeOffset? TimeRangeStart { get; init; }

    [Description("Optional end of the operational time window.")]
    public DateTimeOffset? TimeRangeEnd { get; init; }

    [Description(
        "Failure concepts discovered in the current task, such as disk space, " +
        "fan speed, logging latency, or thermal throttling.")]
    public IReadOnlyList<string> Concepts { get; init; } = [];

    [Description("Maximum findings to return. The server enforces its own cap.")]
    public int? MaximumResults { get; init; }
}

public sealed record RecallResult
{
    public required IReadOnlyList<ResolvedEntity> MatchedEntities { get; init; }

    public required IReadOnlyList<RecalledFinding> Findings { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }
}

public sealed record ResolvedEntity(
    Guid Id,
    string Kind,
    string CanonicalKey,
    string DisplayName);

public sealed record RecalledFinding
{
    public required Guid Id { get; init; }

    public required string Kind { get; init; }

    public required string Predicate { get; init; }

    public required string Statement { get; init; }

    public JsonElement? Value { get; init; }

    public DateTimeOffset? EffectiveFrom { get; init; }

    public DateTimeOffset? EffectiveTo { get; init; }

    public required DateTimeOffset DiscoveredAt { get; init; }

    public required DateTimeOffset RecordedAt { get; init; }

    public required float Confidence { get; init; }

    public required string Status { get; init; }

    public required string PrincipalName { get; init; }

    public required string AgentClient { get; init; }

    public required IReadOnlyList<RelatedEntity> Entities { get; init; }

    public required IReadOnlyList<EvidenceReference> Evidence { get; init; }
}

public sealed record RelatedEntity(
    string Kind,
    string CanonicalKey,
    string DisplayName,
    string Role);

public sealed record RecordFindingsRequest
{
    [Description(
        "Stable identifier for the current Codex/Claude diagnostic session.")]
    public required string SourceSessionId { get; init; }

    [Description(
        "Optional exact model identifier for provenance, when the client knows it.")]
    public string? AgentModel { get; init; }

    [Description(
        "Novel durable findings only. A normal session should produce zero to a " +
        "handful, not a conversation summary.")]
    public required IReadOnlyList<ProposedFinding> Findings { get; init; }
}

public sealed record ProposedFinding
{
    [Description(
        "One of observation, event, hypothesis, conclusion, or procedure.")]
    public required string Kind { get; init; }

    [Description(
        "Stable machine-readable predicate such as storage.root.free_percent.")]
    public required string Predicate { get; init; }

    [Description("Compact human-readable claim.")]
    public required string Statement { get; init; }

    [Description("Optional structured value associated with the predicate.")]
    public JsonElement? Value { get; init; }

    [Description("Operational entities connected to this finding.")]
    public required IReadOnlyList<EntityReference> Entities { get; init; }

    [Description(
        "Compact evidence excerpts or references. Do not submit entire logs.")]
    public IReadOnlyList<EvidenceReference> Evidence { get; init; } = [];

    [Description("When the finding began to be true in the real world.")]
    public DateTimeOffset? EffectiveFrom { get; init; }

    [Description("When the finding stopped being true, when known.")]
    public DateTimeOffset? EffectiveTo { get; init; }

    [Description("When the current agent session discovered the finding.")]
    public required DateTimeOffset DiscoveredAt { get; init; }

    [Description("Confidence from 0 through 1.")]
    public required float Confidence { get; init; }
}

public sealed record EntityReference
{
    [Description(
        "Entity kind such as machine, customer, site, incident, " +
        "software_version, hardware_revision, or component.")]
    public required string Kind { get; init; }

    [Description("Stable canonical identifier, such as rc1234 or 4.18.2.")]
    public required string CanonicalKey { get; init; }

    [Description("Human-readable display name.")]
    public required string DisplayName { get; init; }

    [Description(
        "Relationship to the finding, such as subject, context, software, " +
        "hardware, customer, or incident.")]
    public required string Role { get; init; }

    [Description("Other exact identifiers that refer to the same entity.")]
    public IReadOnlyList<string> Aliases { get; init; } = [];
}

public sealed record EvidenceReference
{
    [Description(
        "Evidence kind such as log, telemetry, command_output, source_code, " +
        "support_case, or human_report.")]
    public required string Type { get; init; }

    [Description(
        "Stable pointer to the source artifact, when one exists.")]
    public string? SourceReference { get; init; }

    [Description(
        "Small supporting excerpt. Do not include an entire log or document.")]
    public string? Excerpt { get; init; }

    [Description("Optional SHA-256 or other source-artifact digest.")]
    public string? Sha256 { get; init; }

    [Description("When the evidence was observed.")]
    public DateTimeOffset? ObservedAt { get; init; }
}

public sealed record RecordFindingsResult
{
    public required IReadOnlyList<RecordFindingOutcome> Outcomes { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }
}

public sealed record RecordFindingOutcome(
    int SubmittedIndex,
    Guid MemoryId,
    bool Duplicate,
    string Status,
    int EvidenceAttached);
