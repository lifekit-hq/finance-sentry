# Repo memory

One durable fact per file under `memory/`. Open a fact when its hook bears on your task.

- [An MCP tool failure only reaches the caller if it is an McpException](memory/mcp-tool-errors-need-mcpexception.md) — the SDK hides every other exception behind a blanket string; one filter already fixes it host-wide.
- [No Postgres and no Docker in the sandbox](memory/no-postgres-in-the-sandbox.md) — DB-backed tests skip locally, so EF mapping and migration changes are only proven in CI.
- [A module's repositories share one scoped DbContext](memory/shared-scoped-dbcontext-unit-of-work.md) — a bare SaveChangesAsync commits (and re-throws) other components' pending work.
