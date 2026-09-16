namespace FinanceSentry.Infrastructure.Observability.Hangfire;

using System.Diagnostics;
using global::Hangfire.Client;
using global::Hangfire.Common;
using global::Hangfire.Server;

/// <summary>
/// Propagates trace context across the Hangfire queue boundary (spec 023 amendment, #616): on enqueue
/// (<see cref="IClientFilter"/>) it stores the ambient <see cref="Activity"/>'s W3C
/// <c>traceparent</c>/<c>tracestate</c> as job parameters — never the whole <see cref="Activity"/> or
/// Baggage, neither of which survives the serialize/deserialize round trip through job storage. On
/// execution (<see cref="IServerFilter"/>) it starts an <see cref="Activity"/> from
/// <see cref="ActivitySource"/> <c>FinanceSentry.Hangfire</c>: a child of the stored context for an
/// ad-hoc enqueue, or an <see cref="ActivityLink"/> to it for a recurring trigger — linking rather than
/// parenting keeps each recurring execution its own trace instead of chaining onto whatever trace
/// happened to be ambient when the schedule last fired. A thrown job exception marks the activity
/// <see cref="ActivityStatusCode.Error"/> before it closes.
/// </summary>
public sealed class HangfireTracingFilter : IClientFilter, IServerFilter
{
    public const string ActivitySourceName = "FinanceSentry.Hangfire";

    internal const string TraceParentParameter = "traceparent";
    internal const string TraceStateParameter = "tracestate";
    internal const string RecurringJobIdParameter = "RecurringJobId";

    private const string ActivityItemKey = "FinanceSentry.Hangfire.Activity";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public void OnCreating(CreatingContext context)
    {
        // No-op: nothing to inspect before the job/BackgroundJob is created.
    }

    public void OnCreated(CreatedContext context)
    {
        var activity = Activity.Current;
        if (string.IsNullOrEmpty(activity?.Id))
            return;

        SetJobParameter(context, TraceParentParameter, activity.Id);
        if (!string.IsNullOrEmpty(activity.TraceStateString))
            SetJobParameter(context, TraceStateParameter, activity.TraceStateString);
    }

    public void OnPerforming(PerformingContext context)
    {
        var traceParent = GetJobParameter(context, TraceParentParameter);
        var traceState = GetJobParameter(context, TraceStateParameter);
        var isRecurring = !string.IsNullOrEmpty(GetJobParameter(context, RecurringJobIdParameter));

        var activity = StartActivity(context.BackgroundJob.Job, traceParent, traceState, isRecurring);
        if (activity is not null)
            context.Items[ActivityItemKey] = activity;
    }

    public void OnPerformed(PerformedContext context)
    {
        if (context.Items.TryGetValue(ActivityItemKey, out var item) && item is Activity activity)
        {
            if (context.Exception is not null && !context.ExceptionHandled)
                activity.SetStatus(ActivityStatusCode.Error, context.Exception.Message);

            activity.Dispose();
        }
    }

    /// <summary>Core start-activity decision (internal for unit testing, no Hangfire context required).</summary>
    internal static Activity? StartActivity(Job? job, string? traceParent, string? traceState, bool isRecurring)
    {
        var name = JobMetricsFilter.JobName(job);

        if (string.IsNullOrEmpty(traceParent) || !ActivityContext.TryParse(traceParent, traceState, out var parentContext))
            return ActivitySource.StartActivity(name, ActivityKind.Internal);

        return isRecurring
            ? ActivitySource.StartActivity(name, ActivityKind.Internal, default(ActivityContext), links: [new ActivityLink(parentContext)])
            : ActivitySource.StartActivity(name, ActivityKind.Internal, parentContext);
    }

    private static void SetJobParameter(CreatedContext context, string name, string value)
    {
        context.Connection.SetJobParameter(context.BackgroundJob.Id, name, SerializationHelper.Serialize(value));
    }

    private static string? GetJobParameter(PerformingContext context, string name)
    {
        var raw = context.Connection.GetJobParameter(context.BackgroundJob.Id, name);
        return raw is null ? null : SerializationHelper.Deserialize<string>(raw);
    }
}
