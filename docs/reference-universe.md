# Reference universe

Stage A of the discovery funnel: the lists the funnel draws candidates from, held as data. Shared market
reference data (no per-user rows), in the `research` schema, refreshed weekly, read through one service and one
MCP tool. It exists so that finding opportunities does not depend on the owner naming tickers: the S&P 500, the
Nasdaq-100, every US-listed sector and industry, and the themes the owner cares about are all queryable, and each
ticker knows which lists it is on.

## Lists

| List key | Kind | Source dataset | Refreshed |
|---|---|---|---|
| `sp500` | index | [datasets/s-and-p-500-companies](https://github.com/datasets/s-and-p-500-companies) `data/constituents.csv` | weekly |
| `nasdaq100` | index | [unliftedq/index-constitution](https://github.com/unliftedq/index-constitution) `latest/nasdaq100.csv` | weekly |
| `sector:<name>` (11) and `industry:<name>` (~75) | sector, industry | [JerBouma/FinanceDatabase](https://github.com/JerBouma/FinanceDatabase) `database/equities/{NMS,NGM,NCM,NYQ,ASE}.csv` (delisted rows and rows without a sector dropped) | weekly |
| `theme:memory`, `theme:ai-infra`, `theme:power`, `theme:semiconductors`, `theme:energy` | theme | `ReferenceUniverse:Themes` in `appsettings.json` (declared as data; edit the config to add a theme or a member) | weekly (materialised into the table) |

Tickers are stored in Yahoo form (`BRK-B`, not `BRK.B`), the spelling the per-ticker bar ingestion resolves.

## Licences (confirmed 2026-11-13, before any dataset was wired in)

| Dataset | Licence | Evidence | Use here |
|---|---|---|---|
| S&P 500 constituents (`datasets/s-and-p-500-companies`) | **ODC-PDDL-1.0** (Open Data Commons Public Domain Dedication and Licence) for the data, MIT/BSD for its code. The GitHub repository record shows no licence; the licence is declared in the dataset's `datapackage.json` (`licenses`) and its README ("All data is licensed under the Open Data Commons Public Domain Dedication and License"). The upstream compiles the list from Wikipedia. | `datapackage.json`, `README.md` | Allowed: the list is read and stored; no credit required (credit to the Open Knowledge Foundation is appreciated). |
| Nasdaq-100 (`unliftedq/index-constitution`) | **MIT** (`LICENSE`, "Copyright (c) 2026 index-constitution contributors"). The repository states its membership data is compiled from Wikipedia's Nasdaq-100 article. | `LICENSE`, `README.md` | Allowed: the notice below is kept. |
| FinanceDatabase equities (`JerBouma/FinanceDatabase`) | **MIT** (`LICENSE`, "Copyright (c) 2023 Jeroen Bouma"); the repository ships the CSVs under that licence and states the database is "entirely for free". The README gives no separate data licence. | `LICENSE`, `README.md` | Allowed: the notice below is kept. |
| Theme lists | Our own configuration. | — | — |

No source was dropped: all three licences allow reading and storing the data for this use. The Nasdaq-100 source
was chosen over `jmccarrell/n100tickers` (MIT, but a Python package that exposes membership only by applying a
change history to a baseline, with no plain current list to read) because `index-constitution` publishes the
current list as a CSV. Free data only; nothing here calls a paid feed.

MIT notices to keep with the data: "Copyright (c) 2026 index-constitution contributors" and "Copyright (c) 2023
Jeroen Bouma" (permission notice as in each repository's `LICENSE`).

## Store

Two new tables, additive migration `M025_ReferenceUniverse`; no existing table is touched.

- `research.reference_list_members` - one row per (list, ticker): `ListKey`, `Ticker`, `Name`, `Source`, `AddedAt`. Unique on (`ListKey`, `Ticker`). A ticker's lists are the rows that name it.
- `research.reference_refresh_runs` - one row per dataset per refresh attempt: `Source`, `StartedAt`, `FinishedAt`, `Succeeded`, `MemberCount`, `Error`.

## Weekly refresh

`ReferenceUniverseRefreshJob` (Hangfire `reference-universe-refresh`, Sundays 04:00 UTC, after FinanceDatabase's own
Sunday update) runs `ReferenceUniverseRefresher`: each dataset (`sp500`, `nasdaq100`, `financedatabase`, `themes`)
is fetched, validated, and swapped in as a unit in one save, independently of the others.

- A payload below the dataset's minimum size, with a missing column, or that would shrink a stored dataset below 70% of its previous size is **refused**, not applied (a truncated download must not wipe a good list). Theme lists are exempt from the shrink guard: they are edited by hand.
- **Failure is loud, three ways**: an error log line naming the dataset; a `reference_refresh_runs` row with `Succeeded = false` and the reason; and the job ends **Failed** (the exception is thrown after every dataset has been tried). The failed dataset keeps its previous members.
- **Never silently stale**: `get_reference_universe` always returns per-dataset `lastSuccessAt`, `lastFailureAt`, `lastError`, `consecutiveFailures` and `stale` (last success older than `StaleAfterDays`, 10 by default, or never), and adds a `note` when any dataset is stale.

The table is empty until the first Sunday run; trigger `reference-universe-refresh` from the Hangfire dashboard to
populate it earlier.

## Consumers

- **`Sp500ConstituentSource`** (the `IIndexConstituentSource` port that feeds Radar's broad universe and the analyst-actions universe) reads the `sp500` list from the table (cached 30 minutes) and falls back to the embedded `sp500-constituents.json` (regenerated 2026-11-13) whenever the table is empty, holds fewer than 400 members, or cannot be read.
- **`IReferenceUniverseReader`**: `GetListsAsync`, `GetMembersAsync(list)`, `GetListsForTickerAsync(ticker)`, `GetStatusAsync()`.
- **MCP `get_reference_universe(list?, ticker?, limit?, offset?)`**: catalogue with no arguments, members of a list (ordered by ticker, `limit` 1-1000 default 200, `offset` default 0 to page; `memberCount` is the full total), or the lists a ticker belongs to.

## Not here

Correlation to the book is not a property of a list: a theme such as `theme:semiconductors` or `theme:memory` is a
set of names, and whether a name moves with the owner's DRAM/SOXX concentration is decided by the add rule that
ranks it, not by membership.
