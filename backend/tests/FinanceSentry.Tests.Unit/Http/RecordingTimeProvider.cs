namespace FinanceSentry.Tests.Unit.Http;

using Microsoft.Extensions.Time.Testing;

/// <summary>
/// A <see cref="FakeTimeProvider"/> that remembers every timer it was asked to create and can run a task to
/// completion by firing the ones that are waits (a retry delay, a backoff) - so a test sees exactly which
/// delay the code under test asked for and never sleeps for it. Timeouts are left alone by the
/// <c>isWait</c> predicate: advancing past one would cancel the attempt it guards.
/// </summary>
internal sealed class RecordingTimeProvider : FakeTimeProvider
{
    // A pump that never finishes means the code under test is waiting for something it will not get.
    private const int MaxSpins = 200_000;

    private readonly object _gate = new();
    private readonly List<TimeSpan> _created = [];

    // Timers already scanned: a second RunAsync on the same clock must not re-fire the first one's waits.
    private int _seen;

    /// <summary>The waits <see cref="RunAsync"/> fired, in order.</summary>
    public List<TimeSpan> Waits { get; } = [];

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (_gate)
            _created.Add(dueTime);
        return base.CreateTimer(callback, state, dueTime, period);
    }

    public async Task RunAsync(Task task, Func<TimeSpan, bool> isWait)
    {
        for (var spin = 0; !task.IsCompleted; spin++)
        {
            if (spin > MaxSpins)
                throw new TimeoutException("The task is still waiting and no wait timer is pending.");

            TimeSpan? next = null;
            lock (_gate)
            {
                while (next is null && _seen < _created.Count)
                {
                    var due = _created[_seen++];
                    if (due > TimeSpan.Zero && isWait(due))
                        next = due;
                }
            }

            if (next is { } wait)
            {
                Waits.Add(wait);
                Advance(wait);
            }

            await Task.Yield();
        }

        await task;
    }

    public async Task<T> RunAsync<T>(Task<T> task, Func<TimeSpan, bool> isWait)
    {
        await RunAsync((Task)task, isWait);
        return await task;
    }
}
