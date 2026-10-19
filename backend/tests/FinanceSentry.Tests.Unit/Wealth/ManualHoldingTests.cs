namespace FinanceSentry.Tests.Unit.Wealth;

using FinanceSentry.Core.Domain;
using FinanceSentry.Core.Headlines;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Services;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Wealth.Application.Commands;
using FinanceSentry.Modules.Wealth.Application.Queries;
using FinanceSentry.Modules.Wealth.Application.Services;
using FinanceSentry.Modules.Wealth.Application.Validators;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

/// <summary>
/// A value the person enters by hand (a Steam wallet, framework §5.5): an L3 reading that counts in total net worth,
/// stays out of cash unless it is cash-convertible, and makes the total ESTIMATED.
/// </summary>
public sealed class ManualHoldingTests
{
    private static readonly Guid UserId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static ManualWealthSource SourceOver(params ManualHolding[] holdings)
    {
        var repo = new Mock<IManualHoldingRepository>();
        repo.Setup(r => r.ListUnscopedAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(holdings);
        return new ManualWealthSource(repo.Object);
    }

    [Fact]
    public async Task AnInEconomyEntry_IsAnOtherPosition_NotCash_AndTheReadingIsEstimated()
    {
        var reading = await SourceOver(
            ManualHolding.Create(UserId, "Steam wallet", 40m, "USD", Today, Convertibility.InEconomy),
            ManualHolding.Create(UserId, "Cash at home", 25m, "USD", Today, Convertibility.Cash)).ReadAsync(UserId);

        reading.CashUsd.Should().Be(25m);
        reading.Positions.Should().ContainSingle().Which.Should().Be(
            new BookFigurePosition("Steam wallet", AssetClassNormalizer.Other, 1m, null, 40m, "manual"));
        reading.TotalValueUsd.Should().Be(65m);
        reading.Reliability.Should().Be(Reliability.L3Estimated);
        reading.FxStatus.Should().Be(FxStatus.Exact);
    }

    [Fact]
    public async Task TheBook_CountsTheEntryInTheTotal_AndTheTotalNotTheCashIsEstimated()
    {
        var source = SourceOver(ManualHolding.Create(UserId, "Steam wallet", 40m, "USD", Today, Convertibility.InEconomy));
        var book = await new BookFiguresService([source], NullLogger<BookFiguresService>.Instance).ReadAsync(UserId);

        book.TotalValueUsd.Should().Be(40m);
        book.CashUsd.Should().Be(0m, "an in-economy value is spendable only inside its economy");
        var assessment = HeadlineQualityMapper.Assess(HeadlineSignals.FromBook(book), true);
        assessment.Quality.Should().Be(HeadlineQuality.Estimated);
        assessment.Reasons.Should().Equal(new HeadlineReason(HeadlineReasonCode.EstimatedValue, "manual"));
        HeadlineSignals.EstimatedIn(book, r => r.CashUsd).Should().BeEmpty();
    }

    [Fact]
    public async Task NoEntries_ReadNothing_AndEstimateNothing()
    {
        var book = await new BookFiguresService([SourceOver()], NullLogger<BookFiguresService>.Instance).ReadAsync(UserId);

        book.TotalValueUsd.Should().Be(0m);
        HeadlineSignals.FromBook(book).EstimatedSources.Should().BeEmpty();
    }

    [Fact]
    public async Task AnEntryAddedInTheWindow_IsANewlyLinkedScopeChange_AnOlderOneIsNot()
    {
        var added = ManualHolding.Create(UserId, "Steam wallet", 40m, "USD", Today, Convertibility.InEconomy);
        var repo = new Mock<IManualHoldingRepository>();
        repo.Setup(r => r.ListIncludingRemovedUnscopedAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([added]);
        var reader = new ManualHoldingScopeChangeReader(repo.Object);

        (await reader.GetChangesSinceAsync(UserId, DateTime.UtcNow.AddDays(-1))).Should().Equal(
            new AccountScopeChange("manual", AccountScopeChangeKinds.Linked, added.CreatedAt.UtcDateTime));
        (await reader.GetChangesSinceAsync(UserId, DateTime.UtcNow.AddDays(1))).Should().BeEmpty();
    }

    [Fact]
    public async Task ARemovedEntry_IsARemovedScopeChange_AtItsRemovalTime_AndWithholdsTheChangePercent()
    {
        var removed = ManualHolding.Create(UserId, "Steam wallet", 500m, "USD", Today, Convertibility.InEconomy);
        removed.Remove();
        removed.RemovedAt.Should().NotBeNull();
        var repo = new Mock<IManualHoldingRepository>();
        repo.Setup(r => r.ListIncludingRemovedUnscopedAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([removed]);
        var reader = new ManualHoldingScopeChangeReader(repo.Object);

        var changes = await reader.GetChangesSinceAsync(UserId, removed.CreatedAt.UtcDateTime.AddDays(-30));
        changes.Should().Equal(
            new AccountScopeChange("manual", AccountScopeChangeKinds.Linked, removed.CreatedAt.UtcDateTime),
            new AccountScopeChange("manual", AccountScopeChangeKinds.Removed, removed.RemovedAt!.Value.UtcDateTime));
        (await reader.GetChangesSinceAsync(UserId, removed.RemovedAt.Value.UtcDateTime.AddDays(1))).Should().BeEmpty();

        // The entry sat in the book for a month and was removed: the $500 drop is not a decline.
        var first = new NetWorthSnapshot { UserId = UserId, SnapshotDate = new DateOnly(2026, 9, 1), TotalNetWorth = 1_500m };
        var last = new NetWorthSnapshot { UserId = UserId, SnapshotDate = new DateOnly(2026, 10, 1), TotalNetWorth = 1_000m };
        var snapshots = new Mock<INetWorthSnapshotRepository>();
        snapshots.Setup(r => r.GetByUserIdAsync(UserId, null, null, It.IsAny<CancellationToken>())).ReturnsAsync([first, last]);
        var scopeReader = new Mock<IAccountScopeChangeReader>();
        scopeReader.Setup(r => r.GetChangesSinceAsync(UserId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new AccountScopeChange("manual", AccountScopeChangeKinds.Removed, new DateTime(2026, 9, 25, 8, 0, 0, DateTimeKind.Utc))]);

        var response = await new GetNetWorthHistoryQueryHandler(snapshots.Object, [scopeReader.Object])
            .Handle(new GetNetWorthHistoryQuery(UserId, null, null), CancellationToken.None);

        response.ScopeChanges.Should().ContainSingle().Which.Kind.Should().Be("removed");
        response.ChangeHeadline!.Percent.Should().BeNull();
        response.ChangeHeadline.Reasons.Select(r => r.Code).Should().Contain(HeadlineReasonCode.ScopeChanged);
    }

    [Fact]
    public void Create_TrimsTheLabel_AndUpperCasesTheCurrency()
    {
        var holding = ManualHolding.Create(UserId, "  Steam wallet ", 40m, "eur", Today, Convertibility.InEconomy);

        holding.Label.Should().Be("Steam wallet");
        holding.Currency.Should().Be("EUR");
        holding.Reliability.Should().Be(Reliability.L3Estimated);
    }

    private static ManualHoldingInput Valid() => new("Steam wallet", 40m, "USD", Today, Convertibility.InEconomy);

    [Fact]
    public void AValidEntry_Passes()
        => new CreateManualHoldingCommandValidator().Validate(new CreateManualHoldingCommand(UserId, Valid()))
            .IsValid.Should().BeTrue();

    public static TheoryData<ManualHoldingInput, string> Invalid() => new()
    {
        { Valid() with { Label = " " }, "Input.Label" },
        { Valid() with { Label = new string('x', ManualHoldingLimits.LabelMaxLength + 1) }, "Input.Label" },
        { Valid() with { Amount = null }, "Input.Amount" },
        { Valid() with { Amount = 0m }, "Input.Amount" },
        { Valid() with { Amount = -5m }, "Input.Amount" },
        { Valid() with { Amount = ManualHoldingLimits.MaxAmount }, "Input.Amount" },
        { Valid() with { Currency = "ZZZ" }, "Input.Currency" },
        { Valid() with { Currency = null }, "Input.Currency" },
        { Valid() with { AsOf = null }, "Input.AsOf" },
        { Valid() with { AsOf = Today.AddDays(2) }, "Input.AsOf" },
        { Valid() with { Convertibility = null }, "Input.Convertibility" },
        { Valid() with { Convertibility = (Convertibility)42 }, "Input.Convertibility" },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void AnInvalidEntry_FailsOnItsField(ManualHoldingInput input, string property)
        => new UpdateManualHoldingCommandValidator().Validate(new UpdateManualHoldingCommand(UserId, Guid.NewGuid(), input))
            .Errors.Select(e => e.PropertyName).Should().Contain(property);

    [Fact]
    public void AMissingBody_Fails()
        => new CreateManualHoldingCommandValidator().Validate(new CreateManualHoldingCommand(UserId, null!))
            .IsValid.Should().BeFalse();
}
