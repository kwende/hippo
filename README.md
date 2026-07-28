# Hippo

Hippo is a headless, shared operational-memory service for coding agents.

Codex, Claude, and other MCP-capable clients can quietly recall prior machine
context while diagnosing logs, then record compact evidence-backed findings for
later sessions. Humans keep using their normal coding clients; Hippo is the
shared hippocampus behind them.

## Status

This repository is an intentionally small proof of concept. The first vertical
slice provides:

- a stateless Streamable HTTP MCP server;
- PostgreSQL storage with pgvector available but not yet used;
- exact entity and alias resolution;
- one-hop entity-context expansion;
- full-text ranking;
- evidence-backed, append-only findings;
- deterministic deduplication;
- per-client API-key identity;
- tool-call audit records;
- Docker Compose for a Windows host running Linux containers.

It deliberately does **not** include a web UI, embeddings, autonomous
verification, Dynamics integration, or Azure infrastructure.

## Architecture

```text
Codex ───────┐
             ├── HTTP MCP ──> Hippo.Server ──> PostgreSQL
Claude ──────┘                   │
                                 └── audit trail
```

The two initial tools are:

- `recall_operational_context`
- `record_operational_findings`

The database stores claims rather than conversations. Every finding is linked to
entities, time, confidence, source session, human/client identity, and evidence.

See [docs/architecture.md](docs/architecture.md) for the model and design
constraints.

## Prerequisites

On Windows:

- Docker Desktop using Linux containers;
- Docker Compose v2;
- PowerShell 7 is convenient but not required.

A local .NET SDK is optional because the application builds inside Docker.
For non-container development, install the .NET 10 SDK.

## Start locally

Copy the environment template:

```powershell
Copy-Item .env.example .env
```

Generate three different secrets and place them in `.env`:

```powershell
1..3 | ForEach-Object {
    [Convert]::ToBase64String(
        [Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
    )
}
```

Start the service:

```powershell
docker compose up --build -d
```

Check it:

```powershell
Invoke-RestMethod http://localhost:8787/health/live
Invoke-RestMethod http://localhost:8787/health/ready
docker compose logs -f memory-api
```

The MCP endpoint is:

```text
http://localhost:8787/mcp
```

PostgreSQL is bound only to the local host at port `54329` for inspection.

```powershell
docker compose exec postgres `
  psql -U hippo -d hippo
```

Useful inspection queries:

```sql
SELECT * FROM hippo.memory_activity_summary
ORDER BY day DESC, principal_id, tool_name;

SELECT id, kind, predicate, statement, status, confidence, recorded_at
FROM hippo.memory_item
ORDER BY recorded_at DESC
LIMIT 50;
```

Database initialization scripts run only when the PostgreSQL volume is empty.
During this prototype, reset everything with:

```powershell
docker compose down -v
docker compose up --build -d
```

Do not use that reset procedure once the database contains anything you care
about. That sentence should be obvious, yet history suggests it deserves ink.

## Connect Codex

Set the token used by the Codex API-key entry in `.env`:

```powershell
[Environment]::SetEnvironmentVariable(
    "HIPPO_CODEX_TOKEN",
    "<same value as HIPPO_CODEX_TOKEN in .env>",
    "User"
)
```

Add this to `~/.codex/config.toml`:

```toml
[mcp_servers.hippo]
url = "http://localhost:8787/mcp"
bearer_token_env_var = "HIPPO_CODEX_TOKEN"
```

Restart Codex after setting the environment variable.

## Connect Claude Code

Set the Claude token:

```powershell
[Environment]::SetEnvironmentVariable(
    "HIPPO_CLAUDE_TOKEN",
    "<same value as HIPPO_CLAUDE_TOKEN in .env>",
    "User"
)
```

Add a user-scoped HTTP MCP server to `~/.claude.json`, or use equivalent
Claude Code configuration:

```json
{
  "mcpServers": {
    "hippo": {
      "type": "http",
      "url": "http://localhost:8787/mcp",
      "headers": {
        "Authorization": "Bearer ${HIPPO_CLAUDE_TOKEN}"
      },
      "alwaysLoad": true
    }
  }
}
```

`alwaysLoad` is useful here because Hippo exposes only two small tools and recall
should be available without a deferred tool-search step.

Use `/mcp` inside Claude Code to verify the connection.

## First acceptance test

1. Ask Claude to diagnose material concerning `rc1234`.
2. Confirm that `record_operational_findings` stores one durable finding.
3. Start a fresh Codex session and provide only `rc1234` plus different logs.
4. Confirm that Codex calls `recall_operational_context` and receives Claude's
   finding.
5. Have Codex record a second finding.
6. Repeat the same work and confirm deduplication returns the existing memory ID
   rather than creating another prose-shaped clone.

The proof of concept succeeds only if the agents invoke the tools naturally and
the retrieved context helps. A database containing facts is not, by itself, a
product. It is merely a database with ambitions.

## Development

```powershell
dotnet restore Hippo.sln
dotnet build Hippo.sln
dotnet test Hippo.sln
```

The GitHub Actions workflow also builds the application image and validates the
Compose configuration.

## Security boundary

The local Compose file binds both exposed ports to `127.0.0.1`. Do not change
that to `0.0.0.0` merely so another machine can connect. For cross-machine use,
put Hippo behind TLS and an authenticated tunnel, VPN, or internal ingress.

API keys are a prototype identity mechanism. The service code depends only on a
`CallerIdentity`; an Azure deployment can replace the middleware with Microsoft
Entra ID without rewriting the repository or tools.

## License

MIT
