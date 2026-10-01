namespace FinanceSentry.Tests.Integration.Alerts;

using FinanceSentry.Modules.Alerts.Application.Services;
using FinanceSentry.Modules.Alerts.Domain;
using FinanceSentry.Modules.Alerts.Infrastructure.Persistence;
using FinanceSentry.Modules.Alerts.Infrastructure.Persistence.Repositories;
using FinanceSentry.Modules.Risk.Application.Commands;
using FinanceSentry.Modules.Risk.Application.Services;
using FinanceSentry.Modules.Risk.Domain;
using FinanceSentry.Modules.Risk.Domain.Ports;
using FinanceSentry.Modules.Risk.Infrastructure.Persistence;
using FinanceSentry.Modules.Risk.Infrastructure.Persistence.Repositories;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;
using Testcontainers.PostgreSql;
using Xunit;

/// <summary>
/// #691 end to end over real Postgres: a MinCashBuffer acknowledgement recorded through Risk's own
/// command silences the liquidity sentinel's <c>CashShortfall</c> alert, and re-opens it once the
/// acknowledged floor worsens past its step. Only the live book read is stubbed.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PolicyAckSuppressesDerivedAlertTests : IAsyncLifetime
{
    private const decimal MinCash = 0.10m;
    private const decimal CashPctAtAck = 0.08m;
    private const decimal WorseningStep = 0.02m;
    private const decimal Total = 10000m;

    private PostgreSqlContainer? _postgres;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Mock<IBookSnapshotReader> _book = new();
    private readonly Mock<IAllocationPolicySource> _allocations = new();

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await _postgres.StartAsync();

        await using var alerts = AlertsContext();
        await alerts.Database.EnsureCreatedAsync();

        // Second context on the same database: EnsureCreated would see existing tables and skip it.
        await using var risk = RiskContext();
        await risk.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }

    // The generator runs from jobs with no person in scope (null); assertion reads act as the user.
    private AlertsDbContext AlertsContext(Guid? actingUser = null) =>
        new(new DbContextOptionsBuilder<AlertsDbContext>().UseNpgsql(_postgres!.GetConnectionString()).Options,
            new FixedCurrentUser(actingUser));

    private RiskDbContext RiskContext() =>
        new(new DbContextOptionsBuilder<RiskDbContext>().UseNpgsql(_postgres!.GetConnectionString()).Options);

    private void GivenCashPct(decimal cashPct)
    {
        _book.Setup(b => b.ReadAsync(_userId, default))
            .ReturnsAsync(new BookSnapshot(Total, Total * cashPct, [], false, [], Total - (Total * cashPct)));
        _allocations.Setup(a => a.GetAllocationTargetsAsync(_userId, default)).ReturnsAsync([]);
    }

    private async Task AcknowledgeMinCashBufferAsync()
    {
        await using var risk = RiskContext();
        await new RiskRuleSetRepository(risk).SaveNewVersionAsync(
            new RiskRuleSet { UserId = _userId, MinCashBufferPct = MinCash });

        await new AcknowledgeViolationCommandHandler(new PolicyViolationAckRepository(risk)).Handle(
            new AcknowledgeViolationCommand(
                _userId, RiskRuleKeys.MinCashBuffer, "CASH", "advisory floor", CashPctAtAck, WorseningStep),
            default);
    }

    private async Task RaiseCashShortfallAsync()
    {
        await using var alerts = AlertsContext();
        await using var risk = RiskContext();
        var reader = new PolicyAckReader(
            new PolicyViolationAckRepository(risk),
            _book.Object,
            new RiskRuleSetRepository(risk),
            _allocations.Object,
            Mock.Of<IDrawdownCheckProvider>(),
            new RiskEvaluationService());

        await new AlertGeneratorService(new AlertRepository(alerts), reader).GenerateCashShortfallAlertAsync(
            _userId, Guid.NewGuid(), "Chase", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5), 25m, "EUR");
    }

    private async Task<List<Alert>> CashShortfallAlertsAsync()
    {
        await using var read = AlertsContext(_userId);
        return await read.Alerts.AsNoTracking()
            .Where(a => a.UserId == _userId && a.Type == AlertType.CashShortfall)
            .ToListAsync();
    }

    [DockerRequiredFact]
    public async Task WithoutAck_TheLiquidityAlertIsRaised()
    {
        GivenCashPct(CashPctAtAck);

        await RaiseCashShortfallAsync();

        (await CashShortfallAlertsAsync()).Should().ContainSingle();
    }

    [DockerRequiredFact]
    public async Task MinCashBufferAck_SilencesTheCashShortfallAlert()
    {
        GivenCashPct(CashPctAtAck);
        await AcknowledgeMinCashBufferAsync();

        await RaiseCashShortfallAsync();

        (await CashShortfallAlertsAsync()).Should().BeEmpty();
    }

    [DockerRequiredFact]
    public async Task MinCashBufferAck_ReopensTheCashShortfallAlertOnceWorsenedPastStep()
    {
        GivenCashPct(CashPctAtAck);
        await AcknowledgeMinCashBufferAsync();

        GivenCashPct(CashPctAtAck - WorseningStep - 0.01m);
        await RaiseCashShortfallAsync();

        (await CashShortfallAlertsAsync()).Should().ContainSingle();
    }
}
