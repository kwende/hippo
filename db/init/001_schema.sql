\set ON_ERROR_STOP on

CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE EXTENSION IF NOT EXISTS vector;

CREATE SCHEMA IF NOT EXISTS hippo;

CREATE TABLE hippo.entity
(
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    kind            text NOT NULL CHECK (btrim(kind) <> ''),
    canonical_key   text NOT NULL CHECK (btrim(canonical_key) <> ''),
    normalized_key  text NOT NULL CHECK (btrim(normalized_key) <> ''),
    display_name    text NOT NULL CHECK (btrim(display_name) <> ''),
    attributes      jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at      timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT entity_kind_normalized_key_uq
        UNIQUE (kind, normalized_key)
);

CREATE TABLE hippo.entity_alias
(
    entity_id        uuid NOT NULL
                     REFERENCES hippo.entity(id) ON DELETE CASCADE,
    alias            text NOT NULL CHECK (btrim(alias) <> ''),
    normalized_alias text NOT NULL CHECK (btrim(normalized_alias) <> ''),

    PRIMARY KEY (entity_id, normalized_alias)
);

CREATE INDEX entity_alias_normalized_idx
    ON hippo.entity_alias(normalized_alias);

CREATE TABLE hippo.entity_relation
(
    id                  uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    source_entity_id    uuid NOT NULL
                        REFERENCES hippo.entity(id) ON DELETE CASCADE,
    relation            text NOT NULL CHECK (btrim(relation) <> ''),
    target_entity_id    uuid NOT NULL
                        REFERENCES hippo.entity(id) ON DELETE CASCADE,
    effective_from      timestamptz,
    effective_to        timestamptz,
    created_at          timestamptz NOT NULL DEFAULT now(),

    CHECK (source_entity_id <> target_entity_id),
    CHECK (effective_to IS NULL OR effective_from IS NULL
           OR effective_to >= effective_from)
);

CREATE UNIQUE INDEX entity_relation_identity_uq
    ON hippo.entity_relation
    (
        source_entity_id,
        relation,
        target_entity_id,
        coalesce(effective_from, '-infinity'::timestamptz)
    );

CREATE INDEX entity_relation_source_idx
    ON hippo.entity_relation(source_entity_id);

CREATE INDEX entity_relation_target_idx
    ON hippo.entity_relation(target_entity_id);

CREATE TABLE hippo.memory_item
(
    id                  uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    kind                text NOT NULL
                        CHECK (kind IN
                        (
                            'observation',
                            'event',
                            'hypothesis',
                            'conclusion',
                            'procedure'
                        )),
    predicate           text NOT NULL CHECK (btrim(predicate) <> ''),
    statement           text NOT NULL CHECK (btrim(statement) <> ''),
    value               jsonb,

    effective_from      timestamptz,
    effective_to        timestamptz,
    discovered_at       timestamptz NOT NULL,
    recorded_at         timestamptz NOT NULL DEFAULT now(),

    confidence          real NOT NULL
                        CHECK (confidence >= 0 AND confidence <= 1),
    status              text NOT NULL
                        CHECK (status IN
                        (
                            'observed',
                            'proposed',
                            'verified',
                            'superseded',
                            'contradicted',
                            'retracted'
                        )),

    principal_id        text NOT NULL CHECK (btrim(principal_id) <> ''),
    principal_name      text NOT NULL CHECK (btrim(principal_name) <> ''),
    agent_client        text NOT NULL CHECK (btrim(agent_client) <> ''),
    agent_model         text,
    source_session_id   text NOT NULL CHECK (btrim(source_session_id) <> ''),

    supersedes_id       uuid REFERENCES hippo.memory_item(id),
    dedupe_hash         bytea NOT NULL,

    search_document     tsvector GENERATED ALWAYS AS
    (
        to_tsvector
        (
            'english',
            coalesce(predicate, '') || ' ' ||
            coalesce(statement, '') || ' ' ||
            coalesce(value::text, '')
        )
    ) STORED,

    CHECK (effective_to IS NULL OR effective_from IS NULL
           OR effective_to >= effective_from),

    CONSTRAINT memory_item_dedupe_hash_uq UNIQUE (dedupe_hash)
);

CREATE INDEX memory_item_search_idx
    ON hippo.memory_item USING gin(search_document);

CREATE INDEX memory_item_effective_idx
    ON hippo.memory_item(effective_from DESC);

CREATE INDEX memory_item_status_kind_idx
    ON hippo.memory_item(status, kind);

CREATE INDEX memory_item_recorded_idx
    ON hippo.memory_item(recorded_at DESC);

CREATE TABLE hippo.memory_item_entity
(
    memory_item_id  uuid NOT NULL
                    REFERENCES hippo.memory_item(id) ON DELETE CASCADE,
    entity_id       uuid NOT NULL
                    REFERENCES hippo.entity(id) ON DELETE CASCADE,
    role            text NOT NULL CHECK (btrim(role) <> ''),

    PRIMARY KEY (memory_item_id, entity_id, role)
);

CREATE INDEX memory_item_entity_entity_idx
    ON hippo.memory_item_entity(entity_id, memory_item_id);

CREATE TABLE hippo.memory_evidence
(
    id                  uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    memory_item_id      uuid NOT NULL
                        REFERENCES hippo.memory_item(id) ON DELETE CASCADE,
    evidence_type       text NOT NULL CHECK (btrim(evidence_type) <> ''),
    source_reference    text,
    source_excerpt      text,
    source_hash         text,
    observed_at         timestamptz,
    attached_at         timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX memory_evidence_identity_uq
    ON hippo.memory_evidence
    (
        memory_item_id,
        evidence_type,
        coalesce(source_reference, ''),
        coalesce(source_hash, '')
    );

CREATE INDEX memory_evidence_memory_idx
    ON hippo.memory_evidence(memory_item_id);

CREATE TABLE hippo.tool_call_audit
(
    id                  bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    occurred_at         timestamptz NOT NULL DEFAULT now(),
    principal_id        text NOT NULL,
    principal_name      text NOT NULL,
    agent_client        text NOT NULL,
    tool_name           text NOT NULL,
    source_session_id   text,
    request_summary     jsonb NOT NULL DEFAULT '{}'::jsonb,
    result_count        integer NOT NULL DEFAULT 0,
    memory_ids          uuid[] NOT NULL DEFAULT '{}'::uuid[],
    duration_ms         integer NOT NULL,
    succeeded           boolean NOT NULL,
    error_code          text
);

CREATE INDEX tool_call_audit_occurred_idx
    ON hippo.tool_call_audit(occurred_at DESC);

CREATE INDEX tool_call_audit_principal_idx
    ON hippo.tool_call_audit(principal_id, occurred_at DESC);

CREATE VIEW hippo.memory_activity_summary AS
SELECT
    date_trunc('day', occurred_at) AS day,
    principal_id,
    principal_name,
    agent_client,
    tool_name,
    count(*) AS calls,
    sum(result_count) AS returned_or_recorded_items,
    round(avg(duration_ms), 1) AS average_duration_ms,
    count(*) FILTER (WHERE succeeded) AS successful_calls,
    count(*) FILTER (WHERE NOT succeeded) AS failed_calls
FROM hippo.tool_call_audit
GROUP BY
    date_trunc('day', occurred_at),
    principal_id,
    principal_name,
    agent_client,
    tool_name;

COMMENT ON SCHEMA hippo IS
    'Shared evidence-backed operational memory for MCP-capable agents.';

COMMENT ON TABLE hippo.memory_item IS
    'Append-only claims. Corrections should create new records and lifecycle links.';
