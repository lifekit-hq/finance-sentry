namespace FinanceSentry.Tests.Unit.Persistence;

using FinanceSentry.Core.Auth;
using FinanceSentry.Infrastructure.Persistence;
using FinanceSentry.Infrastructure.Persistence.Conventions;
using FinanceSentry.Tests.Unit.Architecture;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

/// <summary>
/// The structural half of the Owner filter: a context derived from <see cref="OwnerScopedDbContext"/> scopes every
/// <see cref="IUserOwned"/> entity itself, so these tests fail the build for the two ways a per-user table can still
/// leak - an entity that carries a <c>UserId</c> without declaring <see cref="IUserOwned"/>, and a model whose
/// filter did not reach an implementer. Every converted context is picked up by reflection.
/// </summary>
public sealed class OwnerScopedDbContextTests
{
    private static IEnumerable<Type> ScopedContextTypes()
        => ModelSnapshotSyncTests.ContextTypes().Where(t => typeof(OwnerScopedDbContext).IsAssignableFrom(t) && t != typeof(ProbeContext));

    public static TheoryData<Type> OwnerScopedContexts()
    {
        var data = new TheoryData<Type>();
        foreach (var type in ScopedContextTypes())
        {
            data.Add(type);
        }

        return data;
    }

    [Fact]
    public void At_least_one_context_is_owner_scoped_so_the_structural_tests_are_not_vacuous()
        => ScopedContextTypes().Should().NotBeEmpty();

    [Theory]
    [MemberData(nameof(OwnerScopedContexts))]
    public void Every_IUserOwned_entity_declares_the_Owner_filter(Type contextType)
    {
        using var context = ModelSnapshotSyncTests.CreateOffline(contextType);

        var owned = context.Model.GetEntityTypes()
            .Where(e => typeof(IUserOwned).IsAssignableFrom(e.ClrType) && e.BaseType is null)
            .ToList();

        owned.Should().NotBeEmpty($"{contextType.Name} is owner-scoped, so it should hold per-user entities");
        owned.Should().OnlyContain(
            e => e.GetDeclaredQueryFilters().Any(f => f.Key == OwnerQueryFilter.Name),
            "a per-user entity without the Owner filter would be readable across people");
    }

    [Theory]
    [MemberData(nameof(OwnerScopedContexts))]
    public void Every_entity_with_a_UserId_column_declares_IUserOwned(Type contextType)
    {
        using var context = ModelSnapshotSyncTests.CreateOffline(contextType);

        // A nullable UserId marks a shared corpus (null rows are visible to everyone): IUserOwned's Guid cannot hold
        // it, so such an entity declares the Owner filter by hand instead.
        var unmarked = context.Model.GetEntityTypes()
            .Where(e => e.FindProperty("UserId") is { ClrType: var t } && (t == typeof(Guid) || t == typeof(Guid?))
                && !typeof(IUserOwned).IsAssignableFrom(e.ClrType)
                && !(e.FindProperty("UserId")!.ClrType == typeof(Guid?)
                    && e.GetDeclaredQueryFilters().Any(f => f.Key == OwnerQueryFilter.Name)))
            .Select(e => e.ClrType.Name);

        unmarked.Should().BeEmpty(
            "an entity keyed to a person must implement IUserOwned (or, for a shared corpus with a nullable UserId, " +
            "declare the Owner filter itself) or the context cannot scope it to that person");
    }

    [Fact]
    public void Conventions_default_a_generated_Guid_key_and_store_enums_as_text_while_an_explicit_mapping_wins()
    {
        using var context = ModelSnapshotSyncTests.CreateOffline(typeof(ProbeContext));
        var model = context.Model;

        model.FindEntityType(typeof(Plain))!.FindProperty("Id")!.GetDefaultValueSql()
            .Should().Be(GuidKeyDefaultValueConvention.DefaultSql);
        model.FindEntityType(typeof(Explicit))!.FindProperty("Id")!.GetDefaultValueSql()
            .Should().Be("uuid_generate_v4()");
        model.FindEntityType(typeof(Assigned))!.FindProperty("Id")!.GetDefaultValueSql()
            .Should().BeNull("a key the caller always assigns is not database-generated");

        var plain = model.FindEntityType(typeof(Plain))!;
        plain.FindProperty(nameof(Plain.Kind))!.GetProviderClrType().Should().Be(typeof(string));
        plain.FindProperty(nameof(Plain.Amount))!.GetPrecision().Should().Be(ProbeContext.Precision);
        plain.FindProperty(nameof(Plain.Fee))!.GetPrecision().Should().Be(10, "an explicit precision wins over the default");
    }

    public enum Kind
    {
        One,
        Two,
    }

    public sealed class Plain : IUserOwned
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Kind Kind { get; set; }
        public decimal Amount { get; set; }
        public decimal Fee { get; set; }
    }

    public sealed class Explicit
    {
        public Guid Id { get; set; }
    }

    public sealed class Assigned
    {
        public Guid Id { get; set; }
    }

    public sealed class ProbeContext(DbContextOptions<ProbeContext> options, ICurrentUser currentUser)
        : OwnerScopedDbContext(options, currentUser)
    {
        public const int Precision = 18;

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            base.ConfigureConventions(configurationBuilder);
            configurationBuilder.HaveGeneratedGuidKeyDefault().HaveDecimalPrecision(Precision, 2);
        }

        protected override void ConfigureModel(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Plain>().Property(p => p.Fee).HasPrecision(10, 4);
            modelBuilder.Entity<Explicit>().Property(p => p.Id).HasDefaultValueSql("uuid_generate_v4()");
            modelBuilder.Entity<Assigned>().Property(p => p.Id).ValueGeneratedNever();
        }
    }
}
