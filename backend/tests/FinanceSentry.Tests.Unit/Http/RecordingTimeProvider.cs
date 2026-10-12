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
    // Real time a pump waits for the code under test to ask for its next timer before giving up: far longer
    // than any scheduling stall, so it only ever elapses when the code is waiting for something it will not get.
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

    private readonly object _gate = new();
    private readonly List<TimeSpan> _created = [];
    private TaskCompletionSource _createdSignal = NewSignal();

    // Timers already scanned: a second RunAsync on the same clock must not re-fire the first one's waits.
    private int _seen;

    /// <summary>The waits <see cref="RunAsync"/> fired, in order.</summary>
    public List<TimeSpan> Waits { get; } = [];

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        // Recorded only once the clock knows the timer: a pump that saw it earlier could advance past its due
        // time before it was registered, and the timer would then never fire.
        var timer = base.CreateTimer(callback, state, dueTime, period);
        lock (_gate)
        {
            _created.Add(dueTime);
            _createdSignal.TrySetResult();
        }

        return timer;
    }

    public async Task RunAsync(Task task, Func<TimeSpan, bool> isWait)
    {
        while (!task.IsCompleted)
        {
            TimeSpan? next = null;
            Task created;
            lock (_gate)
            {
                while (next is null && _seen < _created.Count)
                {
                    var due = _created[_seen++];
                    if (due > TimeSpan.Zero && isWait(due))
                        next = due;
                }

                if (next is null && _createdSignal.Task.IsCompleted)
                    _createdSignal = NewSignal();
                created = _createdSignal.Task;
            }

            if (next is { } wait)
            {
                Waits.Add(wait);
                Advance(wait);
                continue;
            }

            // Nothing to fire: sleep until the task finishes or the code asks for another timer.
            using var stall = new CancellationTokenSource();
            var woken = await Task.WhenAny(task, created, Task.Delay(StallTimeout, stall.Token));
            if (!ReferenceEquals(woken, task) && !ReferenceEquals(woken, created))
                throw new TimeoutException("The task is still waiting and no wait timer is pending.");
            await stall.CancelAsync();
        }

        await task;
    }

    public async Task<T> RunAsync<T>(Task<T> task, Func<TimeSpan, bool> isWait)
    {
        await RunAsync((Task)task, isWait);
        return await task;
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
