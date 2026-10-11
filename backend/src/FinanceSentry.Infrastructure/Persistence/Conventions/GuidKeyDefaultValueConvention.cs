namespace FinanceSentry.Infrastructure.Persistence.Conventions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

/// <summary>
/// Gives a generated single-column <see cref="Guid"/> primary key the <c>gen_random_uuid()</c> column default, so a row
/// inserted outside EF (SQL migrations, <c>INSERT ... ON CONFLICT</c> upserts) still gets an id.
/// </summary>
public sealed class GuidKeyDefaultValueConvention : IModelFinalizingConvention
{
    public const string DefaultSql = "gen_random_uuid()";

    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            var key = entityType.FindPrimaryKey();
            if (key is not { Properties: [{ ClrType: var type } property] }
                || type != typeof(Guid)
                || property.ValueGenerated != ValueGenerated.OnAdd
                || property.IsForeignKey()
                || property.FindAnnotation(RelationalAnnotationNames.DefaultValueSql) is not null
                || property.FindAnnotation(RelationalAnnotationNames.DefaultValue) is not null
                || property.FindAnnotation(RelationalAnnotationNames.ComputedColumnSql) is not null)
                continue;

            property.Builder.HasDefaultValueSql(DefaultSql);
        }
    }
}
