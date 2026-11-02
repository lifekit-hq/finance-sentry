namespace FinanceSentry.Tests.Unit.BankSync.PaydayTransfer;

using FinanceSentry.Modules.BankSync.Application.Queries;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

public class GetPaydayTransferQueryHandlerTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset RateAt = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private readonly Mock<IBankAccountRepository> _accounts = new();
    private readonly Mock<IPaydayTransferSettingsRepository> _settings = new();
    private readonly Mock<IEurUahRateSource> _rates = new();

    private GetPaydayTransferQueryHandler Handler()
        => new(_accounts.Object, _settings.Object, _rates.Object, new FakeTimeProvider(RateAt));

    private void Accounts(params BankAccount[] accounts)
        => _accounts.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(accounts);

    private static BankAccount Card(decimal owed, string provider = "monobank", string type = "credit", string currency = "UAH")
        => new() { UserId = UserId, Provider = provider, AccountType = type, Currency = currency, CurrentBalance = owed };

    [Fact]
    public async Task Builds_the_breakdown_from_the_owed_amount_the_fixed_outflow_and_the_buy_rate()
    {
        Accounts(Card(30000.00m));
        _settings.Setup(s => s.GetAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaydayTransferSettings { UserId = UserId, FixedOutflowUah = 15000m, RoundingStepEur = 50m });
        _rates.Setup(r => r.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new EurUahBuyRate(50.00m, RateAt));

        var result = await Handler().Handle(new GetPaydayTransferQuery(UserId), CancellationToken.None);

        result.HasCard.Should().BeTrue();
        result.OwedUah.Should().Be(30000.00m);
        result.FixedOutflowUah.Should().Be(15000m);
        result.TotalUah.Should().Be(45000m);
        result.Rate!.RateBuy.Should().Be(50.00m);
        result.Rate.PublishedAt.Should().Be(RateAt);
        result.ExactEur.Should().Be(900.00m);
        result.SuggestedEur.Should().Be(900m);
        result.CushionEur.Should().Be(0m);
    }

    [Fact]
    public async Task Without_stored_settings_the_defaults_apply()
    {
        Accounts(Card(1000m));
        _settings.Setup(s => s.GetAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync((PaydayTransferSettings?)null);
        _rates.Setup(r => r.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new EurUahBuyRate(50m, RateAt));

        var result = await Handler().Handle(new GetPaydayTransferQuery(UserId), CancellationToken.None);

        result.FixedOutflowUah.Should().Be(PaydayTransferDefaults.FixedOutflowUah);
        result.RoundingStepEur.Should().Be(PaydayTransferDefaults.RoundingStepEur);
    }

    [Fact]
    public async Task Only_monobank_uah_credit_cards_count_and_a_card_in_credit_owes_nothing()
    {
        Accounts(Card(10000m), Card(5000m), Card(900m, provider: "truelayer"), Card(900m, type: "checking"),
            Card(900m, currency: "EUR"), Card(-2000m));
        _rates.Setup(r => r.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new EurUahBuyRate(50m, RateAt));

        var result = await Handler().Handle(new GetPaydayTransferQuery(UserId), CancellationToken.None);

        result.OwedUah.Should().Be(15000m);
    }

    [Fact]
    public async Task A_closed_card_does_not_count()
    {
        var closed = Card(8000m);
        closed.ClosedAt = RateAt.AddDays(-1);
        Accounts(closed);

        var result = await Handler().Handle(new GetPaydayTransferQuery(UserId), CancellationToken.None);

        result.HasCard.Should().BeFalse();
    }

    [Fact]
    public async Task Without_a_card_there_is_no_figure_and_the_rate_is_not_fetched()
    {
        Accounts();

        var result = await Handler().Handle(new GetPaydayTransferQuery(UserId), CancellationToken.None);

        result.HasCard.Should().BeFalse();
        result.SuggestedEur.Should().BeNull();
        _rates.Verify(r => r.GetAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Without_a_rate_the_uah_side_is_still_shown_and_the_eur_side_is_empty()
    {
        Accounts(Card(30000m));
        _rates.Setup(r => r.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync((EurUahBuyRate?)null);

        var result = await Handler().Handle(new GetPaydayTransferQuery(UserId), CancellationToken.None);

        result.HasCard.Should().BeTrue();
        result.TotalUah.Should().Be(45000m);
        result.Rate.Should().BeNull();
        result.ExactEur.Should().BeNull();
        result.SuggestedEur.Should().BeNull();
        result.CushionEur.Should().BeNull();
    }
}
