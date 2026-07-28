using System.Data;
using System.Diagnostics;
using System.Text.Json;
using Hippo.Server.Authentication;
using Hippo.Server.Configuration;
using Hippo.Server.Contracts;
using Hippo.Server.Services;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Hippo.Server.Data;

public sealed class MemoryRepository(
    NpgsqlDataSource dataSource,
    FindingCanonicalizer canonicalizer,
    FindingValidator validator,
    IOptions<HippoOptions> options,
    ILogger<MemoryRepository> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HippoOptions _options = options.Value;

    public async Task<RecallResult> RecallAsync(
        CallerIdentity caller,
        RecallRequest request,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        validator.Validate(request);

        var normalizedIdentifiers = request.EntityIdentifiers
            .Where(identifier => !string.IsNullOrWhiteSpace(identifier))
            .Select(Normalize)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        try
        {
            await using var connection =
                await dataSource.OpenConnectionAsync(cancellationToken);

            var matchedEntities = await ResolveEntitiesAsync(
                connection,
                normalizedIdentifiers,
                cancellationToken);

            if (matchedEntities.Count == 0)
            {
                var warnings = new[]
                {
                    "No supplied identifier currently resolves to a Hippo entity."
                };

                await WriteAuditAsync(
                    caller,
                    "recall_operational_context",
                    null,
                    new
                    {
                        identifiers = normalizedIdentifiers,
                        concepts = request.Concepts
                    },
                    0,
                    [],
                    stopwatch.ElapsedMilliseconds,
                    true,
                    null,
                    cancellationToken);

                return new RecallResult
                {
                    MatchedEntities = [],
                    Findings = [],
                    Warnings = warnings
                };
            }

            var maximumResults = Math.Clamp(
                request.MaximumResults ?? _options.MaximumRecallResults,
                1,
                _options.MaximumRecallResults);

            var findings = await QueryFindingsAsync(
                connection,
                matchedEntities.Select(entity => entity.Id).ToArray(),
                request,
                maximumResults,
                cancellationToken);

            await WriteAuditAsync(
                caller,
                "recall_operational_context",
                null,
                new
                {
                    identifiers = normalizedIdentifiers,
                    concepts = request.Concepts,
                    request.TimeRangeStart,
                    request.TimeRangeEnd
                },
                findings.Count,
                findings.Select(finding => finding.Id).ToArray(),
                stopwatch.ElapsedMilliseconds,
                true,
                null,
                cancellationToken);

            return new RecallResult
            {
                MatchedEntities = matchedEntities,
                Findings = findings,
                Warnings = []
            };
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Operational-memory recall failed for {PrincipalId}.",
                caller.PrincipalId);

            await TryWriteFailedAuditAsync(
                caller,
                "recall_operational_context",
                null,
                new
                {
                    identifiers = normalizedIdentifiers,
                    concepts = request.Concepts
                },
                stopwatch.ElapsedMilliseconds,
                exception,
                cancellationToken);

            throw;
        }
    }

    public async Task<RecordFindingsResult> RecordAsync(
        CallerIdentity caller,
        RecordFindingsRequest request,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        validator.Validate(request);

        var outcomes = new List<RecordFindingOutcome>();
        var warnings = new List<string>();

        try
        {
            await using var connection =
                await dataSource.OpenConnectionAsync(cancellationToken);

            await using var transaction =
                await connection.BeginTransactionAsync(cancellationToken);

            for (var index = 0; index < request.Findings.Count; index++)
            {
                var finding = request.Findings[index];
                var entityLinks = new List<(Guid Id, string Role)>();

                foreach (var entity in finding.Entities)
                {
                    var entityId = await GetOrCreateEntityAsync(
                        connection,
                        transaction,
                        entity,
                        cancellationToken);

                    entityLinks.Add((entityId, Normalize(entity.Role)));
                }

                var hash = canonicalizer.ComputeHash(finding);
                var proposedStatus = DetermineInitialStatus(finding);

                var insertResult = await InsertOrFindMemoryAsync(
                    connection,
                    transaction,
                    caller,
                    request,
                    finding,
                    proposedStatus,
                    hash,
                    cancellationToken);

                foreach (var entityLink in entityLinks.Distinct())
                {
                    await LinkEntityAsync(
                        connection,
                        transaction,
                        insertResult.MemoryId,
                        entityLink.Id,
                        entityLink.Role,
                        cancellationToken);
                }

                var evidenceAttached = 0;

                foreach (var evidence in finding.Evidence)
                {
                    evidenceAttached += await AttachEvidenceAsync(
                        connection,
                        transaction,
                        insertResult.MemoryId,
                        evidence,
                        cancellationToken);
                }

                outcomes.Add(new RecordFindingOutcome(
                    index,
                    insertResult.MemoryId,
                    insertResult.Duplicate,
                    insertResult.Status,
                    evidenceAttached));
            }

            await transaction.CommitAsync(cancellationToken);

            await WriteAuditAsync(
                caller,
                "record_operational_findings",
                request.SourceSessionId,
                new
                {
                    findingCount = request.Findings.Count,
                    kinds = request.Findings
                        .Select(finding => Normalize(finding.Kind))
                        .Distinct()
                },
                outcomes.Count,
                outcomes.Select(outcome => outcome.MemoryId).ToArray(),
                stopwatch.ElapsedMilliseconds,
                true,
                null,
                cancellationToken);

            return new RecordFindingsResult
            {
                Outcomes = outcomes,
                Warnings = warnings
            };
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Operational-memory write failed for {PrincipalId}.",
                caller.PrincipalId);

            await TryWriteFailedAuditAsync(
                caller,
                "record_operational_findings",
                request.SourceSessionId,
                new
                {
                    findingCount = request.Findings.Count
                },
                stopwatch.ElapsedMilliseconds,
                exception,
                cancellationToken);

            throw;
        }
    }

    private static async Task<IReadOnlyList<ResolvedEntity>> ResolveEntitiesAsync(
        NpgsqlConnection connection,
        IReadOnlyCollection<string> normalizedIdentifiers,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT DISTINCT
                e.id,
                e.kind,
                e.canonical_key,
                e.display_name
            FROM hippo.entity AS e
            LEFT JOIN hippo.entity_alias AS a
                ON a.entity_id = e.id
            WHERE
                e.normalized_key = ANY (@identifiers)
                OR a.normalized_alias = ANY (@identifiers)
            ORDER BY e.kind, e.canonical_key;
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add(
            new NpgsqlParameter<string[]>("identifiers", normalizedIdentifiers.ToArray())
            {
                NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text
            });

        var results = new List<ResolvedEntity>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ResolvedEntity(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)));
        }

        return results;
    }

    private static async Task<IReadOnlyList<RecalledFinding>> QueryFindingsAsync(
        NpgsqlConnection connection,
        Guid[] matchedEntityIds,
        RecallRequest request,
        int maximumResults,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            WITH expanded_entities AS
            (
                SELECT id, true AS direct
                FROM unnest(@entity_ids::uuid[]) AS ids(id)

                UNION

                SELECT relation.target_entity_id, false
                FROM hippo.entity_relation AS relation
                WHERE relation.source_entity_id = ANY (@entity_ids)

                UNION

                SELECT relation.source_entity_id, false
                FROM hippo.entity_relation AS relation
                WHERE relation.target_entity_id = ANY (@entity_ids)
            ),
            candidates AS
            (
                SELECT
                    memory.id,
                    bool_or(expanded.direct) AS direct_match,
                    CASE
                        WHEN @query = '' THEN 0::real
                        ELSE ts_rank_cd
                        (
                            memory.search_document,
                            websearch_to_tsquery('english', @query)
                        )
                    END AS lexical_score
                FROM hippo.memory_item AS memory
                JOIN hippo.memory_item_entity AS link
                    ON link.memory_item_id = memory.id
                JOIN expanded_entities AS expanded
                    ON expanded.id = link.entity_id
                WHERE
                    memory.status <> 'retracted'
                    AND
                    (
                        @time_start IS NULL
                        OR coalesce(
                            memory.effective_to,
                            'infinity'::timestamptz
                        ) >= @time_start
                    )
                    AND
                    (
                        @time_end IS NULL
                        OR coalesce(
                            memory.effective_from,
                            '-infinity'::timestamptz
                        ) <= @time_end
                    )
                GROUP BY memory.id
            )
            SELECT
                memory.id,
                memory.kind,
                memory.predicate,
                memory.statement,
                memory.value::text,
                memory.effective_from,
                memory.effective_to,
                memory.discovered_at,
                memory.recorded_at,
                memory.confidence,
                memory.status,
                memory.principal_name,
                memory.agent_client,
                coalesce
                (
                    (
                        SELECT jsonb_agg
                        (
                            jsonb_build_object
                            (
                                'kind', entity.kind,
                                'canonicalKey', entity.canonical_key,
                                'displayName', entity.display_name,
                                'role', link.role
                            )
                            ORDER BY entity.kind, entity.canonical_key, link.role
                        )
                        FROM hippo.memory_item_entity AS link
                        JOIN hippo.entity AS entity
                            ON entity.id = link.entity_id
                        WHERE link.memory_item_id = memory.id
                    ),
                    '[]'::jsonb
                )::text AS entities_json,
                coalesce
                (
                    (
                        SELECT jsonb_agg
                        (
                            jsonb_build_object
                            (
                                'type', evidence.evidence_type,
                                'sourceReference', evidence.source_reference,
                                'excerpt', evidence.source_excerpt,
                                'sha256', evidence.source_hash,
                                'observedAt', evidence.observed_at
                            )
                            ORDER BY evidence.attached_at, evidence.id
                        )
                        FROM hippo.memory_evidence AS evidence
                        WHERE evidence.memory_item_id = memory.id
                    ),
                    '[]'::jsonb
                )::text AS evidence_json
            FROM candidates
            JOIN hippo.memory_item AS memory
                ON memory.id = candidates.id
            ORDER BY
                candidates.direct_match DESC,
                candidates.lexical_score DESC,
                CASE memory.status
                    WHEN 'verified' THEN 0
                    WHEN 'observed' THEN 1
                    WHEN 'proposed' THEN 2
                    WHEN 'contradicted' THEN 3
                    WHEN 'superseded' THEN 4
                    ELSE 5
                END,
                memory.confidence DESC,
                coalesce(memory.effective_from, memory.discovered_at) DESC
            LIMIT @maximum_results;
            """;

        await using var command = new NpgsqlCommand(sql, connection);

        command.Parameters.Add(
            new NpgsqlParameter<Guid[]>("entity_ids", matchedEntityIds)
            {
                NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Uuid
            });

        command.Parameters.AddWithValue(
            "query",
            string.Join(
                ' ',
                request.Concepts.Where(concept =>
                    !string.IsNullOrWhiteSpace(concept))));

        command.Parameters.Add(
            new NpgsqlParameter("time_start", NpgsqlDbType.TimestampTz)
            {
                Value = (object?)request.TimeRangeStart ?? DBNull.Value
            });

        command.Parameters.Add(
            new NpgsqlParameter("time_end", NpgsqlDbType.TimestampTz)
            {
                Value = (object?)request.TimeRangeEnd ?? DBNull.Value
            });

        command.Parameters.AddWithValue("maximum_results", maximumResults);

        var findings = new List<RecalledFinding>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            findings.Add(new RecalledFinding
            {
                Id = reader.GetGuid(0),
                Kind = reader.GetString(1),
                Predicate = reader.GetString(2),
                Statement = reader.GetString(3),
                Value = reader.IsDBNull(4)
                    ? null
                    : ParseElement(reader.GetString(4)),
                EffectiveFrom = reader.IsDBNull(5)
                    ? null
                    : reader.GetFieldValue<DateTimeOffset>(5),
                EffectiveTo = reader.IsDBNull(6)
                    ? null
                    : reader.GetFieldValue<DateTimeOffset>(6),
                DiscoveredAt = reader.GetFieldValue<DateTimeOffset>(7),
                RecordedAt = reader.GetFieldValue<DateTimeOffset>(8),
                Confidence = reader.GetFloat(9),
                Status = reader.GetString(10),
                PrincipalName = reader.GetString(11),
                AgentClient = reader.GetString(12),
                Entities =
                    JsonSerializer.Deserialize<List<RelatedEntity>>(
                        reader.GetString(13),
                        JsonOptions)
                    ?? [],
                Evidence =
                    JsonSerializer.Deserialize<List<EvidenceReference>>(
                        reader.GetString(14),
                        JsonOptions)
                    ?? []
            });
        }

        return findings;
    }

    private static async Task<Guid> GetOrCreateEntityAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        EntityReference entity,
        CancellationToken cancellationToken)
    {
        const string insertEntitySql =
            """
            INSERT INTO hippo.entity
            (
                kind,
                canonical_key,
                normalized_key,
                display_name
            )
            VALUES
            (
                @kind,
                @canonical_key,
                @normalized_key,
                @display_name
            )
            ON CONFLICT (kind, normalized_key)
            DO UPDATE SET
                display_name = CASE
                    WHEN length(EXCLUDED.display_name) >
                         length(hippo.entity.display_name)
                    THEN EXCLUDED.display_name
                    ELSE hippo.entity.display_name
                END
            RETURNING id;
            """;

        var normalizedKind = Normalize(entity.Kind);
        var normalizedKey = Normalize(entity.CanonicalKey);

        await using var command =
            new NpgsqlCommand(insertEntitySql, connection, transaction);

        command.Parameters.AddWithValue("kind", normalizedKind);
        command.Parameters.AddWithValue(
            "canonical_key",
            entity.CanonicalKey.Trim());
        command.Parameters.AddWithValue("normalized_key", normalizedKey);
        command.Parameters.AddWithValue(
            "display_name",
            entity.DisplayName.Trim());

        var entityId = (Guid)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new DataException("Entity insert did not return an ID."));

        var aliases = entity.Aliases
            .Append(entity.CanonicalKey)
            .Append(entity.DisplayName)
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Select(alias => new
            {
                Alias = alias.Trim(),
                Normalized = Normalize(alias)
            })
            .DistinctBy(alias => alias.Normalized)
            .ToArray();

        const string insertAliasSql =
            """
            INSERT INTO hippo.entity_alias
            (
                entity_id,
                alias,
                normalized_alias
            )
            VALUES
            (
                @entity_id,
                @alias,
                @normalized_alias
            )
            ON CONFLICT DO NOTHING;
            """;

        foreach (var alias in aliases)
        {
            await using var aliasCommand =
                new NpgsqlCommand(insertAliasSql, connection, transaction);

            aliasCommand.Parameters.AddWithValue("entity_id", entityId);
            aliasCommand.Parameters.AddWithValue("alias", alias.Alias);
            aliasCommand.Parameters.AddWithValue(
                "normalized_alias",
                alias.Normalized);

            await aliasCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        return entityId;
    }

    private static async Task<MemoryInsertResult> InsertOrFindMemoryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CallerIdentity caller,
        RecordFindingsRequest request,
        ProposedFinding finding,
        string initialStatus,
        byte[] hash,
        CancellationToken cancellationToken)
    {
        const string insertSql =
            """
            INSERT INTO hippo.memory_item
            (
                kind,
                predicate,
                statement,
                value,
                effective_from,
                effective_to,
                discovered_at,
                confidence,
                status,
                principal_id,
                principal_name,
                agent_client,
                agent_model,
                source_session_id,
                dedupe_hash
            )
            VALUES
            (
                @kind,
                @predicate,
                @statement,
                @value,
                @effective_from,
                @effective_to,
                @discovered_at,
                @confidence,
                @status,
                @principal_id,
                @principal_name,
                @agent_client,
                @agent_model,
                @source_session_id,
                @dedupe_hash
            )
            ON CONFLICT (dedupe_hash) DO NOTHING
            RETURNING id, status;
            """;

        await using var insertCommand =
            new NpgsqlCommand(insertSql, connection, transaction);

        insertCommand.Parameters.AddWithValue(
            "kind",
            Normalize(finding.Kind));

        insertCommand.Parameters.AddWithValue(
            "predicate",
            Normalize(finding.Predicate));

        insertCommand.Parameters.AddWithValue(
            "statement",
            finding.Statement.Trim());

        insertCommand.Parameters.Add(
            new NpgsqlParameter("value", NpgsqlDbType.Jsonb)
            {
                Value = finding.Value is { } value
                    ? value.GetRawText()
                    : DBNull.Value
            });

        insertCommand.Parameters.Add(
            new NpgsqlParameter("effective_from", NpgsqlDbType.TimestampTz)
            {
                Value = (object?)finding.EffectiveFrom ?? DBNull.Value
            });

        insertCommand.Parameters.Add(
            new NpgsqlParameter("effective_to", NpgsqlDbType.TimestampTz)
            {
                Value = (object?)finding.EffectiveTo ?? DBNull.Value
            });

        insertCommand.Parameters.AddWithValue(
            "discovered_at",
            finding.DiscoveredAt);

        insertCommand.Parameters.AddWithValue(
            "confidence",
            finding.Confidence);

        insertCommand.Parameters.AddWithValue("status", initialStatus);
        insertCommand.Parameters.AddWithValue(
            "principal_id",
            caller.PrincipalId);
        insertCommand.Parameters.AddWithValue(
            "principal_name",
            caller.DisplayName);
        insertCommand.Parameters.AddWithValue(
            "agent_client",
            caller.ClientName);

        insertCommand.Parameters.Add(
            new NpgsqlParameter("agent_model", NpgsqlDbType.Text)
            {
                Value = (object?)request.AgentModel ?? DBNull.Value
            });

        insertCommand.Parameters.AddWithValue(
            "source_session_id",
            request.SourceSessionId.Trim());

        insertCommand.Parameters.Add(
            new NpgsqlParameter("dedupe_hash", NpgsqlDbType.Bytea)
            {
                Value = hash
            });

        await using (var reader =
                     await insertCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                return new MemoryInsertResult(
                    reader.GetGuid(0),
                    false,
                    reader.GetString(1));
            }
        }

        const string existingSql =
            """
            SELECT id, status
            FROM hippo.memory_item
            WHERE dedupe_hash = @dedupe_hash;
            """;

        await using var existingCommand =
            new NpgsqlCommand(existingSql, connection, transaction);

        existingCommand.Parameters.Add(
            new NpgsqlParameter("dedupe_hash", NpgsqlDbType.Bytea)
            {
                Value = hash
            });

        await using var existingReader =
            await existingCommand.ExecuteReaderAsync(cancellationToken);

        if (!await existingReader.ReadAsync(cancellationToken))
        {
            throw new DataException(
                "A duplicate memory was detected but could not be retrieved.");
        }

        return new MemoryInsertResult(
            existingReader.GetGuid(0),
            true,
            existingReader.GetString(1));
    }

    private static async Task LinkEntityAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid memoryId,
        Guid entityId,
        string role,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            INSERT INTO hippo.memory_item_entity
            (
                memory_item_id,
                entity_id,
                role
            )
            VALUES
            (
                @memory_id,
                @entity_id,
                @role
            )
            ON CONFLICT DO NOTHING;
            """;

        await using var command =
            new NpgsqlCommand(sql, connection, transaction);

        command.Parameters.AddWithValue("memory_id", memoryId);
        command.Parameters.AddWithValue("entity_id", entityId);
        command.Parameters.AddWithValue("role", role);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> AttachEvidenceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid memoryId,
        EvidenceReference evidence,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            INSERT INTO hippo.memory_evidence
            (
                memory_item_id,
                evidence_type,
                source_reference,
                source_excerpt,
                source_hash,
                observed_at
            )
            VALUES
            (
                @memory_id,
                @evidence_type,
                @source_reference,
                @source_excerpt,
                @source_hash,
                @observed_at
            )
            ON CONFLICT DO NOTHING;
            """;

        await using var command =
            new NpgsqlCommand(sql, connection, transaction);

        command.Parameters.AddWithValue("memory_id", memoryId);
        command.Parameters.AddWithValue(
            "evidence_type",
            Normalize(evidence.Type));

        command.Parameters.Add(
            new NpgsqlParameter("source_reference", NpgsqlDbType.Text)
            {
                Value = (object?)evidence.SourceReference?.Trim()
                    ?? DBNull.Value
            });

        command.Parameters.Add(
            new NpgsqlParameter("source_excerpt", NpgsqlDbType.Text)
            {
                Value = (object?)evidence.Excerpt?.Trim()
                    ?? DBNull.Value
            });

        command.Parameters.Add(
            new NpgsqlParameter("source_hash", NpgsqlDbType.Text)
            {
                Value = (object?)evidence.Sha256?.Trim()
                    ?? DBNull.Value
            });

        command.Parameters.Add(
            new NpgsqlParameter("observed_at", NpgsqlDbType.TimestampTz)
            {
                Value = (object?)evidence.ObservedAt ?? DBNull.Value
            });

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task WriteAuditAsync(
        CallerIdentity caller,
        string toolName,
        string? sourceSessionId,
        object requestSummary,
        int resultCount,
        Guid[] memoryIds,
        long durationMilliseconds,
        bool succeeded,
        string? errorCode,
        CancellationToken cancellationToken)
    {
        const string sql =
            """
            INSERT INTO hippo.tool_call_audit
            (
                principal_id,
                principal_name,
                agent_client,
                tool_name,
                source_session_id,
                request_summary,
                result_count,
                memory_ids,
                duration_ms,
                succeeded,
                error_code
            )
            VALUES
            (
                @principal_id,
                @principal_name,
                @agent_client,
                @tool_name,
                @source_session_id,
                @request_summary,
                @result_count,
                @memory_ids,
                @duration_ms,
                @succeeded,
                @error_code
            );
            """;

        await using var command = dataSource.CreateCommand(sql);

        command.Parameters.AddWithValue(
            "principal_id",
            caller.PrincipalId);
        command.Parameters.AddWithValue(
            "principal_name",
            caller.DisplayName);
        command.Parameters.AddWithValue(
            "agent_client",
            caller.ClientName);
        command.Parameters.AddWithValue("tool_name", toolName);

        command.Parameters.Add(
            new NpgsqlParameter("source_session_id", NpgsqlDbType.Text)
            {
                Value = (object?)sourceSessionId ?? DBNull.Value
            });

        command.Parameters.Add(
            new NpgsqlParameter("request_summary", NpgsqlDbType.Jsonb)
            {
                Value = JsonSerializer.Serialize(requestSummary)
            });

        command.Parameters.AddWithValue("result_count", resultCount);

        command.Parameters.Add(
            new NpgsqlParameter<Guid[]>("memory_ids", memoryIds)
            {
                NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Uuid
            });

        command.Parameters.AddWithValue(
            "duration_ms",
            checked((int)Math.Min(durationMilliseconds, int.MaxValue)));

        command.Parameters.AddWithValue("succeeded", succeeded);

        command.Parameters.Add(
            new NpgsqlParameter("error_code", NpgsqlDbType.Text)
            {
                Value = (object?)errorCode ?? DBNull.Value
            });

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task TryWriteFailedAuditAsync(
        CallerIdentity caller,
        string toolName,
        string? sourceSessionId,
        object requestSummary,
        long durationMilliseconds,
        Exception exception,
        CancellationToken cancellationToken)
    {
        try
        {
            await WriteAuditAsync(
                caller,
                toolName,
                sourceSessionId,
                requestSummary,
                0,
                [],
                durationMilliseconds,
                false,
                exception.GetType().Name,
                cancellationToken);
        }
        catch (Exception auditException)
        {
            logger.LogWarning(
                auditException,
                "Failed to write the failure audit record for {ToolName}.",
                toolName);
        }
    }

    private static string DetermineInitialStatus(ProposedFinding finding)
    {
        var kind = Normalize(finding.Kind);

        return kind is "observation" or "event" &&
               finding.Evidence.Count > 0
            ? "observed"
            : "proposed";
    }

    private static JsonElement ParseElement(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string Normalize(string value) =>
        string.Join(
            ' ',
            value.Trim()
                .ToLowerInvariant()
                .Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries));

    private sealed record MemoryInsertResult(
        Guid MemoryId,
        bool Duplicate,
        string Status);
}
