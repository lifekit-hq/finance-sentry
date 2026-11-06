namespace FinanceSentry.Modules.Wealth.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Exceptions;
using FinanceSentry.Modules.Wealth.Domain.Repositories;

/// <summary>
/// The fields of an income source as the person sent them. Every required field is nullable so a missing one is refused
/// by <c>IncomeSourceInputValidator</c> instead of binding to a default (a missing kind would read as Salary).
/// </summary>
public record IncomeSourceInput(
    IncomeKind? Kind,
    string? Label,
    decimal? Amount,
    string? Currency,
    IncomeCadence? Cadence,
    IncomeBasis? GrossOrNet,
    DateOnly? StartDate,
    DateOnly? EndDate);

public record IncomeSourceDto(
    Guid Id,
    IncomeKind Kind,
    string Label,
    decimal Amount,
    string Currency,
    IncomeCadence Cadence,
    IncomeBasis GrossOrNet,
    DateOnly StartDate,
    DateOnly? EndDate,
    decimal? MonthlyEquivalent,
    bool Active,
    DateTimeOffset UpdatedAt)
{
    public static IncomeSourceDto From(IncomeSource s, DateOnly today) => new(
        s.Id, s.Kind, s.Label, s.Amount, s.Currency, s.Cadence, s.GrossOrNet, s.StartDate, s.EndDate,
        s.MonthlyEquivalent, s.IsActiveOn(today), s.UpdatedAt);
}

public record CreateIncomeSourceCommand(Guid UserId, IncomeSourceInput Input) : ICommand<IncomeSourceDto>;

public record UpdateIncomeSourceCommand(Guid UserId, Guid Id, IncomeSourceInput Input) : ICommand<IncomeSourceDto>;

public record RetireIncomeSourceCommand(Guid UserId, Guid Id) : ICommand<IncomeSourceDto>;

public record DeleteIncomeSourceCommand(Guid UserId, Guid Id) : ICommand<Unit>;

// Each write is put on the audit trail by the context's AuditedWriteSaveChangesInterceptor (field names, never values);
// the handlers do not remember to. The validator has run before these handlers, so the input's fields are present.
public class CreateIncomeSourceCommandHandler(IIncomeSourceRepository sources, TimeProvider clock)
    : ICommandHandler<CreateIncomeSourceCommand, IncomeSourceDto>
{
    public async Task<IncomeSourceDto> Handle(CreateIncomeSourceCommand command, CancellationToken cancellationToken)
    {
        var i = command.Input;
        var source = IncomeSource.Create(
            command.UserId, i.Kind!.Value, i.Label!, i.Amount!.Value, i.Currency!, i.Cadence!.Value, i.GrossOrNet!.Value,
            i.StartDate!.Value, i.EndDate);
        await sources.AddAsync(source, cancellationToken);
        return IncomeSourceDto.From(source, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
    }
}

public class UpdateIncomeSourceCommandHandler(IIncomeSourceRepository sources, TimeProvider clock)
    : ICommandHandler<UpdateIncomeSourceCommand, IncomeSourceDto>
{
    public async Task<IncomeSourceDto> Handle(UpdateIncomeSourceCommand command, CancellationToken cancellationToken)
    {
        var source = await sources.GetAsync(command.UserId, command.Id, cancellationToken)
            ?? throw new IncomeSourceNotFoundException();
        var i = command.Input;
        source.Update(
            i.Kind!.Value, i.Label!, i.Amount!.Value, i.Currency!, i.Cadence!.Value, i.GrossOrNet!.Value,
            i.StartDate!.Value, i.EndDate);
        await sources.SaveAsync(cancellationToken);
        return IncomeSourceDto.From(source, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
    }
}

public class RetireIncomeSourceCommandHandler(IIncomeSourceRepository sources, TimeProvider clock)
    : ICommandHandler<RetireIncomeSourceCommand, IncomeSourceDto>
{
    public async Task<IncomeSourceDto> Handle(RetireIncomeSourceCommand command, CancellationToken cancellationToken)
    {
        var source = await sources.GetAsync(command.UserId, command.Id, cancellationToken)
            ?? throw new IncomeSourceNotFoundException();
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        source.Retire(today);
        await sources.SaveAsync(cancellationToken);
        return IncomeSourceDto.From(source, today);
    }
}

public class DeleteIncomeSourceCommandHandler(IIncomeSourceRepository sources)
    : ICommandHandler<DeleteIncomeSourceCommand, Unit>
{
    public async Task<Unit> Handle(DeleteIncomeSourceCommand command, CancellationToken cancellationToken)
    {
        var source = await sources.GetAsync(command.UserId, command.Id, cancellationToken)
            ?? throw new IncomeSourceNotFoundException();
        await sources.RemoveAsync(source, cancellationToken);
        return Unit.Value;
    }
}
