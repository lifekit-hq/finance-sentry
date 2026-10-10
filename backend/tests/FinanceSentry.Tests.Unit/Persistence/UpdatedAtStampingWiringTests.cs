namespace FinanceSentry.Tests.Unit.Persistence;

using System.Runtime.CompilerServices;
using FinanceSentry.Core.Auth;
using FinanceSentry.Core.Domain;
using FinanceSentry.Infrastructure.Persistence;
using FinanceSentry.Modules.Agent;
using FinanceSentry.Modules.Agent.Infrastructure;
using FinanceSentry.Modules.Alerts;
using FinanceSentry.Modules.Alerts.Infrastructure.Persistence;
using FinanceSentry.Modules.Analytics;
using FinanceSentry.Modules.Analytics.Infrastructure.Persistence;
using FinanceSentry.Modules.Auth;
using FinanceSentry.Modules.Auth.Infrastructure.Persistence;
using FinanceSentry.Modules.BankSync;
using FinanceSentry.Modules.BankSync.Infrastructure.Persistence;
using FinanceSentry.Modules.BrokerageSync;
using FinanceSentry.Modules.BrokerageSync.Infrastructure.Persistence;
using FinanceSentry.Modules.Budgets;
using FinanceSentry.Modules.Budgets.Infrastructure.Persistence;
using FinanceSentry.Modules.Companion;
using FinanceSentry.Modules.Companion.Infrastructure.Persistence;
using FinanceSentry.Modules.CryptoSync;
using FinanceSentry.Modules.CryptoSync.Infrastructure.Persistence;
using FinanceSentry.Modules.Events;
using FinanceSentry.Modules.Events.Infrastructure.Persistence;
using FinanceSentry.Modules.Radar;
using FinanceSentry.Modules.Radar.Infrastructure.Persistence;
using FinanceSentry.Modules.Research;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FinanceSentry.Modules.Retention;
using FinanceSentry.Modules.Retention.Infrastructure.Persistence;
using FinanceSentry.Modules.Risk;
using FinanceSentry.Modules.Risk.Infrastructure.Persistence;
using FinanceSentry.Modules.Subscriptions;
using FinanceSentry.Modules.Subscriptions.Infrastructure.Persistence;
using FinanceSentry.Modules.Wealth;
using FinanceSentry.Modules.Wealth.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Every module registers its DbContext through its real <c>AddXModule</c> call, so a module that forgets
/// <c>UseUpdatedAtStamping</c> (or a new module that never adds it) fails here instead of silently leaving
/// <c>UpdatedAt</c> frozen. The database is unreachable on purpose: the interceptor stamps before SaveChanges
/// opens a connection, so the stamped value is read off the tracked entity after the save fails.
/// </summary>
public sealed class UpdatedAtStampingWiringTests
{
    private const string UnreachableDatabase =
        "Host=127.0.0.1;Port=1;Database=unreachable;Username=test;Password=test;Timeout=1";

    private static readonly TimeSpan StampTolerance = TimeSpan.FromMinutes(1);

    public static TheoryData<string> Modules => new(Registrations.Keys);

    private static readonly Dictionary<string, (Action<IServiceCollection, IConfiguration> Register, Type Context)> Registrations = new()
    {
        ["Agent"] = ((s, c) => s.AddAgentModule(c), typeof(AgentDbContext)),
        ["Alerts"] = ((s, c) => s.AddAlertsModule(c), typeof(AlertsDbContext)),
        ["Analytics"] = ((s, c) => s.AddAnalyticsModule(c), typeof(AnalyticsDbContext)),
        ["Auth"] = ((s, c) => s.AddAuthModule(c), typeof(AuthDbContext)),
        ["BankSync"] = ((s, c) => s.AddBankSyncModule(c), typeof(BankSyncDbContext)),
        ["BrokerageSync"] = ((s, c) => s.AddBrokerageSyncModule(c), typeof(BrokerageSyncDbContext)),
        ["Budgets"] = ((s, c) => s.AddBudgetsModule(c), typeof(BudgetsDbContext)),
        ["Companion"] = ((s, c) => s.AddCompanionModule(c), typeof(CompanionDbContext)),
        ["CryptoSync"] = ((s, c) => s.AddCryptoSyncModule(c), typeof(CryptoSyncDbContext)),
        ["Events"] = ((s, c) => s.AddEventsModule(c), typeof(EventsDbContext)),
        ["Radar"] = ((s, c) => s.AddRadarModule(c), typeof(RadarDbContext)),
        ["Research"] = ((s, c) => s.AddResearchModule(c), typeof(ResearchDbContext)),
        ["Retention"] = ((s, c) => s.AddRetentionModule(c), typeof(RetentionDbContext)),
        ["Risk"] = ((s, c) => s.AddRiskModule(c), typeof(RiskDbContext)),
        ["Subscriptions"] = ((s, c) => s.AddSubscriptionsModule(c), typeof(SubscriptionsDbContext)),
        ["Wealth"] = ((s, c) => s.AddWealthModule(c), typeof(WealthDbContext)),
    };

    [Theory]
    [MemberData(nameof(Modules))]
    public void ModuleContext_HasTheUpdatedAtInterceptorAttached(string module)
    {
        using var provider = BuildProvider(module);
        using var scope = provider.CreateScope();
        var context = (DbContext)scope.ServiceProvider.GetRequiredService(Registrations[module].Context);

        var interceptors = context.GetService<IDbContextOptions>()
            .FindExtension<CoreOptionsExtension>()!.Interceptors!;

        interceptors.OfType<UpdatedAtSaveChangesInterceptor>().Should().ContainSingle();
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void ModuleContext_StampsEveryModifiedUpdatedAtEntity(string module)
    {
        using var provider = BuildProvider(module);
        using var scope = provider.CreateScope();
        var context = (DbContext)scope.ServiceProvider.GetRequiredService(Registrations[module].Context);

        var tracked = context.Model.GetEntityTypes()
            .Where(e => !e.IsOwned() && typeof(IHasUpdatedAt).IsAssignableFrom(e.ClrType) && e.FindProperty("UpdatedAt") is not null)
            .Select(e => Track(context, e))
            .ToList();
        if (tracked.Count == 0)
            return;

        var save = () => context.SaveChanges();
        save.Should().Throw<Exception>("the database is unreachable");

        foreach (var (entityType, entity) in tracked)
        {
            var stamped = ToInstant(context.Entry(entity).Property("UpdatedAt").CurrentValue!);
            stamped.Should().BeCloseTo(DateTimeOffset.UtcNow, StampTolerance, $"{entityType} was modified and saved");
        }
    }

    private static ServiceProvider BuildProvider(string module)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = UnreachableDatabase,
            ["Deduplication:MasterKeyBase64"] = Convert.ToBase64String(new byte[32]),
            ["Jwt:Secret"] = "updated-at-stamping-wiring-tests-signing-secret-0123456789",
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<ICurrentUser>(new NoPerson());
        Registrations[module].Register(services, config);
        return services.BuildServiceProvider();
    }

    private static (string EntityType, object Entity) Track(
        DbContext context, Microsoft.EntityFrameworkCore.Metadata.IEntityType entityType)
    {
        var entity = RuntimeHelpers.GetUninitializedObject(entityType.ClrType);
        var entry = context.Entry(entity);
        foreach (var key in entityType.FindPrimaryKey()!.Properties)
        {
            entry.Property(key.Name).CurrentValue = key.ClrType == typeof(Guid) ? Guid.NewGuid()
                : key.ClrType == typeof(string) ? "key"
                : Activator.CreateInstance(key.ClrType);
        }

        entry.State = EntityState.Unchanged;
        var updatedAt = entry.Property("UpdatedAt");
        updatedAt.CurrentValue = updatedAt.Metadata.ClrType == typeof(DateTimeOffset)
            ? (object)DateTimeOffset.MinValue
            : DateTime.MinValue;
        entry.State = EntityState.Modified;
        return (entityType.ClrType.Name, entity);
    }

    private static DateTimeOffset ToInstant(object value) =>
        value is DateTimeOffset offset ? offset : new DateTimeOffset(((DateTime)value).ToUniversalTime());

    private sealed class NoPerson : ICurrentUser
    {
        public Guid? UserId => null;
    }
}
