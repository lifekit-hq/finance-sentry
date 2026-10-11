namespace FinanceSentry.Tests.Unit.Observability;

using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using FinanceSentry.Infrastructure.Observability.Hangfire;
using FluentAssertions;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Xunit;

/// <summary>
/// Classification of a failed job's exception (feature 414, US2). Fan-out jobs report every failed
/// unit of work in one <see cref="AggregateException"/>, so the aggregate case decides whether a
/// total outage reaches the consecutive-failure streak at all.
/// </summary>
public sealed class JobFailureTransientClassifierTests
{
    [Fact]
    public void Null_IsNotTransient() =>
        JobFailureTransientClassifier.IsTransient(null).Should().BeFalse();

    [Fact]
    public void SelfHealingError_IsTransient() =>
        JobFailureTransientClassifier.IsTransient(new SocketException()).Should().BeTrue();

    [Fact]
    public void ResiliencePipelineTimeout_IsTransient() =>
        JobFailureTransientClassifier
            .IsTransient(new TimeoutRejectedException("attempt timed out"))
            .Should().BeTrue();

    [Fact]
    public void OpenCircuit_IsTransient() =>
        JobFailureTransientClassifier
            .IsTransient(new BrokenCircuitException())
            .Should().BeTrue();

    [Fact]
    public void TransientErrorWrappedInAChain_IsTransient() =>
        JobFailureTransientClassifier
            .IsTransient(new InvalidOperationException("sync failed", new TimeoutException()))
            .Should().BeTrue();

    [Fact]
    public void TransientHttpStatus_IsTransient() =>
        JobFailureTransientClassifier
            .IsTransient(new HttpRequestException("throttled", null, HttpStatusCode.TooManyRequests))
            .Should().BeTrue();

    [Fact]
    public void NonTransientHttpStatus_IsNotTransient() =>
        JobFailureTransientClassifier
            .IsTransient(new HttpRequestException("bad request", null, HttpStatusCode.BadRequest))
            .Should().BeFalse();

    [Fact]
    public void Aggregate_OfOnlyTransientFailures_IsTransient() =>
        JobFailureTransientClassifier
            .IsTransient(new AggregateException(new TimeoutException(), new SocketException()))
            .Should().BeTrue();

    [Fact]
    public void Aggregate_LedByATransientFailure_IsNotTransient() =>
        JobFailureTransientClassifier
            .IsTransient(new AggregateException(new TimeoutException(), new InvalidOperationException("sticky")))
            .Should().BeFalse();

    [Fact]
    public void NestedAggregate_IsFlattenedBeforeJudging() =>
        JobFailureTransientClassifier
            .IsTransient(new AggregateException(
                new TimeoutException(),
                new AggregateException(new SocketException(), new InvalidOperationException("sticky"))))
            .Should().BeFalse();

    [Fact]
    public void EmptyAggregate_IsNotTransient() =>
        JobFailureTransientClassifier.IsTransient(new AggregateException()).Should().BeFalse();
}
