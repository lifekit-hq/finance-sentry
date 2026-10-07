using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.BrokerageSync.Domain.Exceptions;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;

namespace FinanceSentry.Modules.BrokerageSync.Application.Commands;

public sealed record DisconnectIBKRCommand(Guid UserId) : ICommand<Unit>;

public sealed class DisconnectIBKRCommandHandler(
    IIBKRCredentialRepository credentialRepository,
    IBrokerageHoldingRepository holdingRepository)
    : ICommandHandler<DisconnectIBKRCommand, Unit>
{
    public async Task<Unit> Handle(DisconnectIBKRCommand command, CancellationToken cancellationToken)
    {
        var credential = await credentialRepository.GetByUserIdAsync(command.UserId, cancellationToken);
        var holdings = await holdingRepository.GetByUserIdAsync(command.UserId, cancellationToken);

        var hasActiveCredential = credential is not null && credential.IsActive;
        if (!hasActiveCredential && holdings.Count == 0)
            throw new BrokerAccountNotFoundException(
                $"No active IBKR account found for user {command.UserId}.");

        if (credential is not null)
        {
            credential.Deactivate();
            credentialRepository.Update(credential);
        }

        await holdingRepository.DeleteByUserIdAndProviderAsync(command.UserId, "ibkr", cancellationToken);
        await holdingRepository.SaveChangesAsync(cancellationToken);

        if (credential is not null)
            await credentialRepository.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
