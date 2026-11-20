namespace FinanceSentry.Modules.Research.Tests.Advice;

using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Risk.Application.Queries;
using Xunit;

/// <summary>
/// Proof for "one advice engine" (#1492): the 2026-10-31 cash-sweep input and the 2026-11-04 rebalance input, read
/// from the same stored policy (IPS v4: equities 45 (35-55), crypto 50 (35-60), cash 5 (3-10)), give consistent
/// advice, and the two entry points never diverge on one input.
/// </summary>
public class PortfolioAdviceEngineTests
{
    private const decimal Book = 14_000m;

    // ── Blocked case: the degenerate buffer never recommends deployment ──────

    [Fact]
    public void Blocked_DegenerateBuffer_IsReportedNotSet_AndNeverDeploys()
    {
        // A stored fraction below the 1% floor (0.0006 = 0.06% of the book, about $8) is a unit slip, not a policy.
        var drift = Drift(cashUsd: 2_432m);
        var rules = Rules(minCashBufferFraction: 0.0006m);

        var cash = PortfolioAdviceEngine.CashDeployment(drift, rules);

        Assert.Equal(CashAdviceStatus.BufferNotSet, cash.Status);
        Assert.Equal(0m, cash.DeployableUsd);
        Assert.Null(PortfolioAdviceEngine.Advise(drift, rules).Basis.BufferUsd);
        Assert.Contains("cash buffer not set", PortfolioAdviceEngine.Advise(drift, rules).Basis.Describe());
    }

    [Fact]
    public void Blocked_NoRiskRules_NeverDeploys()
        => Assert.Equal(CashAdviceStatus.BufferNotSet, PortfolioAdviceEngine.CashDeployment(Drift(2_432m), null).Status);

    // ── Allowed case: a real buffer, one voice for both questions ────────────

    [Fact]
    public void Allowed_SaneBuffer_DeploysOnlyTheExcess_AndStatesItsBasis()
    {
        var advice = PortfolioAdviceEngine.Advise(Drift(cashUsd: 2_432m), Rules(0.05m));

        // buffer = max(5% of 14,000 = 700, cash sleeve min 3% = 420) = 700
        Assert.Equal(CashAdviceStatus.Deploy, advice.Cash.Status);
        Assert.Equal(1_732m, advice.Cash.DeployableUsd);
        Assert.Equal(700m, advice.Basis.BufferUsd);
        Assert.Equal("IPS v4, risk rules v3, cash buffer $700", advice.Basis.Describe());
    }

    [Fact]
    public void Allowed_Cash1031Input_StoredFractionIsReadAsAFraction_NotAsAPercent()
    {
        // 2026-10-31: $2,432 idle on a ~$14k book, stored fraction 0.07 (7%). Read as a percent (0.07 / 100) the buffer
        // was about $9.80, the "$9 minimum buffer" of the audit; read as the fraction it is, it is 7% = $980.
        var advice = PortfolioAdviceEngine.Advise(Drift(cashUsd: 2_432m), Rules(0.07m));

        Assert.Equal(980m, advice.Basis.BufferUsd);
        Assert.Equal(CashAdviceStatus.Deploy, advice.Cash.Status);
        Assert.Equal(1_452m, advice.Cash.DeployableUsd);
    }

    [Fact]
    public void Allowed_PolicyCashFloor_WinsOverASmallerRuleBuffer()
    {
        // Rule buffer 1% of 14,000 = 140 is valid but below the IPS cash sleeve minimum (3% = 420): the floor is 420.
        var advice = PortfolioAdviceEngine.Advise(Drift(cashUsd: 1_000m), Rules(0.01m));

        Assert.Equal(420m, advice.Basis.BufferUsd);
        Assert.Equal(CashAdviceStatus.Deploy, advice.Cash.Status);
        Assert.Equal(580m, advice.Cash.DeployableUsd);
    }

    [Fact]
    public void Allowed_Rebalance1104Input_BuysCash_AndHoldsInsteadOfDeploying()
    {
        // 2026-11-04: cash $100 of 14,000 is under the 3% band, so the policy says buy cash; the cash path must
        // not say deploy at the same time.
        var advice = PortfolioAdviceEngine.Advise(Drift(cashUsd: 100m), Rules(0.05m));

        // Cash target 5% of 14,000 = 700, held 100: buy 600.
        Assert.Contains(advice.Orders, o => o.AssetClass == "Cash" && o.Direction == OrderDirection.Buy && o.NotionalUsd == 600m);
        Assert.Equal(CashAdviceStatus.Hold, advice.Cash.Status);
        Assert.Equal(0m, advice.Cash.DeployableUsd);
    }

    // ── The entry points cannot diverge ──────────────────────────────────────

    [Theory]
    [InlineData(0, 0.05)]
    [InlineData(100, 0.05)]
    [InlineData(420, 0.05)]
    [InlineData(700, 0.05)]
    [InlineData(2_432, 0.05)]
    [InlineData(2_432, 0.0006)]
    [InlineData(2_432, 0.10)]
    [InlineData(5_000, 0.01)]
    public void EntryPoints_AgreeWithTheEngine_AndNeverAdviseOpposites(int cash, double bufferFraction)
    {
        var drift = Drift(cashUsd: cash);
        var rules = Rules((decimal)bufferFraction);

        var advice = PortfolioAdviceEngine.Advise(drift, rules);

        Assert.Equal(advice.Orders, PortfolioAdviceEngine.Rebalance(drift, rules));
        Assert.Equal(advice.Cash, PortfolioAdviceEngine.CashDeployment(drift, rules));

        var buysCash = advice.Orders.Any(o => o.AssetClass == "Cash" && o.Direction == OrderDirection.Buy);
        Assert.False(buysCash && advice.Cash.Status == CashAdviceStatus.Deploy, "buy cash and deploy cash in one answer");
    }

    // ── Fixtures: IPS v4 from the audit, read through the drift DTO ──────────

    private static AllocationDriftDto Drift(decimal cashUsd)
    {
        var cashPct = Math.Round(cashUsd / Book * 100m, 2);
        var rest = Book - cashUsd;
        var equities = rest * 0.66m;
        var crypto = rest - equities;
        var sleeves = new List<AllocationSleeveDrift>
        {
            Sleeve("Equities", 45m, 35m, 55m, equities),
            Sleeve("Crypto", 50m, 35m, 60m, crypto),
            Sleeve("Cash", 5m, 3m, 10m, cashUsd),
        };
        return new AllocationDriftDto(
            true, Book, cashUsd, Book - cashUsd, sleeves.Any(s => s.Status != "Within"), sleeves, "quarterly",
            PolicyVersion: 4);

        static AllocationSleeveDrift Sleeve(string name, decimal target, decimal min, decimal max, decimal usd)
        {
            var pct = Math.Round(usd / Book * 100m, 2);
            var status = pct < min ? "UnderBand" : pct > max ? "OverBand" : "Within";
            return new AllocationSleeveDrift(name, target, min, max, pct, usd, Math.Round(pct - target, 2), status);
        }
    }

    private static RiskRuleSetDto Rules(decimal minCashBufferFraction)
        => new(Guid.NewGuid(), 3, 0.5m, null, minCashBufferFraction, null, null, null, DateTimeOffset.UtcNow);
}
