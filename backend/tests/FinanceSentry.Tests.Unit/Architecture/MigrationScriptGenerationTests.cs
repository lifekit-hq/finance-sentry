namespace FinanceSentry.Tests.Unit.Architecture;

using FluentAssertions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

/// <summary>
/// Every migration's SQL must be generatable offline. Discovery attributes
/// (MigrationDiscoveryTests) and a synced snapshot (ModelSnapshotSyncTests)
/// only get a migration as far as the migrator's front door — SQL generation
/// can still throw at apply time, e.g. a data operation against a table no
/// entity maps (the 2026-09-05 incident, layer 2: M011's categories seed had
/// no columnTypes, so every startup generated "no entity type mapped to
/// bank_sync.categories", rolled back, and left production without the
/// counterparty tables while CI stayed green). Generating the full script
/// here exercises MigrationsSqlGenerator over every operation of every
/// migration with no database, so that class of failure breaks unit tests
/// instead of production startup.
/// </summary>
public class MigrationScriptGenerationTests
{
    [Theory]
    [MemberData(nameof(ModelSnapshotSyncTests.AllContexts), MemberType = typeof(ModelSnapshotSyncTests))]
    public void Full_migration_script_generates_without_error(Type contextType)
    {
        using var context = ModelSnapshotSyncTests.CreateOffline(contextType);

        var act = () => context.GetService<IMigrator>().GenerateScript();

        act.Should().NotThrow(
            $"every {contextType.Name} migration must produce SQL offline — a migration whose script " +
            "generation throws fails at Database.Migrate() on every startup and never applies");
    }
}
