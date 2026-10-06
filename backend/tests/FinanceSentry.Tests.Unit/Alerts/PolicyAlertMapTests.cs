namespace FinanceSentry.Tests.Unit.Alerts;

using System.Reflection;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Core.Utils;
using FinanceSentry.Modules.Alerts.Application.Services;
using FinanceSentry.Modules.Alerts.Domain;
using FinanceSentry.Modules.Alerts.Domain.Repositories;
using FinanceSentry.Modules.Risk.Domain;
using FluentAssertions;
using Moq;
using Xunit;

/// <summary>#691: an acknowledgement on a policy silences every alert class derived from it.</summary>
public class PolicyAlertMapTests
{
    /// <summary>
    /// Alert types that are deliberately NOT derived from a Risk policy. Adding an alert type forces
    /// a choice: map it in <see cref="PolicyAlertMap"/> or list it here.
    /// </summary>
    private static readonly HashSet<string> NotPolicyDerived = new(StringComparer.Ordinal)
    {
        AlertType.LowBalance, // operator-set per-account threshold, not a policy floor
        AlertType.SyncFailure,
        AlertType.UnusualSpend,
        AlertType.ThesisBroken,
        AlertType.MarketStructure,
        AlertType.PolicyViolation, // the Risk emitter itself; already gated by Risk's ack evaluation
        AlertType.Opportunity,
        AlertType.ConsentExpiring,
        AlertType.JobFailure,
        AlertType.PerformanceBrief,
        AlertType.PriceHike,
        AlertType.DuplicateCharge,
        AlertType.CategorySpike,
        AlertType.FxSpread,
        AlertType.RebalanceProposal,
        AlertType.CashSweepProposal,
        AlertType.EarningsAhead,
        AlertType.FilingLanded,
        AlertType.NewsCluster,
        AlertType.BudgetBreach,
        AlertType.FamilyStatement,
        AlertType.FireBrief,
        AlertType.PolicyReview, // measured against the policy statement's bands, not a Risk rule
        AlertType.PolicyReviewMissed,
        AlertType.RelativeUnderperformance,
    };

    private static readonly string[] AllAlertTypes = typeof(AlertType)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral)
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToArray();

    [Fact]
    public void EveryAlertType_IsEitherMappedToAPolicyOrExplicitlyNotPolicyDerived()
    {
        var unclassified = AllAlertTypes
            .Where(t => !PolicyAlertMap.Entries.ContainsKey(t) && !NotPolicyDerived.Contains(t));

        unclassified.Should().BeEmpty(
            "a new alert type must be mapped in PolicyAlertMap or listed as not policy-derived here");
    }

    [Fact]
    public void MappedAlertTypes_AreRealAlertTypes_AndPolicyKeysAreRiskRuleKeys()
    {
        var riskKeys = typeof(RiskRuleKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet();

        foreach (var (alertType, policyKey) in PolicyAlertMap.Entries)
        {
            AllAlertTypes.Should().Contain(alertType);
            riskKeys.Should().Contain(policyKey);
        }
    }

    [Fact]
    public void CashShortfall_MapsToMinCashBuffer()
    {
        PolicyAlertMap.TryGetPolicyKey(AlertType.CashShortfall, out var key).Should().BeTrue();
        key.Should().Be(RiskRuleKeys.MinCashBuffer);
    }

    [Fact]
    public async Task AcknowledgedPolicy_SuppressesItsDerivedAlert()
    {
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var repo = new Mock<IAlertRepository>();
        var acks = new Mock<IPolicyAckReader>();
        acks.Setup(a => a.IsPolicySilencedAsync(userId, RiskRuleKeys.MinCashBuffer, default))
            .ReturnsAsync(true);

        await new AlertGeneratorService(repo.Object, acks.Object).GenerateCashShortfallAlertAsync(
            userId, accountId, "Chase", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5), 25m, "EUR");

        repo.Verify(r => r.AddAsync(It.IsAny<Alert>(), default), Times.Never);
    }

    [Fact]
    public async Task AcknowledgedPolicy_DoesNotSilenceUnmappedAlerts()
    {
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var repo = new Mock<IAlertRepository>();
        var acks = new Mock<IPolicyAckReader>();
        acks.Setup(a => a.IsPolicySilencedAsync(It.IsAny<Guid>(), It.IsAny<string>(), default))
            .ReturnsAsync(true);

        await new AlertGeneratorService(repo.Object, acks.Object)
            .GenerateLowBalanceAlertAsync(userId, accountId, "Chase", 100m, 500m);

        repo.Verify(r => r.AddAsync(It.Is<Alert>(a => a.Type == AlertType.LowBalance), default), Times.Once);
    }
}
