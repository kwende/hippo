using System.Text.Json;
using Hippo.Server.Contracts;
using Hippo.Server.Services;

namespace Hippo.Tests;

public sealed class FindingCanonicalizerTests
{
    private readonly FindingCanonicalizer _canonicalizer = new();

    [Fact]
    public void Hash_ignores_json_property_and_entity_order()
    {
        var first = CreateFinding(
            """{"baselineMs":7.2,"debugMs":86.4}""",
            [
                Entity("machine", "RC1234", "subject"),
                Entity("software_version", "4.18.2", "software")
            ]);

        var second = CreateFinding(
            """{"debugMs":86.4,"baselineMs":7.2}""",
            [
                Entity("software_version", "4.18.2", "software"),
                Entity("machine", "rc1234", "subject")
            ]);

        Assert.Equal(
            _canonicalizer.ComputeHash(first),
            _canonicalizer.ComputeHash(second));
    }

    [Fact]
    public void Hash_changes_when_structured_value_changes()
    {
        var first = CreateFinding(
            """{"freePercent":1.8}""",
            [Entity("machine", "rc1234", "subject")]);

        var second = CreateFinding(
            """{"freePercent":2.0}""",
            [Entity("machine", "rc1234", "subject")]);

        Assert.NotEqual(
            _canonicalizer.ComputeHash(first),
            _canonicalizer.ComputeHash(second));
    }

    [Fact]
    public void Hash_does_not_depend_on_statement_wording()
    {
        var first = CreateFinding(
            """{"freePercent":1.8}""",
            [Entity("machine", "rc1234", "subject")]);

        var second = first with
        {
            Statement =
                "The root volume was 98.2 percent full."
        };

        Assert.Equal(
            _canonicalizer.ComputeHash(first),
            _canonicalizer.ComputeHash(second));
    }

    private static ProposedFinding CreateFinding(
        string valueJson,
        IReadOnlyList<EntityReference> entities)
    {
        using var value = JsonDocument.Parse(valueJson);

        return new ProposedFinding
        {
            Kind = "observation",
            Predicate = "storage.root.free_percent",
            Statement = "The root volume had 1.8 percent free space.",
            Value = value.RootElement.Clone(),
            Entities = entities,
            Evidence =
            [
                new EvidenceReference
                {
                    Type = "log",
                    SourceReference = "log://rc1234/2026-08-01#8211"
                }
            ],
            EffectiveFrom =
                DateTimeOffset.Parse("2026-08-01T14:03:00Z"),
            DiscoveredAt =
                DateTimeOffset.Parse("2026-08-02T09:00:00Z"),
            Confidence = 1
        };
    }

    private static EntityReference Entity(
        string kind,
        string key,
        string role) =>
        new()
        {
            Kind = kind,
            CanonicalKey = key,
            DisplayName = key,
            Role = role
        };
}
