namespace FinanceSentry.Modules.Wealth.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Exceptions;
using FinanceSentry.Modules.Wealth.Domain.Repositories;

/// <summary>
/// The fields of a career event as the person sent them. Every required field is nullable so a missing one is refused by
/// <c>CareerEventInputValidator</c> instead of binding to a default (a missing kind would read as IncomeStep).
/// </summary>
public record CareerEventInput(
    CareerEventKind? Kind,
    DateOnly? Date,
    DateOnly? EndDate,
    string? Label,
    decimal? Amount,
    string? Currency,
    string? Note);

public record CareerEventDto(
    Guid Id,
    CareerEventKind Kind,
    DateOnly Date,
    DateOnly? EndDate,
    string Label,
    decimal Amount,
    string Currency,
    string? Note,
    bool Counted,
    bool Retired,
    DateTimeOffset UpdatedAt)
{
    public static CareerEventDto From(CareerEvent c, DateOnly today) => new(
        c.Id, c.Kind, c.Date, c.EndDate, c.Label, c.Amount, c.Currency, c.Note, c.IsCounted(today), c.RetiredAt is not null, c.UpdatedAt);
}

public record CreateCareerEventCommand(Guid UserId, CareerEventInput Input) : ICommand<CareerEventDto>;

public record UpdateCareerEventCommand(Guid UserId, Guid Id, CareerEventInput Input) : ICommand<CareerEventDto>;

public record RetireCareerEventCommand(Guid UserId, Guid Id) : ICommand<CareerEventDto>;

public record DeleteCareerEventCommand(Guid UserId, Guid Id) : ICommand<Unit>;

// Each write is put on the audit trail by the context's AuditedWriteSaveChangesInterceptor (field names, never values);
// the handlers do not remember to. The validator has run before these handlers, so the input's fields are present.
public class CreateCareerEventCommandHandler(ICareerEventRepository events, TimeProvider clock)
    : ICommandHandler<CreateCareerEventCommand, CareerEventDto>
{
    public async Task<CareerEventDto> Handle(CreateCareerEventCommand command, CancellationToken cancellationToken)
    {
        var i = command.Input;
        var career = CareerEvent.Create(
            command.UserId, i.Kind!.Value, i.Date!.Value, i.EndDate, i.Label!, i.Amount!.Value, i.Currency!, i.Note);
        await events.AddAsync(career, cancellationToken);
        return CareerEventDto.From(career, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
    }
}

public class UpdateCareerEventCommandHandler(ICareerEventRepository events, TimeProvider clock)
    : ICommandHandler<UpdateCareerEventCommand, CareerEventDto>
{
    public async Task<CareerEventDto> Handle(UpdateCareerEventCommand command, CancellationToken cancellationToken)
    {
        var career = await events.GetAsync(command.UserId, command.Id, cancellationToken)
            ?? throw new CareerEventNotFoundException();
        var i = command.Input;
        career.Update(i.Kind!.Value, i.Date!.Value, i.EndDate, i.Label!, i.Amount!.Value, i.Currency!, i.Note);
        await events.SaveAsync(cancellationToken);
        return CareerEventDto.From(career, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
    }
}

public class RetireCareerEventCommandHandler(ICareerEventRepository events, TimeProvider clock)
    : ICommandHandler<RetireCareerEventCommand, CareerEventDto>
{
    public async Task<CareerEventDto> Handle(RetireCareerEventCommand command, CancellationToken cancellationToken)
    {
        var career = await events.GetAsync(command.UserId, command.Id, cancellationToken)
            ?? throw new CareerEventNotFoundException();
        var now = clock.GetUtcNow();
        career.Retire(now);
        await events.SaveAsync(cancellationToken);
        return CareerEventDto.From(career, DateOnly.FromDateTime(now.UtcDateTime));
    }
}

public class DeleteCareerEventCommandHandler(ICareerEventRepository events)
    : ICommandHandler<DeleteCareerEventCommand, Unit>
{
    public async Task<Unit> Handle(DeleteCareerEventCommand command, CancellationToken cancellationToken)
    {
        var career = await events.GetAsync(command.UserId, command.Id, cancellationToken)
            ?? throw new CareerEventNotFoundException();
        await events.RemoveAsync(career, cancellationToken);
        return Unit.Value;
    }
}
