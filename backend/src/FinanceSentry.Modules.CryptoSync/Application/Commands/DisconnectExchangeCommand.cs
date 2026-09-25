using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.CryptoSync.Domain.Exceptions;
using FinanceSentry.Modules.CryptoSync.Domain.Repositories;

namespace FinanceSentry.Modules.CryptoSync.Application.Commands;

public sealed record DisconnectExchangeCommand(Guid UserId, string Provider) : ICommand<Unit>;

/// <summary>
/// Disconnects one venue: deactivates its credential and removes its holdings. Another venue's
/// holdings for the same user are never touched.
/// </summary>
public sealed class DisconnectExchangeCommandHandler(
    IExchangeCredentialRepository credentialRepository,
    ICryptoHoldingRepository holdingRepository)
    : ICommandHandler<DisconnectExchangeCommand, Unit>
{
    public async Task<Unit> Handle(DisconnectExchangeCommand command, CancellationToken cancellationToken)
    {
        var credential = await credentialRepository.GetAsync(command.UserId, command.Provider, cancellationToken);
        var holdings = await holdingRepository.GetAllByUserAndProviderAsync(
            command.UserId, command.Provider, cancellationToken);

        var hasActiveCredential = credential is { IsActive: true };
        if (!hasActiveCredential && holdings.Count == 0)
        {
            throw new ExchangeAccountNotFoundException(command.Provider);
        }

        if (hasActiveCredential)
        {
            credential!.Deactivate();
            credentialRepository.Update(credential);
        }

        await holdingRepository.DeleteByUserAndProviderAsync(command.UserId, command.Provider, cancellationToken);

        if (hasActiveCredential)
        {
            await credentialRepository.SaveChangesAsync(cancellationToken);
        }

        return Unit.Value;
    }
}
