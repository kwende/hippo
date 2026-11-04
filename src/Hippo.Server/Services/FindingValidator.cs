using System.Text.RegularExpressions;
using Hippo.Server.Configuration;
using Hippo.Server.Contracts;
using Microsoft.Extensions.Options;

namespace Hippo.Server.Services;

public sealed partial class FindingValidator(IOptions<HippoOptions> options)
{
    private static readonly HashSet<string> AllowedKinds =
    [
        "observation",
        "event",
        "hypothesis",
        "conclusion",
        "procedure"
    ];

    private readonly HippoOptions _options = options.Value;

    public void Validate(RecallRequest request)
    {
        var ids = request.EntityIdentifiers ?? [];
        var sources = request.SourceSystems ?? [];

        if (ids.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "Recall entity identifiers must not be empty or whitespace.");
        }

        if (ids.Count > 50)
        {
            throw new ArgumentException(
                "Recall accepts at most 50 entity identifiers.");
        }

        ValidateSourceSystems(sources, "Recall");

        if (ids.Count == 0 && sources.Count == 0)
        {
            throw new ArgumentException(
                "Recall requires at least one entity identifier or source system.");
        }

        if (request.TimeRangeStart is not null &&
            request.TimeRangeEnd is not null &&
            request.TimeRangeEnd < request.TimeRangeStart)
        {
            throw new ArgumentException(
                "Recall time-range end cannot precede its start.");
        }
    }

    public void Validate(RecordFindingsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SourceSessionId) ||
            request.SourceSessionId.Length > 200)
        {
            throw new ArgumentException(
                "SourceSessionId is required and cannot exceed 200 characters.");
        }

        var findings = request.Findings ?? [];

        if (findings.Count > _options.MaximumFindingsPerWrite)
        {
            throw new ArgumentException(
                $"A write may contain at most " +
                $"{_options.MaximumFindingsPerWrite} findings.");
        }

        for (var index = 0; index < findings.Count; index++)
        {
            Validate(findings[index], index);
        }
    }

    public void ValidateSourceSystem(string sourceSystem) =>
        ValidateSourceSystems([sourceSystem], "Write");

    private void Validate(ProposedFinding finding, int index)
    {
        if (!AllowedKinds.Contains(Normalize(finding.Kind)))
        {
            throw new ArgumentException(
                $"Finding {index} has unsupported kind '{finding.Kind}'.");
        }

        if (string.IsNullOrWhiteSpace(finding.Predicate) ||
            finding.Predicate.Length > 200 ||
            !PredicatePattern().IsMatch(finding.Predicate))
        {
            throw new ArgumentException(
                $"Finding {index} has an invalid predicate. Use a stable " +
                "lowercase dotted identifier.");
        }

        if (string.IsNullOrWhiteSpace(finding.Statement) ||
            finding.Statement.Length > 2000)
        {
            throw new ArgumentException(
                $"Finding {index} requires a statement of at most 2000 characters.");
        }

        if (finding.Confidence is < 0 or > 1)
        {
            throw new ArgumentException(
                $"Finding {index} confidence must be between 0 and 1.");
        }

        if (finding.EffectiveFrom is not null &&
            finding.EffectiveTo is not null &&
            finding.EffectiveTo < finding.EffectiveFrom)
        {
            throw new ArgumentException(
                $"Finding {index} effective end cannot precede its start.");
        }

        var entities = finding.Entities ?? [];

        if (entities.Count > 20)
        {
            throw new ArgumentException(
                $"Finding {index} may contain at most 20 entities.");
        }

        foreach (var entity in entities)
        {
            if (string.IsNullOrWhiteSpace(entity.Kind) ||
                string.IsNullOrWhiteSpace(entity.CanonicalKey) ||
                string.IsNullOrWhiteSpace(entity.DisplayName) ||
                string.IsNullOrWhiteSpace(entity.Role))
            {
                throw new ArgumentException(
                    $"Finding {index} contains an incomplete entity reference.");
            }
        }

        var evidence = finding.Evidence ?? [];

        if (evidence.Count > 20)
        {
            throw new ArgumentException(
                $"Finding {index} may contain at most 20 evidence references.");
        }

        foreach (var evidenceRef in evidence)
        {
            if (string.IsNullOrWhiteSpace(evidenceRef.Type))
            {
                throw new ArgumentException(
                    $"Finding {index} contains evidence without a type.");
            }

            if (evidenceRef.Excerpt?.Length >
                _options.MaximumEvidenceExcerptLength)
            {
                throw new ArgumentException(
                    $"Finding {index} contains an evidence excerpt longer than " +
                    $"{_options.MaximumEvidenceExcerptLength} characters.");
            }
        }
    }

    private static string Normalize(string value) =>
        value.Trim().ToLowerInvariant();

    private static void ValidateSourceSystems(
        IReadOnlyList<string> sources,
        string operation)
    {
        if (sources.Count > 20)
        {
            throw new ArgumentException(
                $"{operation} accepts at most 20 source systems.");
        }

        if (sources.Any(source =>
                string.IsNullOrWhiteSpace(source) ||
                source.Length > 100 ||
                !SourceSystemPattern().IsMatch(source)))
        {
            throw new ArgumentException(
                $"{operation} source systems must be lowercase stable identifiers.");
        }
    }

    [GeneratedRegex(
        "^[a-z0-9]+(?:[._-][a-z0-9]+)*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex PredicatePattern();

    [GeneratedRegex(
        "^[a-z0-9]+(?:[._-][a-z0-9]+)*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex SourceSystemPattern();
}
