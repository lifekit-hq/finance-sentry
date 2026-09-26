namespace FinanceSentry.Modules.BankSync.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain.Exceptions;
using FinanceSentry.Modules.BankSync.Domain.Repositories;

/// <summary>
/// Sets or clears the expected monthly inflow (e.g. rent) on one of the caller's own
/// counterparties — the fact <c>GetFamilyClearingStatementQuery</c> reads to populate its rent
/// fields. See docs/money-semantics.md §5.1.
/// </summary>
/// <param name="Amount">
/// Null together with <paramref name="Currency"/> clears the expectation. Otherwise must be
/// strictly positive.
/// </param>
/// <param name="Currency">Must be a valid ISO 4217 code when <paramref name="Amount"/> is set.</param>
public sealed record SetCounterpartyExpectedInflowCommand(
    Guid UserId, Guid CounterpartyId, decimal? Amount, string? Currency)
    : ICommand<SetCounterpartyExpectedInflowResult>;

public sealed record SetCounterpartyExpectedInflowResult(
    Guid CounterpartyId, decimal? ExpectedMonthlyInflowAmount, string? ExpectedMonthlyInflowCurrency);

public sealed class SetCounterpartyExpectedInflowCommandHandler(ICounterpartyRepository counterparties)
    : ICommandHandler<SetCounterpartyExpectedInflowCommand, SetCounterpartyExpectedInflowResult>
{
    private readonly ICounterpartyRepository _counterparties =
        counterparties ?? throw new ArgumentNullException(nameof(counterparties));

    public async Task<SetCounterpartyExpectedInflowResult> Handle(
        SetCounterpartyExpectedInflowCommand command, CancellationToken ct)
    {
        var counterparty = await _counterparties.GetByIdAsync(command.CounterpartyId, ct);

        // Not found and "belongs to someone else" return the same error: the endpoint may only
        // ever change the caller's own counterparties, and a distinct response for "exists but
        // isn't yours" would let a caller enumerate other users' counterparty ids.
        if (counterparty is null || counterparty.UserId != command.UserId)
            throw new CounterpartyNotFoundException(command.CounterpartyId);

        if (command.Amount is null && command.Currency is null)
        {
            counterparty.ExpectedMonthlyInflowAmount = null;
            counterparty.ExpectedMonthlyInflowCurrency = null;
        }
        else
        {
            if (command.Amount is null || command.Currency is null)
                throw new InvalidExpectedInflowException(
                    "Amount and currency must be set together, or both cleared.");

            if (command.Amount <= 0)
                throw new InvalidExpectedInflowException("Expected inflow amount must be positive.");

            if (!Iso4217Currency.IsValid(command.Currency))
                throw new InvalidExpectedInflowException(
                    $"'{command.Currency}' is not a valid ISO 4217 currency code.");

            counterparty.ExpectedMonthlyInflowAmount = command.Amount;
            counterparty.ExpectedMonthlyInflowCurrency = command.Currency.Trim().ToUpperInvariant();
        }

        await _counterparties.UpdateAsync(counterparty, ct);

        return new SetCounterpartyExpectedInflowResult(
            counterparty.Id, counterparty.ExpectedMonthlyInflowAmount, counterparty.ExpectedMonthlyInflowCurrency);
    }
}
