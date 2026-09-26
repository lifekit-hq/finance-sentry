using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.BrokerageSync.Domain;
using FinanceSentry.Modules.BrokerageSync.Domain.Exceptions;
using FinanceSentry.Modules.BrokerageSync.Domain.Repositories;

namespace FinanceSentry.Modules.BrokerageSync.Application.Commands;

/// <summary>The one human-set path for <see cref="BrokerageInstrument.Classification"/>. Null clears it.</summary>
public sealed record SetInstrumentClassificationCommand(
    Guid UserId,
    Guid InstrumentId,
    InstrumentClassification? Classification) : ICommand<Unit>;

public sealed class SetInstrumentClassificationCommandHandler(IBrokerageInstrumentRepository instrumentRepository)
    : ICommandHandler<SetInstrumentClassificationCommand, Unit>
{
    public async Task<Unit> Handle(SetInstrumentClassificationCommand request, CancellationToken ct)
    {
        // Scoped to the caller's own userId — an instrument belonging to another
        // user is indistinguishable from a nonexistent one here.
        var instrument = await instrumentRepository.GetByIdAsync(request.UserId, request.InstrumentId, ct)
            ?? throw new BrokerageInstrumentNotFoundException(
                $"No instrument {request.InstrumentId} found for this user.");

        instrument.SetClassification(request.Classification);
        instrumentRepository.Update(instrument);
        await instrumentRepository.SaveChangesAsync(ct);

        return Unit.Value;
    }
}
