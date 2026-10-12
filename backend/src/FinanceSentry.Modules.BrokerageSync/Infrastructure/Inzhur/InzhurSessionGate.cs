using Hangfire;
using Hangfire.Storage;

namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.Inzhur;

/// <summary>
/// Serializes everything that reads and rewrites one user's session jar: the hourly keep-alive, the daily read and a
/// reconnect. The cabinet may replace the refresh token on every refresh, so two refreshes from the same jar would
/// leave the loser presenting a token that is already gone and read as a dead session.
/// </summary>
public interface IInzhurSessionGate
{
    /// <summary>
    /// Waits for the user's turn and holds it until the result is disposed. Throws an <see cref="InzhurApiException"/>
    /// of kind <see cref="InzhurFailureKind.Unavailable"/> when the turn does not come in time, so the caller retries
    /// later like any other transient failure.
    /// </summary>
    Task<IDisposable> EnterAsync(Guid userId, CancellationToken ct = default);
}

/// <summary>
/// The gate as a Hangfire distributed lock, so it holds across the API's workers and across processes. The storage is
/// taken lazily (the module is also loaded by hosts that run no Hangfire, where nothing enters the gate).
/// </summary>
public sealed class HangfireInzhurSessionGate(Func<JobStorage> storage) : IInzhurSessionGate
{
    /// <summary>A refresh plus the two reads is seconds; a turn not given within this means a call is hung.</summary>
    public static readonly TimeSpan WaitLimit = TimeSpan.FromMinutes(2);

    public static string ResourceFor(Guid userId) => $"inzhur-session:{userId:N}";

    // AcquireDistributedLock blocks its thread while it waits, so the wait runs off the caller's.
    public Task<IDisposable> EnterAsync(Guid userId, CancellationToken ct = default)
        => Task.Run<IDisposable>(() =>
        {
            var connection = storage().GetConnection();
            try
            {
                return new Held(connection, connection.AcquireDistributedLock(ResourceFor(userId), WaitLimit));
            }
            catch (DistributedLockTimeoutException ex)
            {
                connection.Dispose();
                throw new InzhurApiException(
                    InzhurFailureKind.Unavailable, "Another Inzhur session refresh for this user is still running.", ex);
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }, ct);

    private sealed class Held(IStorageConnection connection, IDisposable distributedLock) : IDisposable
    {
        public void Dispose()
        {
            distributedLock.Dispose();
            connection.Dispose();
        }
    }
}
