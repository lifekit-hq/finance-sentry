namespace FinanceSentry.Tests.Unit.Architecture;

using System.Reflection;
using FinanceSentry.Core.Auth;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// Every module DbContext's model must match its hand-maintained ModelSnapshot.
/// EF Core 9+ refuses to run <c>Database.Migrate()</c> when they diverge
/// (PendingModelChangesWarning) — so a drifted snapshot doesn't just skip one
/// migration, it blocks EVERY pending migration for that context at startup
/// while the app keeps running against the old schema (the 2026-09-05 incident:
/// a CURRENT_TIMESTAMP default present in the snapshot but absent from the
/// model kept M011/M012 from applying and broke the dashboard in production).
/// Migrations in this repo are written by hand, so nothing regenerates the
/// snapshot automatically — this test is the only thing keeping them honest.
/// </summary>
public class ModelSnapshotSyncTests
{
    private static IEnumerable<Type> ContextTypes()
    {
        var files = Directory.GetFiles(AppContext.BaseDirectory, "FinanceSentry.*.dll")
            .Where(f => !Path.GetFileName(f).StartsWith("FinanceSentry.Tests.", StringComparison.Ordinal));

        foreach (var file in files)
        {
            Assembly assembly;
            try
            {
                assembly = Assembly.LoadFrom(file);
            }
            catch (BadImageFormatException)
            {
                continue;
            }

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t is not null).ToArray()!;
            }

            foreach (var type in types)
            {
                if (type.IsClass && !type.IsAbstract && typeof(DbContext).IsAssignableFrom(type)
                    && type.GetConstructors().Any(c =>
                        c.GetParameters() is [var p] && p.ParameterType.IsGenericType
                        && p.ParameterType.GetGenericTypeDefinition() == typeof(DbContextOptions<>)))
                {
                    yield return type;
                }
            }
        }
    }

    public static TheoryData<Type> AllContexts()
    {
        var data = new TheoryData<Type>();
        foreach (var type in ContextTypes())
        {
            data.Add(type);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllContexts))]
    public void Model_matches_its_snapshot_so_migrations_can_apply(Type contextType)
    {
        // The connection string is never opened — HasPendingModelChanges only
        // compares the compiled model against the migrations assembly's snapshot.
        using var context = CreateOffline(contextType);

        context.Database.HasPendingModelChanges().Should().BeFalse(
            $"{contextType.Name}'s model differs from its ModelSnapshot — Database.Migrate() will refuse to " +
            "apply ANY pending migration for this context until the snapshot is reconciled with the model");
    }

    /// <summary>
    /// A context over a connection string that is never opened. Contexts under the Owner query filter also take
    /// an <see cref="ICurrentUser"/>; the model does not depend on who it is, so they get <see cref="NoCurrentUser"/>.
    /// </summary>
    internal static DbContext CreateOffline(Type contextType)
    {
        var optionsBuilderType = typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType);
        var optionsBuilder = (DbContextOptionsBuilder)Activator.CreateInstance(optionsBuilderType)!;
        optionsBuilder.UseNpgsql("Host=localhost;Database=design_time_only;Username=x;Password=x");

        var takesCurrentUser = contextType.GetConstructors()
            .Any(c => c.GetParameters().Any(p => p.ParameterType == typeof(ICurrentUser)));
        object[] args = takesCurrentUser ? [optionsBuilder.Options, NoCurrentUser.Instance] : [optionsBuilder.Options];
        return (DbContext)Activator.CreateInstance(contextType, args)!;
    }
}
