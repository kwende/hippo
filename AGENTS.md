# Hippo agent instructions

## Purpose

Hippo is a shared, evidence-backed operational memory service used by multiple
coding agents. The human should not have to explicitly search or maintain it
during normal diagnostic work.

## Ambient recall

When a task involves logs, telemetry, source code, or diagnostic material for an
identifiable machine, customer, site, incident, software version, hardware
revision, or component:

1. Identify the relevant entity identifiers and approximate time range.
2. Call `recall_operational_context` before finalizing conclusions.
3. If analysis reveals important new concepts or identifiers, recall once more
   with that narrower context.
4. Treat recalled items as evidence, not unquestionable truth.
5. Prefer verified and observed items over proposed hypotheses.
6. Do not mention routine recall to the user unless asked.

## Memory consolidation

After reaching a durable, evidence-backed finding:

1. Call `record_operational_findings`.
2. Store claims, not entire conversations or raw logs.
3. Distinguish observations, events, hypotheses, conclusions, and procedures.
4. Include entity identifiers, effective time, discovery time, confidence, and
   compact evidence references.
5. Mark speculative explanations as hypotheses.
6. Do not store credentials, personal secrets, ordinary plans, transient
   debugging chatter, or unsupported speculation.
7. A diagnostic session should usually produce zero to a handful of memories,
   not a transcript-shaped landfill.
8. Do not narrate routine recording unless asked.

## Engineering

- Keep the MCP surface small.
- PostgreSQL is authoritative storage.
- Prefer exact relational retrieval before embeddings.
- Writes are append-only; future correction should supersede or contradict
  rather than mutate history.
- Preserve human principal, agent client, source session, and evidence.
- Add tests for changes to canonicalization, validation, SQL, or tool contracts.
