namespace FinanceSentry.Infrastructure.Persistence.Conventions;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// The column patterns every module repeated by hand. <see cref="Apply"/> is what every <c>OwnerScopedDbContext</c>
/// gets; the two extensions are opt-in per context because the existing schemas differ (some Guid keys carry no
/// database default, and decimal precision varies by module). An explicit mapping in a context still wins over each.
/// </summary>
public static class PersistenceConventions
{
    /// <summary>Every enum property is stored as its name; chain <c>HasMaxLength</c> where the column should be bounded.</summary>
    public static void Apply(ModelConfigurationBuilder configurationBuilder)
        => configurationBuilder.Properties<Enum>().HaveConversion<string>();

    /// <summary>Gives each generated single-column <see cref="Guid"/> primary key the <c>gen_random_uuid()</c> column default.</summary>
    public static ModelConfigurationBuilder HaveGeneratedGuidKeyDefault(this ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Conventions.Add(_ => new GuidKeyDefaultValueConvention());
        return configurationBuilder;
    }

    /// <summary>Sets the precision of every <see cref="decimal"/> column that declares none of its own.</summary>
    public static ModelConfigurationBuilder HaveDecimalPrecision(
        this ModelConfigurationBuilder configurationBuilder, int precision, int scale)
    {
        configurationBuilder.Properties<decimal>().HavePrecision(precision, scale);
        return configurationBuilder;
    }
}
