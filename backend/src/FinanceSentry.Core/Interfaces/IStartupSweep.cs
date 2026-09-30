namespace FinanceSentry.Core.Interfaces;

/// <summary>
/// One-shot enqueues a module runs once per process before the host serves (e.g. reaping sync state
/// orphaned by the previous process). Unlike <see cref="IJobRegistrar"/> this is not idempotent and
/// must never be repeated.
/// </summary>
public interface IStartupSweep
{
    void Enqueue(IServiceProvider services);
}
