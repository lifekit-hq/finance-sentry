namespace FinanceSentry.Modules.Wealth.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Exceptions;
using FinanceSentry.Modules.Wealth.Domain.Repositories;

/// <summary>
/// The fields of a manual holding as the person sent them. Every field is nullable so a missing one is refused by
/// <c>ManualHoldingInputValidator</c> instead of binding to a default (a missing convertibility would read as Cash).
/// </summary>
public record ManualHoldingInput(
    string? Label, decimal? Amount, string? Currency, DateOnly? AsOf, Convertibility? Convertibility);

public record ManualHoldingDto(
    Guid Id,
    string Label,
    decimal Amount,
    string Currency,
    DateOnly AsOf,
    Convertibility Convertibility,
    Reliability Reliability,
    DateTimeOffset UpdatedAt)
{
    public static ManualHoldingDto From(ManualHolding h)
        => new(h.Id, h.Label, h.Amount, h.Currency, h.AsOf, h.Convertibility, h.Reliability, h.UpdatedAt);
}

public record CreateManualHoldingCommand(Guid UserId, ManualHoldingInput Input) : ICommand<ManualHoldingDto>;

public record UpdateManualHoldingCommand(Guid UserId, Guid Id, ManualHoldingInput Input) : ICommand<ManualHoldingDto>;

public record DeleteManualHoldingCommand(Guid UserId, Guid Id) : ICommand<Unit>;

// The validator has run before these handlers, so the input's fields are present.
public class CreateManualHoldingCommandHandler(IManualHoldingRepository holdings)
    : ICommandHandler<CreateManualHoldingCommand, ManualHoldingDto>
{
    public async Task<ManualHoldingDto> Handle(CreateManualHoldingCommand command, CancellationToken cancellationToken)
    {
        var i = command.Input;
        var holding = ManualHolding.Create(
            command.UserId, i.Label!, i.Amount!.Value, i.Currency!, i.AsOf!.Value, i.Convertibility!.Value);
        await holdings.AddAsync(holding, cancellationToken);
        return ManualHoldingDto.From(holding);
    }
}

public class UpdateManualHoldingCommandHandler(IManualHoldingRepository holdings)
    : ICommandHandler<UpdateManualHoldingCommand, ManualHoldingDto>
{
    public async Task<ManualHoldingDto> Handle(UpdateManualHoldingCommand command, CancellationToken cancellationToken)
    {
        var holding = await holdings.GetAsync(command.UserId, command.Id, cancellationToken)
            ?? throw new ManualHoldingNotFoundException();
        var i = command.Input;
        holding.Update(i.Label!, i.Amount!.Value, i.Currency!, i.AsOf!.Value, i.Convertibility!.Value);
        await holdings.SaveAsync(cancellationToken);
        return ManualHoldingDto.From(holding);
    }
}

public class DeleteManualHoldingCommandHandler(IManualHoldingRepository holdings)
    : ICommandHandler<DeleteManualHoldingCommand, Unit>
{
    public async Task<Unit> Handle(DeleteManualHoldingCommand command, CancellationToken cancellationToken)
    {
        var holding = await holdings.GetAsync(command.UserId, command.Id, cancellationToken)
            ?? throw new ManualHoldingNotFoundException();
        holding.Remove();
        await holdings.SaveAsync(cancellationToken);
        return Unit.Value;
    }
}
