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
        if (request.EntityIdentifiers.Count is < 1 or > 50)
        {
            throw new ArgumentException(
                "Recall requires between 1 and 50 entity identifiers.");
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

        if (request.Findings.Count > _options.MaximumFindingsPerWrite)
        {
            throw new ArgumentException(
                $"A write may contain at most " +
                $"{_options.MaximumFindingsPerWrite} findings.");
        }

        for (var index = 0; index < request.Findings.Count; index++)
        {
            Validate(request.Findings[index], index);
        }
    }

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

        if (finding.Entities.Count is < 1 or > 20)
        {
            throw new ArgumentException(
                $"Finding {index} requires between 1 and 20 entities.");
        }

        foreach (var entity in finding.Entities)
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

        if (finding.Evidence.Count > 20)
        {
            throw new ArgumentException(
                $"Finding {index} may contain at most 20 evidence references.");
        }

        foreach (var evidence in finding.Evidence)
        {
            if (string.IsNullOrWhiteSpace(evidence.Type))
            {
                throw new ArgumentException(
                    $"Finding {index} contains evidence without a type.");
            }

            if (evidence.Excerpt?.Length >
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

    [GeneratedRegex(
        "^[a-z0-9]+(?:[._-][a-z0-9]+)*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex PredicatePattern();
}
