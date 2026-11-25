# Quickstart: Thesis Break Monitor

## Run the stack

```bash
cd docker && docker compose -f docker-compose.dev.yml up -d postgres api
# health: GET http://localhost:5001/api/v1/health -> {"status":"healthy"}
```

## Apply the migration

M004 reshapes `research.theses.invalidation_triggers` and backfills the DRAM/GRAB triggers.
Applied automatically on API startup (module migrator) or manually:

```bash
dotnet ef database update \
  --project backend/src/FinanceSentry.Modules.Research \
  --context ResearchDbContext
```

## Exercise via MCP

The two tools are served by `FinanceSentry.Mcp`. From an MCP client (Ledger, or the MCP inspector):

```
run_thesis_monitor            # → run summary, persists break-state
list_thesis_breaks            # → broken theses with full evidence
```

Or let the Hangfire recurring job `thesis-monitor` run on schedule (dashboard:
http://localhost:5001/hangfire → trigger it manually to test).

## Verify the golden path (spec Independent Test)

1. Seed/confirm a thesis with `gross_margin lessThan 0.35` proxy `MU`, 2 quarters.
2. Call `run_thesis_monitor`.
3. Assert: thesis `BrokenAt`/`BrokenReason` set; exactly one `ThesisBroken` alert exists.
4. Call `list_thesis_breaks` → the thesis appears with metric/observed/threshold/reason.
5. Call `run_thesis_monitor` again → no duplicate alert, `BrokenAt` unchanged.

## Unit-test the evaluator (Test-First)

```bash
dotnet test backend/tests/FinanceSentry.Modules.Research.Tests \
  --filter FullyQualifiedName~ThesisMonitor
```

Cases that MUST pass: consecutive-period breach, YoY (same fiscal period prior year), proxy-ticker
substitution, divide-by-zero → non-evaluable, insufficient periods → non-evaluable, price_drawdown
over N quarterly readings, idempotent re-run (no second alert), auto-clear on recovered data.

## Build gate

```bash
dotnet build backend/     # MUST be zero warnings before any task is complete
```
