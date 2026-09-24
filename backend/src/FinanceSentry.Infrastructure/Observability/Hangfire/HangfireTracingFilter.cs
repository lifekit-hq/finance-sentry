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
/// <see cref="ActivitySource"/> <c>FinanceSentry.Hangfire</c>: a child of the stored context for the
/// first attempt of an ad-hoc enqueue, or an <see cref="ActivityLink"/> to it for a recurring trigger,
/// every automatic retry, and every manual requeue. A thrown job exception marks the activity
/// <see cref="ActivityStatusCode.Error"/> before it closes.
///
/// <para>
/// <b>Why retries link instead of parent.</b> "One trace per request" holds only for the first
/// attempt. Hangfire's automatic retry backs off between attempts, so a retry can run minutes or hours
/// after the request span closed; parenting it would stretch the request's trace across that gap and
/// make a fast request look like an hours-long one. The failure that caused the retry is an
/// operational event in its own right, so each retry gets its own root trace, keeps a link back to the
/// originating request, and carries <c>hangfire.job_id</c> + <c>hangfire.retry_count</c> tags so all
/// attempts of one job can still be grouped from either end.
/// </para>
///
/// <para>
/// <b>Manual requeue gets the same treatment.</b> An operator's dashboard Requeue is not an automatic
/// retry — <c>AutomaticRetryAttribute</c> never writes <c>RetryCount</c> for it, so keying on that
/// counter alone would re-parent the job onto the original request's trace however much later the
/// requeue happens (a week-old trace gaining a fresh span), and would do so inconsistently: a job
/// declared with <c>Attempts = 0</c> is always parented, while one whose automatic retries already ran
/// out stays linked purely because the stale counter is still positive. <see
/// cref="HangfireManualRequeueDetectionFilter"/> closes that gap with a signal the requeue itself
/// produces — a state-election filter, not job age — and records <see
/// cref="ManualRequeueParameter"/>; this filter treats that marker exactly like <c>RetryCount &gt; 0</c>
/// and then clears it so a later automatic retry of the requeued run follows the ordinary retry rule.
/// </para>
/// </summary>
public sealed class HangfireTracingFilter : IClientFilter, IServerFilter
{
    public const string ActivitySourceName = "FinanceSentry.Hangfire";

    internal const string TraceParentParameter = "traceparent";
    internal const string TraceStateParameter = "tracestate";
    internal const string RecurringJobIdParameter = "RecurringJobId";

    /// <summary>Set by Hangfire's <c>AutomaticRetryAttribute</c> before each retry attempt; absent on the first run.</summary>
    internal const string RetryCountParameter = "RetryCount";

    /// <summary>
    /// Set by <see cref="HangfireManualRequeueDetectionFilter"/> when a job is elected into
    /// <c>Enqueued</c> by a manual dashboard Requeue rather than automatic retry; cleared here once
    /// read, in <see cref="OnPerforming"/>.
    /// </summary>
    internal const string ManualRequeueParameter = "FinanceSentryManualRequeue";

    internal const string JobIdTag = "hangfire.job_id";
    internal const string RetryCountTag = "hangfire.retry_count";

    private const string ActivityItemKey = "FinanceSentry.Hangfire.Activity";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public void OnCreating(CreatingContext context)
    {
        var activity = Activity.Current;
        if (string.IsNullOrEmpty(activity?.Id))
            return;

        context.SetJobParameter(TraceParentParameter, activity.Id);
        if (!string.IsNullOrEmpty(activity.TraceStateString))
            context.SetJobParameter(TraceStateParameter, activity.TraceStateString);
    }

    public void OnCreated(CreatedContext context)
    {
    }

    public void OnPerforming(PerformingContext context)
    {
        var traceParent = GetJobParameter<string>(context, TraceParentParameter);
        var traceState = GetJobParameter<string>(context, TraceStateParameter);
        var isRecurring = !string.IsNullOrEmpty(GetJobParameter<string>(context, RecurringJobIdParameter));
        var retryCount = GetJobParameter<int>(context, RetryCountParameter);
        var isManualRequeue = GetJobParameter<bool>(context, ManualRequeueParameter);
        if (isManualRequeue)
            context.SetJobParameter(ManualRequeueParameter, false);

        var activity = StartActivity(context.BackgroundJob.Job, traceParent, traceState, isRecurring, retryCount, isManualRequeue);
        if (activity is null)
            return;

        activity.SetTag(JobIdTag, context.BackgroundJob.Id);
        activity.SetTag(RetryCountTag, retryCount);
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

    /// <summary>
    /// Core start-activity decision (internal for unit testing, no Hangfire context required). The stored
    /// context becomes the parent only for the first attempt of an ad-hoc enqueue; recurring runs,
    /// automatic retries (<paramref name="retryCount"/> &gt; 0, i.e. Hangfire's <c>RetryCount</c>
    /// parameter), and manual requeues (<paramref name="isManualRequeue"/>, set by <see
    /// cref="HangfireManualRequeueDetectionFilter"/>) start their own trace and link to it instead.
    /// </summary>
    internal static Activity? StartActivity(Job? job, string? traceParent, string? traceState, bool isRecurring, int retryCount = 0, bool isManualRequeue = false)
    {
        var name = JobMetricsFilter.JobName(job);

        if (string.IsNullOrEmpty(traceParent) || !ActivityContext.TryParse(traceParent, traceState, out var parentContext))
            return ActivitySource.StartActivity(name, ActivityKind.Internal);

        var linkInsteadOfParent = isRecurring || retryCount > 0 || isManualRequeue;
        return linkInsteadOfParent
            ? ActivitySource.StartActivity(name, ActivityKind.Internal, default(ActivityContext), links: [new ActivityLink(parentContext)])
            : ActivitySource.StartActivity(name, ActivityKind.Internal, parentContext);
    }

    private static T? GetJobParameter<T>(PerformingContext context, string name) =>
        context.GetJobParameter<T>(name, allowStale: true);
}
