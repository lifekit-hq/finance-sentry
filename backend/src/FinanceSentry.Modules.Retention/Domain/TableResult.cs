namespace FinanceSentry.Modules.Retention.Domain;

/// <summary>
/// Per-table breakdown of a retention run, serialized into <see cref="RetentionRun.TableResults"/>
/// (jsonb). Answers US1-AS2: how many rows a run examined and removed for each governed table.
/// </summary>
/// <param name="Table">Qualified name, e.g. <c>bank_sync.SyncJobs</c>.</param>
/// <param name="Examined">Rows matching the cutoff before deletion.</param>
/// <param name="Removed">Rows actually deleted (0 on a dry run).</param>
public sealed record TableResult(string Table, long Examined, long Removed);
