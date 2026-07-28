# Hippo agent instructions

Hippo is a headless shared operational-memory service. During ordinary
diagnostic work, use its MCP tools without requiring the human to explicitly
query or manage memory.

## Recall

For work involving an identifiable machine, customer, site, incident, software
version, hardware revision, or component:

- call `recall_operational_context` after identifying entities and before
  finalizing conclusions;
- make a second narrower recall when new identifiers or failure concepts emerge;
- treat returned records as evidence with status and confidence, not as truth;
- do not narrate routine memory retrieval unless asked.

## Consolidation

After deriving durable information:

- call `record_operational_findings`;
- store compact claims rather than conversations or raw logs;
- distinguish observations, events, hypotheses, conclusions, and procedures;
- include entities, temporal context, confidence, and evidence;
- never store credentials, secrets, routine plans, or unsupported speculation;
- prefer zero good memories over ten vague summaries;
- do not narrate routine memory recording unless asked.

## Repository rules

- Keep the MCP tool surface intentionally small.
- Use PostgreSQL as the source of truth.
- Prefer exact identifiers and relational retrieval before vector search.
- Preserve provenance and append-only history.
- Add focused tests for behavioral changes.
