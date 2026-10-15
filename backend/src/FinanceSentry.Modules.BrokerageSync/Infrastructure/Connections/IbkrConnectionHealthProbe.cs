using FinanceSentry.Core.Connections;
using FinanceSentry.Infrastructure.Connections;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Interfaces;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.IBKR.OAuth;

namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.Connections;

/// <summary>
/// IBKR's probe (<see cref="IConnectionHealthProbe"/>): the OAuth session check (<see cref="IBrokerAdapter.EnsureSessionAsync"/>),
/// which takes the live session token and lists the portfolio accounts - no positions, ledger or trades. IBKR Flex has no
/// probe: its only call generates a statement, and its daily sync already looks again at a suspect failure.
/// </summary>
public sealed class IbkrConnectionHealthProbe(
    IIBKRCredentialRepository credentials,
    IBrokerAdapter broker,
    IbkrFailureClassifier classifier,
    ConnectionHealthProbeRecorder recorder) : IConnectionHealthProbe
{
    private const string Provider = IbkrFailureClassifier.ProviderName;

    public async Task<IReadOnlyList<ConnectionHealthProbeTarget>> ListCandidatesAsync(CancellationToken ct = default) =>
        (await credentials.GetAllActiveUnscopedAsync(ct))
            .Where(c => c.Health.SuspectSince is not null || c.Health.State == ConnectionHealthState.Dormant)
            .Select(c => new ConnectionHealthProbeTarget(
                new ConnectionHealthSubject(Provider, nameof(IBKRCredential), c.Id, c.UserId), c.Health))
            .ToList();

    public async Task ProbeAsync(ConnectionHealthProbeTarget target, ConnectionHealthProbeReason reason, CancellationToken ct = default)
    {
        if (await credentials.GetByIdUnscopedAsync(target.Subject.Id, ct) is not { IsActive: true } credential)
            return;

        await recorder.ProbeAsync(
            target.Subject,
            credential.Health,
            c => broker.EnsureSessionAsync(credential.Id, c),
            classifier,
            (health, c) =>
            {
                credential.ApplyHealth(health);
                return credentials.SaveHealthUnscopedAsync(credential.Id, health, c);
            },
            ct);
    }
}
