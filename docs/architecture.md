# Hippo architecture

## Goal

Hippo gives independently operated coding agents a shared operational memory without introducing a new human-facing application.

A developer asks Codex or Claude to inspect logs exactly as they do today. The agent quietly retrieves relevant prior context, performs the diagnosis, answers normally, and records only novel durable findings.

## Core loop

```text
Human request and logs
  -> agent identifies machine, time, and version
  -> recall_operational_context
  -> agent analyzes current evidence and recalled context
  -> optional narrower recall
  -> human-facing answer
  -> record_operational_findings
```

## Epistemic model

Hippo stores claims, not chat transcripts. Each finding contains entities, kind, predicate, value, statement, effective time, discovery time, confidence, status, evidence, and caller provenance.

Initial kinds are `observation`, `event`, `hypothesis`, `conclusion`, and `procedure`.

Initial statuses are `observed`, `proposed`, `verified`, `superseded`, `contradicted`, and `retracted`. The first write tool never self-verifies conclusions.

## Retrieval

Version zero intentionally avoids embeddings. Recall normalizes supplied identifiers, resolves canonical keys and aliases, expands one relationship hop, finds linked memories, scores full-text concept overlap, and ranks direct matches, status, confidence, and recency.

Machine IDs, software versions, dates, and hardware revisions are symbolic data. A vector database is not a superior serial-number index merely because it has more conference talks.

Embeddings can be added when measured misses demonstrate a semantic-recall need.

## Consolidation and deduplication

A finding is canonicalized from its kind, predicate, structured value, sorted entity links, effective-time bucket, and sorted evidence references. The SHA-256 digest is unique. Repeated submission returns the existing memory ID and may attach new evidence or entity links.

Wording is not part of the identity key. This prevents trivial paraphrases from multiplying into fake knowledge.

## Security boundary

The local prototype uses fixed bearer credentials mapped to a human principal and client identity. The credentials themselves are not persisted.

Every tool call records the principal, client, tool, request summary, result count, memory IDs, duration, and success state.

The application layer depends on a `CallerIdentity`, so a hosted deployment can replace the local authentication middleware without rewriting the repository or tools. Local ports bind only to `127.0.0.1`.

## Database ownership

PostgreSQL is authoritative. The schema contains entities and aliases, optional relationships, append-only memory items, entity links, evidence references, and tool-call audit rows. pgvector is installed but unused in the first slice.

## Non-goals for the first slice

- no web UI;
- no raw log ingestion;
- no conversation archival;
- no automatic fact verification;
- no model running inside the memory service;
- no Dynamics synchronization;
- no Azure deployment;
- no graph database;
- no embeddings.

The prototype should first establish whether stock Codex and Claude clients reliably invoke the tools and whether recall improves real diagnostic work.
