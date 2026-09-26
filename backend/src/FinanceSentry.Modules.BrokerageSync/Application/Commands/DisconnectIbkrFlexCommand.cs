using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.BrokerageSync.Domain.Exceptions;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;

namespace FinanceSentry.Modules.BrokerageSync.Application.Commands;

public sealed record DisconnectIbkrFlexCommand(Guid UserId) : ICommand<Unit>;

public sealed class DisconnectIbkrFlexCommandHandler(IIBKRFlexCredentialRepository credentialRepository)
    : ICommandHandler<DisconnectIbkrFlexCommand, Unit>
{
    public async Task<Unit> Handle(DisconnectIbkrFlexCommand command, CancellationToken cancellationToken)
    {
        var credential = await credentialRepository.GetByUserIdAsync(command.UserId, cancellationToken);
        if (credential is null || !credential.IsActive)
            throw new BrokerAccountNotFoundException(
                $"No active IBKR Flex credential found for user {command.UserId}.");

        credential.Deactivate();
        credentialRepository.Update(credential);
        await credentialRepository.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
