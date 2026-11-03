namespace FinanceSentry.Tests.Integration.Auth;

using System.Text.RegularExpressions;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Infrastructure.Persistence;
using FinanceSentry.Tests.Integration.Research;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Structural guards on the data export that need no data: it covers exactly the modules account closure erases, and no
/// column that looks like a stored secret is written. Both fail when someone adds a module or a column without
/// deciding, instead of the export quietly dropping a person's data or leaking a key.
/// </summary>
public class UserDataExportConventionTests(AssetDossierApiFactory factory) : IClassFixture<AssetDossierApiFactory>
{
    private static readonly Regex SecretLooking = new(
        "secret|token|password|passphrase|api.?key|private|credential|cipher|signature|session|endpoint|p256dh|salt|hash|auth",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Columns whose name looks secret but whose value is not, each reviewed. Anything new fails the test below.
    private static readonly HashSet<string> Reviewed = new(StringComparer.Ordinal)
    {
        // The hash of a document's or article's text, used to skip a re-ingest: not a secret.
        "ResearchDocument.ContentHash",
        "NewsArticle.ContentHash",
        "ResearchChunk.ContentHash",
        // How many model tokens a chunk of text costs: a count, not a credential.
        "ResearchChunk.TokenEstimate",
        // The digest of a transaction's own fields, used only to skip re-imports: derived from the row, opens nothing.
        "Transaction.UniqueHash",
        // The id of the stored connection an account syncs through (the connection's key material is byte[], dropped).
        "BankAccount.MonobankCredentialId",
        // When a broker session started and last refreshed, and the version of the key that encrypts it: metadata only.
        "InzhurCredential.SessionStartedAt",
        "InzhurCredential.SessionRefreshedAt",
        "InzhurCredential.SessionKeyVersion",
        // The market session (regular, pre, post) a cached quote was struck in: not a login session.
        "QuoteCacheEntry.Session",
    };

    [Fact]
    public void EveryModuleThatErasesAPersonsRowsAlsoExportsThem()
    {
        using var scope = factory.Services.CreateScope();

        var erased = scope.ServiceProvider.GetServices<IUserDataEraser>().Select(e => e.Module).ToHashSet();
        var exported = scope.ServiceProvider.GetServices<IUserDataExporter>().Select(e => e.Module).ToHashSet();

        exported.Should().BeEquivalentTo(erased, "the export covers what closing the account deletes, module for module");
    }

    [Fact]
    public void NoMappedColumnThatLooksLikeASecretIsExported()
    {
        using var scope = factory.Services.CreateScope();
        var leaks = new List<string>();

        foreach (var context in AllContexts(scope.ServiceProvider))
        {
            // Identity's own entities are exported only by the Auth exporter, column by column.
            if (context.GetType().Name == "AuthDbContext")
                continue;

            foreach (var entity in context.Model.GetEntityTypes())
            {
                foreach (var column in OwnedRowExporter.ExportedColumns(context.Model, entity.ClrType))
                {
                    var id = $"{entity.ClrType.Name}.{column}";
                    if (SecretLooking.IsMatch(column) && !Reviewed.Contains(id))
                        leaks.Add($"{context.GetType().Name}: {id}");
                }
            }
        }

        leaks.Should().BeEmpty(
            "a column that looks like a credential must be dropped (OwnedRowExporter.Redacted, or stored as byte[]) or reviewed");
    }

    [Fact]
    public void EveryEncryptedColumnIsDropped()
    {
        using var scope = factory.Services.CreateScope();
        var written = new List<string>();

        foreach (var context in AllContexts(scope.ServiceProvider))
        {
            foreach (var entity in context.Model.GetEntityTypes().Where(e => e.ClrType.Name != "ApplicationUser"))
            {
                var exported = OwnedRowExporter.ExportedColumns(context.Model, entity.ClrType);
                written.AddRange(entity.GetProperties()
                    .Where(p => p.ClrType == typeof(byte[]) && p.PropertyInfo is not null && exported.Contains(p.Name))
                    .Select(p => $"{entity.ClrType.Name}.{p.Name}"));
            }
        }

        written.Should().BeEmpty("binary columns are the encrypted provider keys, their IVs and auth tags");
    }

    private static IEnumerable<DbContext> AllContexts(IServiceProvider services) =>
        typeof(OwnedRowExporter).Assembly.GetReferencedAssemblies()
            .Concat(AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName()))
            .Where(n => n.Name?.StartsWith("FinanceSentry.", StringComparison.Ordinal) == true)
            .Select(n => n.Name!)
            .Distinct()
            .SelectMany(name => AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == name))
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsAbstract: false, IsGenericTypeDefinition: false } && typeof(DbContext).IsAssignableFrom(t))
            .Select(services.GetService)
            .OfType<DbContext>();
}
