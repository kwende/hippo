using Hippo.Server.Configuration;
using Hippo.Server.Contracts;
using Hippo.Server.Services;
using Microsoft.Extensions.Options;

namespace Hippo.Tests;

public sealed class FindingValidatorTests
{
    private readonly FindingValidator _validator = new(
        Options.Create(new HippoOptions()));

    [Fact]
    public void Rejects_interpretive_prose_as_a_predicate()
    {
        var request = ValidRequest() with
        {
            Findings =
            [
                ValidFinding() with
                {
                    Predicate = "Logging probably caused the slowdown"
                }
            ]
        };

        Assert.Throws<ArgumentException>(() => _validator.Validate(request));
    }

    [Fact]
    public void Rejects_more_than_the_write_cap()
    {
        var request = ValidRequest() with
        {
            Findings = Enumerable
                .Range(0, 11)
                .Select(_ => ValidFinding())
                .ToArray()
        };

        Assert.Throws<ArgumentException>(() => _validator.Validate(request));
    }

    [Fact]
    public void Accepts_a_small_evidence_backed_observation()
    {
        _validator.Validate(ValidRequest());
    }

    private static RecordFindingsRequest ValidRequest() =>
        new()
        {
            SourceSessionId = "session-123",
            Findings = [ValidFinding()]
        };

    private static ProposedFinding ValidFinding() =>
        new()
        {
            Kind = "observation",
            Predicate = "storage.root.free_percent",
            Statement = "The root volume had 1.8 percent free space.",
            Entities =
            [
                new EntityReference
                {
                    Kind = "machine",
                    CanonicalKey = "rc1234",
                    DisplayName = "rc1234",
                    Role = "subject"
                }
            ],
            Evidence =
            [
                new EvidenceReference
                {
                    Type = "log",
                    SourceReference = "log://rc1234/2026-08-01"
                }
            ],
            DiscoveredAt = DateTimeOffset.UtcNow,
            Confidence = 1
        };
}
