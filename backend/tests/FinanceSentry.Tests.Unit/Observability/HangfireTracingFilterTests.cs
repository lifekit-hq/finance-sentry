namespace FinanceSentry.Tests.Unit.Observability;

using System.Diagnostics;
using System.Reflection;
using FinanceSentry.Infrastructure.Observability.Hangfire;
using FluentAssertions;
using global::Hangfire;
using global::Hangfire.Client;
using global::Hangfire.Common;
using global::Hangfire.Server;
using global::Hangfire.States;
using global::Hangfire.Storage;
using Moq;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Xunit;

/// <summary>
/// Unit tests for <see cref="HangfireTracingFilter"/> (#616, spec 023 amendment): an enqueued job with
/// an ambient trace parents a child activity, a recurring job links to its stored context instead of
/// parenting onto it, a retried attempt does the same rather than stretching the request trace across
/// the retry back-off, and a thrown job exception marks the activity as errored.
/// </summary>
public class HangfireTracingFilterTests
{
    private static readonly ActivitySource RequestSource = new("FinanceSentry.Tests.HangfireTracing");
    private static readonly MethodInfo SampleMethod = typeof(SampleJob).GetMethod(nameof(SampleJob.Run))!;

    private readonly HangfireTracingFilter _filter = new();
    private readonly Dictionary<string, string> _parameters = [];
    private readonly Mock<IStorageConnection> _connection = new();

    [Fact]
    public void EnqueuedJob_WithAmbientActivity_StartsChildOfStoredContext()
    {
        using var exported = TraceCollector.Start();
        const string jobId = "job-1";

        using (var ambient = RequestSource.StartActivity("incoming-request"))
        {
            ambient.Should().NotBeNull();
            CreateJob();

            var performContext = BuildPerformContext(jobId);
            var performing = new PerformingContext(performContext);
            _filter.OnPerforming(performing);

            var activity = (Activity)performContext.Items[ActivityItemKey()]!;
            activity.ParentSpanId.Should().Be(ambient!.SpanId);
            activity.TraceId.Should().Be(ambient.TraceId);
            activity.Links.Should().BeEmpty();

            _filter.OnPerformed(new PerformedContext(performContext, result: null, canceled: false, exception: null));
        }

        exported.Activities.Should().Contain(a =>
            a.DisplayName == "SampleJob.Run" && a.Status != ActivityStatusCode.Error);
    }

    [Fact]
    public void RecurringJob_LinksToStoredContext_InsteadOfParenting()
    {
        using var exported = TraceCollector.Start();
        const string jobId = "job-2";

        ActivityTraceId ambientTraceId;
        ActivitySpanId ambientSpanId;
        using (var ambient = RequestSource.StartActivity("schedule-trigger"))
        {
            ambient.Should().NotBeNull();
            ambientTraceId = ambient!.TraceId;
            ambientSpanId = ambient.SpanId;
            CreateJob();
        }
        // The recurring trigger itself has no ambient Activity on the Hangfire worker thread — only
        // the stored traceparent/tracestate parameters carry the original context forward.
        _parameters["RecurringJobId"] = SerializationHelper.Serialize("daily-refresh");

        var performContext = BuildPerformContext(jobId);
        var performing = new PerformingContext(performContext);
        _filter.OnPerforming(performing);

        var activity = (Activity)performContext.Items[ActivityItemKey()]!;
        activity.TraceId.Should().NotBe(ambientTraceId);
        activity.ParentSpanId.Should().Be(default(ActivitySpanId));
        activity.Links.Should().ContainSingle(link =>
            link.Context.TraceId == ambientTraceId && link.Context.SpanId == ambientSpanId);

        _filter.OnPerformed(new PerformedContext(performContext, result: null, canceled: false, exception: null));
    }

    [Fact]
    public void RetriedJob_LinksToStoredContext_InsteadOfParenting()
    {
        using var exported = TraceCollector.Start();
        const string jobId = "job-4";
        const int retryAttempt = 2;

        ActivityTraceId ambientTraceId;
        ActivitySpanId ambientSpanId;
        using (var ambient = RequestSource.StartActivity("incoming-request"))
        {
            ambient.Should().NotBeNull();
            ambientTraceId = ambient!.TraceId;
            ambientSpanId = ambient.SpanId;
            CreateJob();
        }
        // AutomaticRetryAttribute writes RetryCount before rescheduling; the worker re-performs the same
        // job with the same stored traceparent, possibly hours later.
        _parameters["RetryCount"] = SerializationHelper.Serialize(retryAttempt);

        var performContext = BuildPerformContext(jobId);
        _filter.OnPerforming(new PerformingContext(performContext));

        var activity = (Activity)performContext.Items[ActivityItemKey()]!;
        activity.TraceId.Should().NotBe(ambientTraceId);
        activity.ParentSpanId.Should().Be(default(ActivitySpanId));
        activity.Links.Should().ContainSingle(link =>
            link.Context.TraceId == ambientTraceId && link.Context.SpanId == ambientSpanId);
        activity.GetTagItem("hangfire.retry_count").Should().Be(retryAttempt);
        activity.GetTagItem("hangfire.job_id").Should().Be(jobId);

        _filter.OnPerformed(new PerformedContext(performContext, result: null, canceled: false, exception: null));
    }

    [Fact]
    public void FirstAttempt_CarriesJobIdAndZeroRetryCountTags()
    {
        using var exported = TraceCollector.Start();
        const string jobId = "job-5";

        var performContext = BuildPerformContext(jobId);
        _filter.OnPerforming(new PerformingContext(performContext));

        var activity = (Activity)performContext.Items[ActivityItemKey()]!;
        activity.GetTagItem("hangfire.job_id").Should().Be(jobId);
        activity.GetTagItem("hangfire.retry_count").Should().Be(0);

        _filter.OnPerformed(new PerformedContext(performContext, result: null, canceled: false, exception: null));
    }

    [Fact]
    public void ThrowingJob_MarksActivityErrorStatus()
    {
        using var exported = TraceCollector.Start();
        const string jobId = "job-3";

        var performContext = BuildPerformContext(jobId);
        var performing = new PerformingContext(performContext);
        _filter.OnPerforming(performing);
        var activity = (Activity)performContext.Items[ActivityItemKey()]!;

        var error = new InvalidOperationException("boom");
        _filter.OnPerformed(new PerformedContext(performContext, result: null, canceled: false, exception: error));

        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.StatusDescription.Should().Be(error.Message);
    }

    private static string ActivityItemKey() => "FinanceSentry.Hangfire.Activity";

    private void CreateJob()
    {
        var job = new Job(typeof(SampleJob), SampleMethod);
        var createContext = new CreateContext(
            Mock.Of<JobStorage>(), _connection.Object, job, Mock.Of<IState>());

        _filter.OnCreating(new CreatingContext(createContext));
        _filter.OnCreated(new CreatedContext(createContext, backgroundJob: null, canceled: false, exception: null));

        foreach (var (name, value) in createContext.Parameters)
            _parameters[name] = SerializationHelper.Serialize(value);
    }

    private PerformContext BuildPerformContext(string jobId)
    {
        var job = new Job(typeof(SampleJob), SampleMethod);
        var backgroundJob = new BackgroundJob(jobId, job, DateTime.UtcNow, new Dictionary<string, string>(_parameters));
        return new PerformContext(Mock.Of<JobStorage>(), _connection.Object, backgroundJob, Mock.Of<IJobCancellationToken>());
    }

    /// <summary>A trivial, publicly-invokable job target — Hangfire's <see cref="Job"/> validation requires one.</summary>
    private static class SampleJob
    {
        public static void Run()
        {
        }
    }

    /// <summary>Collects every activity from the <c>FinanceSentry.Hangfire</c> and request sources for assertion.</summary>
    private sealed class TraceCollector : IDisposable
    {
        private readonly TracerProvider _provider;

        private TraceCollector(TracerProvider provider, List<Activity> activities)
        {
            _provider = provider;
            Activities = activities;
        }

        public List<Activity> Activities { get; }

        public static TraceCollector Start()
        {
            var activities = new List<Activity>();
            var provider = Sdk.CreateTracerProviderBuilder()
                .AddSource(HangfireTracingFilter.ActivitySourceName)
                .AddSource(RequestSource.Name)
                .AddProcessor(new SimpleActivityExportProcessor(new CollectingExporter(activities)))
                .Build();

            return new TraceCollector(provider, activities);
        }

        public void Dispose() => _provider.Dispose();
    }

    private sealed class CollectingExporter(List<Activity> activities) : BaseExporter<Activity>
    {
        public override ExportResult Export(in Batch<Activity> batch)
        {
            lock (activities)
            {
                foreach (var activity in batch)
                {
                    activities.Add(activity);
                }
            }

            return ExportResult.Success;
        }
    }
}
