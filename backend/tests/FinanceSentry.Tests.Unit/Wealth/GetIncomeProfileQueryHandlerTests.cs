namespace FinanceSentry.Tests.Unit.Wealth;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Wealth.Application.Queries;
using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>#454 S4: declared income next to observed. Every figure here is synthetic and in USD, so no rate is involved.</summary>
public sealed class GetIncomeProfileQueryHandlerTests
{
    private static readonly Guid UserId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Start = new(2025, 1, 1);

    private readonly Mock<IIncomeSourceRepository> _sources = new();
    private readonly Mock<IQueryHandler<GetCashflowProfileQuery, CashflowProfileResponse>> _cashflow = new();

    private GetIncomeProfileQueryHandler CreateHandler() => new(_sources.Object, _cashflow.Object, new FixedClock(Now));

    private void Observed(decimal? monthlyIncome) => _cashflow
        .Setup(c => c.Handle(It.Is<GetCashflowProfileQuery>(q => q.UserId == UserId), It.IsAny<CancellationToken>()))
        .ReturnsAsync(monthlyIncome is null
            ? new CashflowProfileResponse(CashflowProfileStatus.InsufficientHistory, 1, null, null, null, null, null, null, null, null, false, CashflowProfileReasons.InsufficientMonths)
            : new CashflowProfileResponse(CashflowProfileStatus.Shown, 6, "2026-03", "2026-08", monthlyIncome, 2_000m, 0.3m, 6m, null, null, false));

    private void Declared(params IncomeSource[] declared)
    {
        _sources.SetupGet(s => s.CanReadEncryptedFields).Returns(true);
        _sources.Setup(s => s.ListAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(declared);
    }

    private static IncomeSource Source(
        decimal amount, IncomeCadence cadence = IncomeCadence.Monthly, IncomeBasis basis = IncomeBasis.Net, DateOnly? end = null,
        string label = "Day job")
        => IncomeSource.Create(UserId, IncomeKind.Salary, label, amount, "USD", cadence, basis, Start, end);

    private Task<IncomeProfileResponse> Read() => CreateHandler().Handle(new GetIncomeProfileQuery(UserId), CancellationToken.None);

    [Fact]
    public async Task ObservedWithinTenPercentOfADeclaredNetIncome_Matches()
    {
        Declared(Source(4_000m));
        Observed(3_700m);

        var profile = await Read();

        profile.Variance.Should().Be(IncomeVariance.Matches);
        profile.DeclaredMonthlyUsd.Should().Be(4_000m);
        profile.DeclaredBasis.Should().Be("Net");
        profile.ObservedMonthlyUsd.Should().Be(3_700m);
        profile.DeclaredReadable.Should().BeTrue();
        profile.Disclosure.Should().Contain("never a trading signal");
    }

    [Theory]
    [InlineData(3_000, IncomeVariance.ObservedLower)]
    [InlineData(5_000, IncomeVariance.ObservedHigher)]
    [InlineData(3_600, IncomeVariance.Matches)]
    [InlineData(4_400, IncomeVariance.Matches)]
    public async Task TheVariance_ComparesObservedWithDeclared(double observed, IncomeVariance expected)
    {
        Declared(Source(4_000m));
        Observed((decimal)observed);

        (await Read()).Variance.Should().Be(expected);
    }

    [Fact]
    public async Task WithNoObservedIncome_TheVarianceIsNoObserved_AndTheReasonIsPassedOn()
    {
        Declared(Source(4_000m));
        Observed(null);

        var profile = await Read();

        profile.Variance.Should().Be(IncomeVariance.NoObserved);
        profile.ObservedMonthlyUsd.Should().BeNull();
        profile.ObservedReason.Should().Be(CashflowProfileReasons.InsufficientMonths);
        profile.DeclaredMonthlyUsd.Should().Be(4_000m, "the declared figure stands without an observed one");
    }

    [Fact]
    public async Task WithNothingDeclared_TheVarianceIsNoDeclared()
    {
        Declared();
        Observed(3_700m);

        var profile = await Read();

        profile.Variance.Should().Be(IncomeVariance.NoDeclared);
        profile.DeclaredMonthlyUsd.Should().BeNull();
    }

    [Fact]
    public async Task ARetiredSource_IsListedButDoesNotCount()
    {
        Declared(Source(4_000m), Source(9_000m, end: new DateOnly(2026, 1, 31), label: "Old job"));
        Observed(4_000m);

        var profile = await Read();

        profile.Sources.Should().HaveCount(2);
        profile.Sources.Single(s => s.Label == "Old job").Active.Should().BeFalse();
        profile.ActiveSources.Should().Be(1);
        profile.DeclaredMonthlyUsd.Should().Be(4_000m);
    }

    [Fact]
    public async Task ASourceRetiredBeforeItStarted_NeverCountsInTheDeclaredTotal()
    {
        var start = new DateOnly(2026, 12, 1);
        var notStarted = IncomeSource.Create(UserId, IncomeKind.Salary, "Next job", 9_000m, "USD", IncomeCadence.Monthly, IncomeBasis.Net, start, null);
        notStarted.Retire(new DateOnly(2026, 11, 5));
        Declared(Source(4_000m), notStarted);
        Observed(4_000m);

        var profile = await new GetIncomeProfileQueryHandler(_sources.Object, _cashflow.Object, new FixedClock(new DateTimeOffset(2026, 12, 1, 12, 0, 0, TimeSpan.Zero)))
            .Handle(new GetIncomeProfileQuery(UserId), CancellationToken.None);

        profile.Sources.Should().HaveCount(2);
        profile.Sources.Single(s => s.Label == "Next job").Active.Should().BeFalse();
        profile.ActiveSources.Should().Be(1);
        profile.DeclaredMonthlyUsd.Should().Be(4_000m);
    }

    [Fact]
    public async Task AGrossDeclaration_IsNotComparableWithIncomeThatLandedAfterTax()
    {
        Declared(Source(5_000m, basis: IncomeBasis.Gross));
        Observed(3_700m);

        var profile = await Read();

        profile.Variance.Should().Be(IncomeVariance.NotComparable);
        profile.DeclaredBasis.Should().Be("Gross");
        profile.DeclaredMonthlyUsd.Should().Be(5_000m);
    }

    [Fact]
    public async Task MixedBases_AreNotComparable_AndSaySo()
    {
        Declared(Source(4_000m), Source(500m, basis: IncomeBasis.Gross, label: "Side"));
        Observed(4_000m);

        var profile = await Read();

        profile.Variance.Should().Be(IncomeVariance.NotComparable);
        profile.DeclaredBasis.Should().Be("Mixed");
    }

    [Fact]
    public async Task OnlyIrregularSources_HaveNoMonthlyFigureToCompare()
    {
        Declared(Source(1_000m, IncomeCadence.Irregular));
        Observed(3_700m);

        var profile = await Read();

        profile.Variance.Should().Be(IncomeVariance.NotComparable);
        profile.DeclaredMonthlyUsd.Should().BeNull();
        profile.Sources.Single().MonthlyEquivalentUsd.Should().BeNull();
        profile.ActiveSources.Should().Be(1);
    }

    [Fact]
    public async Task AWeeklySource_IsSummedAsItsMonthlyEquivalent()
    {
        Declared(Source(600m, IncomeCadence.Weekly), Source(36_000m, IncomeCadence.Annual, label: "Bonus plan"));
        Observed(5_600m);

        var profile = await Read();

        profile.DeclaredMonthlyUsd.Should().Be(5_600m);
        profile.Variance.Should().Be(IncomeVariance.Matches);
    }

    [Fact]
    public async Task OnAHostWithoutTheKey_OnlyThePlainColumnsAreReturned_AndTheEncryptedOnesAreNeverRead()
    {
        _sources.SetupGet(s => s.CanReadEncryptedFields).Returns(false);
        _sources.Setup(s => s.ListOutlinesAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new IncomeSourceOutline(Guid.NewGuid(), IncomeKind.Salary, "USD", IncomeCadence.Monthly, IncomeBasis.Net, Start, null, null),
            new IncomeSourceOutline(Guid.NewGuid(), IncomeKind.Side, "EUR", IncomeCadence.Weekly, IncomeBasis.Gross, Start, new DateOnly(2026, 1, 1), null),
        ]);
        Observed(3_700m);

        var profile = await Read();

        profile.DeclaredReadable.Should().BeFalse();
        profile.Variance.Should().Be(IncomeVariance.DeclaredUnreadable);
        profile.Sources.Should().OnlyContain(s => s.Label == null && s.Amount == null && s.MonthlyEquivalentUsd == null);
        profile.Sources.Select(s => s.Active).Should().Equal(true, false);
        profile.ActiveSources.Should().Be(1);
        profile.DeclaredMonthlyUsd.Should().BeNull();
        profile.ObservedMonthlyUsd.Should().Be(3_700m);
        _sources.Verify(s => s.ListAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OnAHostWithoutTheKey_ASourceRetiredBeforeItStarted_IsNeverActive()
    {
        var notStarted = new DateOnly(2026, 12, 1);
        var retiredOn = new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero);
        _sources.SetupGet(s => s.CanReadEncryptedFields).Returns(false);
        _sources.Setup(s => s.ListOutlinesAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new IncomeSourceOutline(Guid.NewGuid(), IncomeKind.Salary, "USD", IncomeCadence.Monthly, IncomeBasis.Net, notStarted, notStarted, retiredOn),
        ]);
        Observed(3_700m);

        var profile = await new GetIncomeProfileQueryHandler(
            _sources.Object, _cashflow.Object, new FixedClock(new DateTimeOffset(2026, 12, 1, 12, 0, 0, TimeSpan.Zero)))
            .Handle(new GetIncomeProfileQuery(UserId), CancellationToken.None);

        profile.Sources.Should().ContainSingle().Which.Active.Should().BeFalse();
        profile.ActiveSources.Should().Be(0);
        profile.Variance.Should().Be(IncomeVariance.NoDeclared);
    }

    [Fact]
    public async Task OnAHostWithoutTheKey_AndNoActiveSource_TheVarianceIsNoDeclared()
    {
        _sources.SetupGet(s => s.CanReadEncryptedFields).Returns(false);
        _sources.Setup(s => s.ListOutlinesAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        Observed(3_700m);

        (await Read()).Variance.Should().Be(IncomeVariance.NoDeclared);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
