namespace FinanceSentry.Modules.Risk.Application.Services;

using FinanceSentry.Modules.Risk.Domain;
using FinanceSentry.Modules.Risk.Domain.Ports;
using FinanceSentry.Modules.Risk.Domain.Repositories;
using Microsoft.Extensions.Options;

/// <summary>
/// Assembles the <see cref="DrawdownCheck"/> every <c>Evaluate</c> caller passes in (#700): the owner's
/// tolerated decline from the policy port, the book's measured decline from the snapshot history plus
/// the live book. One place, so the daily check, the on-demand compliance report and the
/// acknowledgement reader cannot disagree about whether the book is past its tolerance.
/// </summary>
public interface IDrawdownCheckProvider
{
    /// <summary>The check, or null when there is no tolerance to enforce or no decline to measure yet.</summary>
    Task<DrawdownCheck?> GetAsync(Guid userId, BookSnapshot book, DateTimeOffset now, CancellationToken ct);
}

public sealed class DrawdownCheckProvider(
    IDrawdownPolicySource policySource,
    IHoldingSnapshotRepository snapshotRepo,
    IOptions<RiskOptions> options) : IDrawdownCheckProvider
{
    private readonly int _lookbackDays = options.Value.DrawdownLookbackDays;

    public async Task<DrawdownCheck?> GetAsync(Guid userId, BookSnapshot book, DateTimeOffset now, CancellationToken ct)
    {
        if (await policySource.GetMaxDrawdownAsync(userId, ct) is not { } limit)
            return null;

        var history = await snapshotRepo.ListSinceUnscopedAsync(userId, now - TimeSpan.FromDays(_lookbackDays), ct);
        return BookDrawdownCalculator.Measure(history, book.Positions) is { } observed
            ? new DrawdownCheck(limit, observed)
            : null;
    }
}
