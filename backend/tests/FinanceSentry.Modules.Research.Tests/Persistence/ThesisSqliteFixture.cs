namespace FinanceSentry.Modules.Research.Tests.Persistence;

using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

/// <summary>
/// A SQLite-backed <see cref="ResearchDbContext"/> holding the tables the thesis write path
/// touches — <c>theses</c> and the <c>quote_cache</c> its Created-event hook refreshes — so the
/// #443 save-to-read guarantee and the #626 unit-of-work guarantee can both be exercised over real
/// SQL on any host, no Docker and no network.
///
/// Two deliberate deviations from the production Postgres mapping, both narrowly scoped:
/// <list type="bullet">
/// <item>every entity outside <see cref="KeptEntityTypes"/> is dropped from the model, because the
/// rest of the schema leans on Postgres-only constructs (<c>real[]</c> vectors, <c>jsonb</c>) that
/// SQLite cannot create;</item>
/// <item><c>gen_random_uuid()</c> and the Postgres column types are cleared — SQLite rejects an
/// unknown function in a <c>DEFAULT</c> clause, and the application assigns both the id and the
/// timestamps itself.</item>
/// </list>
///
/// Length limits are *not* a deviation: SQLite ignores <c>varchar(n)</c> widths, so the limits the
/// production model declares are projected into CHECK constraints. Those columns therefore refuse
/// over-length text here exactly as they do on Postgres, and shrinking a <c>HasMaxLength</c> in
/// <see cref="ResearchDbContext"/> makes the tests fail rather than silently pass.
/// </summary>
public sealed class ThesisSqliteFixture : IAsyncDisposable
{
    private const string ThesisTextLengthCheckConstraint = "ck_theses_thesis_text_length";

    private const string QuoteTickerLengthCheckConstraint = "ck_quote_cache_ticker_length";

    /// <summary>
    /// <see cref="QuoteCacheEntry"/> is kept alongside the thesis because the two share the scoped
    /// context: <c>ThesisEventRecorder</c> refreshes quotes between the thesis write and the
    /// event write, so a quote-cache failure lands in the middle of the thesis unit of work.
    /// </summary>
    private static readonly HashSet<Type> KeptEntityTypes =
    [
        typeof(InvestmentThesis),
        typeof(QuoteCacheEntry),
    ];

    private readonly SqliteConnection connection;

    private ThesisSqliteFixture(SqliteConnection connection) => this.connection = connection;

    public static async Task<ThesisSqliteFixture> CreateAsync()
    {
        // The in-memory database lives exactly as long as this connection, so every context built
        // from it sees the same tables while staying isolated from every other fixture.
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var fixture = new ThesisSqliteFixture(connection);
        await using var ctx = fixture.CreateContext();
        await ctx.Database.EnsureCreatedAsync();
        return fixture;
    }

    public ThesisOnlySqliteContext CreateContext() =>
        new(new DbContextOptionsBuilder<ResearchDbContext>()
            .UseSqlite(this.connection)
            .Options);

    public async ValueTask DisposeAsync() => await this.connection.DisposeAsync();

    public sealed class ThesisOnlySqliteContext(DbContextOptions<ResearchDbContext> options)
        : ResearchDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            var unwanted = modelBuilder.Model.GetEntityTypes()
                .Select(e => e.ClrType)
                .Where(t => !KeptEntityTypes.Contains(t))
                .ToList();

            // Ignore rather than RemoveEntityType: it also drops the foreign keys pointing at the
            // type, which RemoveEntityType refuses to do.
            foreach (var clrType in unwanted)
                modelBuilder.Ignore(clrType);

            var thesis = modelBuilder.Entity<InvestmentThesis>();
            thesis.Property(x => x.Id).HasDefaultValueSql(null);
            thesis.Property(x => x.CreatedAt).HasDefaultValueSql(null);
            thesis.Property(x => x.UpdatedAt).HasDefaultValueSql(null);
            thesis.Property(x => x.EntryPrice).HasColumnType(null);
            thesis.Property(x => x.KeyDataPoints).HasColumnType(null);
            thesis.Property(x => x.Catalysts).HasColumnType(null);
            thesis.Property(x => x.InvalidationTriggers).HasColumnType(null);

            thesis.ToTable(t => t.HasCheckConstraint(
                ThesisTextLengthCheckConstraint,
                $"length(\"ThesisText\") <= {DeclaredMaxLength(thesis.Metadata, nameof(InvestmentThesis.ThesisText))}"));

            var quote = modelBuilder.Entity<QuoteCacheEntry>();
            quote.Property(x => x.FetchedAt).HasDefaultValueSql(null);
            quote.Property(x => x.Price).HasColumnType(null);
            quote.Property(x => x.PreviousClose).HasColumnType(null);

            // Gives a test a deterministic way to make the cache write fail mid-unit-of-work, the
            // way a constraint violation does on Postgres.
            quote.ToTable(t => t.HasCheckConstraint(
                QuoteTickerLengthCheckConstraint,
                $"length(\"Ticker\") <= {DeclaredMaxLength(quote.Metadata, nameof(QuoteCacheEntry.Ticker))}"));
        }

        private static int DeclaredMaxLength(IMutableEntityType entityType, string propertyName)
            => entityType.FindProperty(propertyName)!.GetMaxLength()
               ?? throw new InvalidOperationException(
                   $"ResearchDbContext no longer declares a max length for {entityType.ClrType.Name}."
                   + $"{propertyName}, so the storage limit under test has nothing to derive from.");
    }
}
