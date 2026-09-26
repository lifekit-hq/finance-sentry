namespace FinanceSentry.Tests.Unit.BankSync.Application;

using FinanceSentry.Modules.BankSync.Application.Commands;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Exceptions;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// Setting/clearing a counterparty's expected monthly inflow — issue #434 Ship 3. Runs the
/// handler over the real repository against an in-memory context, like
/// <c>CommittedMerchantPinsTests</c>: what these tests are actually about is the persisted state
/// and ownership check, not a mocked call.
/// </summary>
public class SetCounterpartyExpectedInflowCommandTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();

    private static DbContextOptions<BankSyncDbContext> Options(string database) =>
        new DbContextOptionsBuilder<BankSyncDbContext>().UseInMemoryDatabase(database).Options;

    private static (ICounterpartyRepository Repository, BankSyncDbContext Context) NewRepository()
    {
        var context = new BankSyncDbContext(Options($"expected-inflow-{Guid.NewGuid():N}"));
        return (new CounterpartyRepository(context), context);
    }

    private static async Task<Counterparty> Seed(BankSyncDbContext context, Guid userId, string name)
    {
        var counterparty = new Counterparty { UserId = userId, Name = name, FlowRole = FlowRoles.FamilySupport };
        context.Counterparties.Add(counterparty);
        await context.SaveChangesAsync();
        return counterparty;
    }

    private static Task<SetCounterpartyExpectedInflowResult> SetExpectedInflow(
        ICounterpartyRepository repository, Guid userId, Guid counterpartyId, decimal? amount, string? currency) =>
        new SetCounterpartyExpectedInflowCommandHandler(repository)
            .Handle(new SetCounterpartyExpectedInflowCommand(userId, counterpartyId, amount, currency),
                CancellationToken.None);

    [Fact]
    public async Task Set_ValidAmountAndCurrency_PersistsBoth()
    {
        var (repository, context) = NewRepository();
        var counterparty = await Seed(context, UserId, "Tenant");

        var result = await SetExpectedInflow(repository, UserId, counterparty.Id, 500m, "eur");

        result.ExpectedMonthlyInflowAmount.Should().Be(500m);
        result.ExpectedMonthlyInflowCurrency.Should().Be("EUR");
        (await repository.GetByIdAsync(counterparty.Id))!.ExpectedMonthlyInflowCurrency.Should().Be("EUR");
    }

    [Fact]
    public async Task Set_ThenClear_NullsBothFields()
    {
        var (repository, context) = NewRepository();
        var counterparty = await Seed(context, UserId, "Tenant");
        await SetExpectedInflow(repository, UserId, counterparty.Id, 500m, "EUR");

        var cleared = await SetExpectedInflow(repository, UserId, counterparty.Id, null, null);

        cleared.ExpectedMonthlyInflowAmount.Should().BeNull();
        cleared.ExpectedMonthlyInflowCurrency.Should().BeNull();
    }

    [Fact]
    public async Task Set_NonPositiveAmount_IsRejected()
    {
        var (repository, context) = NewRepository();
        var counterparty = await Seed(context, UserId, "Tenant");

        var set = async () => await SetExpectedInflow(repository, UserId, counterparty.Id, 0m, "EUR");

        await set.Should().ThrowAsync<InvalidExpectedInflowException>();
    }

    [Fact]
    public async Task Set_InvalidCurrencyCode_IsRejected()
    {
        var (repository, context) = NewRepository();
        var counterparty = await Seed(context, UserId, "Tenant");

        var set = async () => await SetExpectedInflow(repository, UserId, counterparty.Id, 500m, "NOTREAL");

        await set.Should().ThrowAsync<InvalidExpectedInflowException>();
    }

    [Fact]
    public async Task Set_OnlyOneOfAmountOrCurrencySupplied_IsRejected()
    {
        var (repository, context) = NewRepository();
        var counterparty = await Seed(context, UserId, "Tenant");

        var set = async () => await SetExpectedInflow(repository, UserId, counterparty.Id, 500m, null);

        await set.Should().ThrowAsync<InvalidExpectedInflowException>();
    }

    [Fact]
    public async Task Set_OnAnotherUsersCounterparty_IsRejectedAsNotFound()
    {
        var (repository, context) = NewRepository();
        var counterparty = await Seed(context, OtherUserId, "Someone Else's Tenant");

        var set = async () => await SetExpectedInflow(repository, UserId, counterparty.Id, 500m, "EUR");

        await set.Should().ThrowAsync<CounterpartyNotFoundException>();
    }

    [Fact]
    public async Task Set_OnAMissingCounterparty_IsRejectedAsNotFound()
    {
        var (repository, _) = NewRepository();

        var set = async () => await SetExpectedInflow(repository, UserId, Guid.NewGuid(), 500m, "EUR");

        await set.Should().ThrowAsync<CounterpartyNotFoundException>();
    }
}
