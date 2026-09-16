namespace FinanceSentry.Infrastructure.Observability;

using System.Diagnostics;
using OpenTelemetry;

/// <summary>
/// Drops Npgsql spans that have no parent activity (spec 023 amendment, 2026-09-13). Commands issued
/// outside any request — background queue polling, heartbeats, lock queries — would otherwise become
/// a stream of root traces that drowns the gateway → api → Npgsql spine. Clearing the
/// <see cref="ActivityTraceFlags.Recorded"/> flag keeps the span out of every export processor that
/// runs after this one; Npgsql spans under a request are untouched.
/// </summary>
public sealed class ParentlessNpgsqlSpanFilter : BaseProcessor<Activity>
{
    public const string NpgsqlSourceName = "Npgsql";

    public override void OnStart(Activity data)
    {
        if (data.Source.Name == NpgsqlSourceName && data.ParentSpanId == default)
        {
            data.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
        }
    }
}
