namespace FinanceSentry.Modules.Subscriptions.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Subscriptions.Domain.Exceptions;
using FinanceSentry.Modules.Subscriptions.Domain.Repositories;

// --- Set / clear the installment schedule (total number of payments and/or final payment month) ---

public record SetInstallmentTermCommand(
    string UserId, Guid Id, int? TermCount, DateOnly? EndDate = null, DateOnly? StartDate = null) : ICommand<bool>;

public class SetInstallmentTermCommandHandler(IDetectedSubscriptionRepository repository)
    : ICommandHandler<SetInstallmentTermCommand, bool>
{
    private readonly IDetectedSubscriptionRepository _repository = repository;

    public async Task<bool> Handle(SetInstallmentTermCommand command, CancellationToken ct)
    {
        var item = await _repository.GetByIdAsync(command.Id, ct);
        if (item is null || item.UserId != command.UserId)
            throw new SubscriptionNotFoundException();

        item.SetTerm(command.TermCount, command.EndDate, command.StartDate);
        await _repository.UpsertAsync(item, ct);
        return true;
    }
}

// --- Mark an installment finished ---

public record CompleteInstallmentCommand(string UserId, Guid Id) : ICommand<bool>;

public class CompleteInstallmentCommandHandler(IDetectedSubscriptionRepository repository)
    : ICommandHandler<CompleteInstallmentCommand, bool>
{
    private readonly IDetectedSubscriptionRepository _repository = repository;

    public async Task<bool> Handle(CompleteInstallmentCommand command, CancellationToken ct)
    {
        var item = await _repository.GetByIdAsync(command.Id, ct);
        if (item is null || item.UserId != command.UserId)
            throw new SubscriptionNotFoundException();

        item.MarkCompleted();
        await _repository.UpsertAsync(item, ct);
        return true;
    }
}

// --- Delete an installment (primarily for manually-added ones) ---

public record DeleteInstallmentCommand(string UserId, Guid Id) : ICommand<bool>;

public class DeleteInstallmentCommandHandler(IDetectedSubscriptionRepository repository)
    : ICommandHandler<DeleteInstallmentCommand, bool>
{
    private readonly IDetectedSubscriptionRepository _repository = repository;

    public async Task<bool> Handle(DeleteInstallmentCommand command, CancellationToken ct)
    {
        var item = await _repository.GetByIdAsync(command.Id, ct);
        if (item is null || item.UserId != command.UserId)
            throw new SubscriptionNotFoundException();

        await _repository.DeleteAsync(item, ct);
        return true;
    }
}
