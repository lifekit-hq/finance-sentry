namespace FinanceSentry.Modules.Research.Infrastructure.Persistence;

using System.Text.Json;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Scoring;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

public class ResearchDbContext(DbContextOptions<ResearchDbContext> options) : DbContext(options)
{
    public DbSet<WatchlistItem> WatchlistItems { get; set; } = null!;

    public DbSet<InvestmentThesis> Theses { get; set; } = null!;

    public DbSet<QuoteCacheEntry> QuoteCache { get; set; } = null!;

    public DbSet<NewsArticle> News { get; set; } = null!;

    public DbSet<MacroEvent> MacroEvents { get; set; } = null!;

    public DbSet<InvestmentPolicyStatement> PolicyStatements { get; set; } = null!;

    public DbSet<ThesisEvent> ThesisEvents { get; set; } = null!;

    public DbSet<OpportunityCandidate> OpportunityCandidates { get; set; } = null!;

    public DbSet<CandidateScore> CandidateScores { get; set; } = null!;

    public DbSet<AnalystAction> AnalystActions { get; set; } = null!;

    public DbSet<AnalystUniverseMember> AnalystUniverseMembers { get; set; } = null!;

    public DbSet<NewsSource> NewsSources { get; set; } = null!;

    public DbSet<ValuationSnapshot> ValuationSnapshots { get; set; } = null!;

    public DbSet<RecommendationTrend> RecommendationTrends { get; set; } = null!;

    public DbSet<ResearchDocument> ResearchDocuments { get; set; } = null!;

    public DbSet<ResearchChunk> ResearchChunks { get; set; } = null!;

    public DbSet<ResearchEmbedding> ResearchEmbeddings { get; set; } = null!;

    public DbSet<AssetLedgerRead> AssetLedgerReads { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("research");
        base.OnModelCreating(modelBuilder);

        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var wb = modelBuilder.Entity<WatchlistItem>();
        wb.ToTable("watchlist_items");
        wb.HasKey(x => x.Id);
        wb.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        wb.Property(x => x.UserId).IsRequired();
        wb.Property(x => x.Ticker).IsRequired().HasMaxLength(20);
        wb.Property(x => x.Exchange).HasMaxLength(20);
        wb.Property(x => x.Note).HasMaxLength(500);
        wb.Property(x => x.AddedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        wb.HasIndex(x => new { x.UserId, x.Ticker }).IsUnique().HasDatabaseName("idx_watchlist_user_ticker");

        var tb = modelBuilder.Entity<InvestmentThesis>();
        tb.ToTable("theses");
        tb.HasKey(x => x.Id);
        tb.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        tb.Property(x => x.UserId).IsRequired();
        tb.Property(x => x.Ticker).IsRequired().HasMaxLength(20);
        tb.Property(x => x.ThesisText).IsRequired().HasMaxLength(4000);
        tb.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        tb.Property(x => x.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        tb.Property(x => x.EntryPrice).HasColumnType("numeric(18,6)");
        tb.Property(x => x.BrokenReason).HasMaxLength(1000);
        tb.Property(x => x.KeyDataPoints)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<List<ThesisDataPoint>>(v, jsonOptions) ?? new());
        tb.Property(x => x.Catalysts)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<List<ThesisCatalyst>>(v, jsonOptions) ?? new());
        tb.Property(x => x.InvalidationTriggers)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<List<ThesisInvalidationTrigger>>(v, jsonOptions) ?? new());
        tb.HasIndex(x => new { x.UserId, x.Ticker }).HasDatabaseName("idx_thesis_user_ticker");

        var nb = modelBuilder.Entity<NewsArticle>();
        nb.ToTable("news_articles");
        nb.HasKey(x => x.Id);
        nb.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        nb.Property(x => x.Source).IsRequired().HasMaxLength(50);
        nb.Property(x => x.Title).IsRequired().HasMaxLength(500);
        nb.Property(x => x.Url).IsRequired().HasMaxLength(2000);
        nb.Property(x => x.Summary).HasMaxLength(4000);
        nb.Property(x => x.ContentHash).IsRequired().HasMaxLength(64);
        nb.Property(x => x.PublishedAt).IsRequired();
        nb.Property(x => x.IngestedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        nb.Property(x => x.Tickers)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<List<string>>(v, jsonOptions) ?? new());
        nb.Property(x => x.Categories)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<List<string>>(v, jsonOptions) ?? new());
        nb.Property(x => x.ThesisIds)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<List<Guid>>(v, jsonOptions) ?? new())
            .Metadata.SetValueComparer(GuidListComparer);
        nb.HasIndex(x => x.ContentHash).IsUnique().HasDatabaseName("idx_news_hash");
        nb.HasIndex(x => x.PublishedAt).IsDescending().HasDatabaseName("idx_news_published");

        var mb = modelBuilder.Entity<MacroEvent>();
        mb.ToTable("macro_events");
        mb.HasKey(x => x.Id);
        mb.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        mb.Property(x => x.EventDate).IsRequired();
        mb.Property(x => x.Event).IsRequired().HasMaxLength(200);
        mb.Property(x => x.Region).IsRequired().HasMaxLength(10);
        mb.Property(x => x.Importance).IsRequired().HasMaxLength(10);
        mb.Property(x => x.Source).IsRequired().HasMaxLength(200);
        mb.Property(x => x.IngestedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        mb.HasIndex(x => new { x.EventDate, x.Region, x.Event }).IsUnique().HasDatabaseName("idx_macro_dedup");
        mb.HasIndex(x => x.EventDate).HasDatabaseName("idx_macro_date");

        var qb = modelBuilder.Entity<QuoteCacheEntry>();
        qb.ToTable("quote_cache");
        qb.HasKey(x => x.Ticker);
        qb.Property(x => x.Ticker).HasMaxLength(20);
        qb.Property(x => x.ResolvedTicker).HasMaxLength(20);
        qb.Property(x => x.Price).HasColumnType("numeric(18,6)");
        qb.Property(x => x.PreviousClose).HasColumnType("numeric(18,6)");
        qb.Property(x => x.Currency).HasMaxLength(6).HasDefaultValue("USD");
        qb.Property(x => x.FetchedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        qb.Property(x => x.MarketState).HasMaxLength(20).HasDefaultValue("unknown");
        qb.Property(x => x.Session).HasMaxLength(20).HasDefaultValue("unknown");
        qb.Property(x => x.IsStale).HasDefaultValue(false);
        qb.Property(x => x.SourcePriceTime);
        qb.Property(x => x.RegularMarketTime);

        var ib = modelBuilder.Entity<InvestmentPolicyStatement>();
        ib.ToTable("investment_policy_statements");
        ib.HasKey(x => x.Id);
        ib.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        ib.Property(x => x.UserId).IsRequired();
        ib.Property(x => x.Version).IsRequired();
        ib.Property(x => x.IsCurrent).IsRequired();
        ib.Property(x => x.PrimaryHorizonYears).IsRequired();
        ib.Property(x => x.EmergencyCushionUsd).HasColumnType("numeric(18,2)");
        ib.Property(x => x.RiskTolerance).IsRequired();
        ib.Property(x => x.MaxDrawdownTolerancePct).HasColumnType("numeric(6,2)");
        ib.Property(x => x.SellDiscipline).HasMaxLength(2000);
        ib.Property(x => x.CoolingOffDays).IsRequired();
        ib.Property(x => x.ReviewCadence).IsRequired().HasMaxLength(20);
        ib.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        ib.Property(x => x.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        ib.Property(x => x.Goals)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<List<InvestmentGoal>>(v, jsonOptions) ?? new());
        ib.Property(x => x.AllocationTargets)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<List<AllocationTarget>>(v, jsonOptions) ?? new());
        ib.Property(x => x.Exclusions)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<List<string>>(v, jsonOptions) ?? new());
        ib.Property(x => x.RebalancingRule)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<RebalancingRule>(v, jsonOptions) ?? RebalancingRule.Default);
        ib.Property(x => x.ContributionPlan)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<ContributionPlan>(v, jsonOptions));
        ib.HasIndex(x => new { x.UserId, x.IsCurrent }).HasDatabaseName("idx_ips_user_current");
        ib.HasIndex(x => new { x.UserId, x.Version }).IsUnique().HasDatabaseName("idx_ips_user_version");

        var teb = modelBuilder.Entity<ThesisEvent>();
        teb.ToTable("thesis_events");
        teb.HasKey(x => x.Id);
        teb.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        teb.Property(x => x.UserId).IsRequired();
        teb.Property(x => x.SubjectType).IsRequired().HasConversion<string>().HasMaxLength(20);
        teb.Property(x => x.SubjectId).IsRequired();
        teb.Property(x => x.Ticker).IsRequired().HasMaxLength(20);
        teb.Property(x => x.EventType).IsRequired().HasConversion<string>().HasMaxLength(20);
        teb.Property(x => x.Timestamp).HasDefaultValueSql("CURRENT_TIMESTAMP");
        teb.Property(x => x.SubjectPrice).HasColumnType("numeric(18,6)");
        teb.Property(x => x.BenchmarkPrice).HasColumnType("numeric(18,6)");
        teb.Property(x => x.BenchmarkTicker).IsRequired().HasMaxLength(20).HasDefaultValue("SPY");
        teb.Property(x => x.PricesPending).IsRequired();
        teb.Property(x => x.DecisionNote).HasMaxLength(4000);
        teb.HasIndex(x => new { x.SubjectType, x.SubjectId, x.Timestamp })
            .HasDatabaseName("idx_thesis_events_subject");
        teb.HasIndex(x => new { x.UserId, x.PricesPending })
            .HasDatabaseName("idx_thesis_events_pending");
        teb.HasIndex(x => new { x.UserId, x.EventType, x.Timestamp })
            .HasDatabaseName("idx_thesis_events_user_type_time");

        var ocb = modelBuilder.Entity<OpportunityCandidate>();
        ocb.ToTable("opportunity_candidates");
        ocb.HasKey(x => x.Id);
        ocb.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        ocb.Property(x => x.UserId).IsRequired();
        ocb.Property(x => x.Ticker).IsRequired().HasMaxLength(20);
        ocb.Property(x => x.Source).IsRequired().HasConversion<string>().HasMaxLength(20);
        ocb.Property(x => x.Status).IsRequired().HasConversion<string>().HasMaxLength(20);
        ocb.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        ocb.Property(x => x.ExpiresAt).IsRequired();
        ocb.Property(x => x.RejectedReason).HasMaxLength(1000);
        ocb.Property(x => x.NominationReasons)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<List<string>>(v, jsonOptions) ?? new())
            .Metadata.SetValueComparer(StringListComparer);
        ocb.HasIndex(x => new { x.UserId, x.Status }).HasDatabaseName("idx_opportunity_candidates_user_status");
        ocb.HasIndex(x => new { x.UserId, x.Ticker }).HasDatabaseName("idx_opportunity_candidates_user_ticker");

        var csb = modelBuilder.Entity<CandidateScore>();
        csb.ToTable("candidate_scores");
        csb.HasKey(x => x.Id);
        csb.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        csb.Property(x => x.CandidateId).IsRequired();
        csb.Property(x => x.ScoredAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        csb.Property(x => x.CrowdingClass).IsRequired().HasConversion<string>().HasMaxLength(20);
        csb.Property(x => x.FormulaVersion).IsRequired();
        csb.Property(x => x.IpsFit)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<IpsFitFacts>(v, jsonOptions) ?? IpsFitFacts.Unknown)
            .Metadata.SetValueComparer(IpsFitFactsComparer);
        csb.Property(x => x.Evidence)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<ScoreEvidence>(v, jsonOptions) ?? ScoreEvidence.Empty)
            .Metadata.SetValueComparer(ScoreEvidenceComparer);
        csb.HasIndex(x => new { x.CandidateId, x.ScoredAt }).HasDatabaseName("idx_candidate_scores_candidate_scored");

        var aab = modelBuilder.Entity<AnalystAction>();
        aab.ToTable("analyst_actions");
        aab.HasKey(x => x.Id);
        aab.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        aab.Property(x => x.Ticker).IsRequired().HasMaxLength(12);
        aab.Property(x => x.Firm).IsRequired().HasMaxLength(120);
        aab.Property(x => x.ActionType).IsRequired().HasConversion<string>().HasMaxLength(20);
        aab.Property(x => x.PriorRating).HasMaxLength(40);
        aab.Property(x => x.NewRating).HasMaxLength(40);
        aab.Property(x => x.PriorTarget).HasColumnType("numeric(18,4)");
        aab.Property(x => x.NewTarget).HasColumnType("numeric(18,4)");
        aab.Property(x => x.ActionDate).IsRequired();
        aab.Property(x => x.Source).IsRequired().HasMaxLength(40);
        aab.Property(x => x.SourceUrl);
        aab.Property(x => x.IngestedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        aab.HasIndex(x => new { x.Ticker, x.Firm, x.ActionDate, x.ActionType })
            .IsUnique().HasDatabaseName("idx_analyst_actions_dedup");
        aab.HasIndex(x => x.ActionDate).IsDescending().HasDatabaseName("idx_analyst_actions_date");
        aab.HasIndex(x => new { x.Ticker, x.ActionDate }).HasDatabaseName("idx_analyst_actions_ticker_date");

        var aumb = modelBuilder.Entity<AnalystUniverseMember>();
        aumb.ToTable("analyst_universe_members");
        aumb.HasKey(x => x.Id);
        aumb.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        aumb.Property(x => x.Ticker).IsRequired().HasMaxLength(12);
        aumb.Property(x => x.Reason).IsRequired().HasConversion<string>().HasMaxLength(20);
        aumb.Property(x => x.Active).IsRequired();
        aumb.Property(x => x.AddedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        aumb.HasIndex(x => x.Ticker).IsUnique().HasDatabaseName("idx_analyst_universe_ticker");

        var nsb = modelBuilder.Entity<NewsSource>();
        nsb.ToTable("news_sources");
        nsb.HasKey(x => x.Id);
        nsb.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        nsb.Property(x => x.Name).IsRequired().HasMaxLength(80);
        nsb.Property(x => x.Kind).IsRequired().HasConversion<string>().HasMaxLength(10);
        nsb.Property(x => x.Url).IsRequired();
        nsb.Property(x => x.Keywords)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<List<string>>(v, jsonOptions) ?? new())
            .Metadata.SetValueComparer(StringListComparer);
        nsb.Property(x => x.ThesisId);
        nsb.Property(x => x.Enabled).IsRequired();
        nsb.Property(x => x.ConsecutiveFailures).IsRequired();
        nsb.Property(x => x.LastSuccessAt);
        nsb.Property(x => x.LastFailureReason);
        nsb.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        nsb.Property(x => x.RetiredAt);
        nsb.Property(x => x.RetiredReason);
        nsb.HasOne<InvestmentThesis>().WithMany().HasForeignKey(x => x.ThesisId)
            .OnDelete(DeleteBehavior.SetNull);
        nsb.HasIndex(x => x.Url).IsUnique().HasDatabaseName("idx_news_sources_url");
        nsb.HasIndex(x => x.ThesisId).HasDatabaseName("idx_news_sources_thesis");

        var vsb = modelBuilder.Entity<ValuationSnapshot>();
        vsb.ToTable("valuation_snapshots");
        vsb.HasKey(x => x.Id);
        vsb.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        vsb.Property(x => x.Ticker).IsRequired().HasMaxLength(12);
        vsb.Property(x => x.CapturedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        vsb.Property(x => x.Price).HasColumnType("numeric(18,4)");
        vsb.Property(x => x.TrailingPe).HasColumnType("numeric(12,4)");
        vsb.Property(x => x.ForwardPe).HasColumnType("numeric(12,4)");
        vsb.Property(x => x.EvToEbitda).HasColumnType("numeric(12,4)");
        vsb.Property(x => x.DividendYield).HasColumnType("numeric(8,6)");
        vsb.Property(x => x.ConsensusTarget).HasColumnType("numeric(18,4)");
        vsb.Property(x => x.IsStale).IsRequired();
        vsb.HasIndex(x => new { x.Ticker, x.CapturedAt }).HasDatabaseName("idx_valuation_snapshots_ticker_captured");

        var rtb = modelBuilder.Entity<RecommendationTrend>();
        rtb.ToTable("recommendation_trends");
        rtb.HasKey(x => x.Id);
        rtb.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        rtb.Property(x => x.Ticker).IsRequired().HasMaxLength(12);
        rtb.Property(x => x.Period).IsRequired();
        rtb.Property(x => x.StrongBuy).IsRequired();
        rtb.Property(x => x.Buy).IsRequired();
        rtb.Property(x => x.Hold).IsRequired();
        rtb.Property(x => x.Sell).IsRequired();
        rtb.Property(x => x.StrongSell).IsRequired();
        rtb.Property(x => x.Source).IsRequired().HasMaxLength(40);
        rtb.Property(x => x.IngestedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        rtb.HasIndex(x => new { x.Ticker, x.Period })
            .IsUnique().HasDatabaseName("idx_recommendation_trends_ticker_period");

        var rdb = modelBuilder.Entity<ResearchDocument>();
        rdb.ToTable("research_documents");
        rdb.HasKey(x => x.Id);
        rdb.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        rdb.Property(x => x.SourceType).IsRequired().HasConversion<string>().HasMaxLength(40);
        rdb.Property(x => x.SourceId).IsRequired().HasMaxLength(80);
        rdb.Property(x => x.Title).IsRequired().HasMaxLength(500);
        rdb.Property(x => x.SourceName).HasMaxLength(160);
        rdb.Property(x => x.ContentHash).IsRequired().HasMaxLength(128);
        rdb.Property(x => x.Text).IsRequired();
        rdb.Property(x => x.CapturedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        rdb.Property(x => x.IndexStatus).IsRequired().HasConversion<string>().HasMaxLength(30);
        rdb.Property(x => x.Tickers)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<List<string>>(v, jsonOptions) ?? new())
            .Metadata.SetValueComparer(StringListComparer);
        rdb.Property(x => x.ThesisIds)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<List<Guid>>(v, jsonOptions) ?? new())
            .Metadata.SetValueComparer(GuidListComparer);
        // Postgres 14 treats NULLs as distinct in unique indexes, so this cannot dedupe two global
        // (null-user) rows by itself; the indexer upserts by this identity before inserting.
        rdb.HasIndex(x => new { x.SourceType, x.SourceId, x.UserId })
            .IsUnique().HasDatabaseName("idx_research_documents_source_identity");
        rdb.HasIndex(x => new { x.IndexStatus, x.CapturedAt }).HasDatabaseName("idx_research_documents_status_captured");
        rdb.HasIndex(x => x.PublishedAt).IsDescending().HasDatabaseName("idx_research_documents_published");

        var rcb = modelBuilder.Entity<ResearchChunk>();
        rcb.ToTable("research_chunks");
        rcb.HasKey(x => x.Id);
        rcb.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        rcb.Property(x => x.Text).IsRequired();
        rcb.Property(x => x.ContentHash).IsRequired().HasMaxLength(128);
        rcb.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        rcb.HasOne<ResearchDocument>().WithMany().HasForeignKey(x => x.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
        rcb.HasIndex(x => new { x.DocumentId, x.Ordinal, x.ContentHash })
            .IsUnique().HasDatabaseName("idx_research_chunks_doc_ordinal_hash");

        var reb = modelBuilder.Entity<ResearchEmbedding>();
        reb.ToTable("research_embeddings");
        reb.HasKey(x => x.Id);
        reb.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        reb.Property(x => x.Provider).IsRequired().HasMaxLength(60);
        reb.Property(x => x.Model).IsRequired().HasMaxLength(120);
        reb.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        // float[] maps to real[] on Npgsql — no extension required; similarity ranks in-app.
        reb.Property(x => x.Vector).IsRequired().Metadata.SetValueComparer(FloatArrayComparer);
        reb.HasOne<ResearchChunk>().WithMany().HasForeignKey(x => x.ChunkId)
            .OnDelete(DeleteBehavior.Cascade);
        reb.HasIndex(x => new { x.ChunkId, x.Provider, x.Model, x.EmbeddingVersion })
            .IsUnique().HasDatabaseName("idx_research_embeddings_chunk_provider_model_version");

        var alr = modelBuilder.Entity<AssetLedgerRead>();
        alr.ToTable("asset_ledger_reads");
        alr.HasKey(x => x.Id);
        alr.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
        alr.Property(x => x.UserId).IsRequired();
        alr.Property(x => x.Symbol).IsRequired().HasMaxLength(20);
        alr.Property(x => x.Narrative).IsRequired();
        alr.Property(x => x.SourceFingerprint).IsRequired().HasMaxLength(64);
        alr.Property(x => x.GeneratedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        alr.HasIndex(x => new { x.UserId, x.Symbol })
            .IsUnique().HasDatabaseName("idx_asset_ledger_reads_user_symbol");
    }

    private static readonly ValueComparer<List<string>> StringListComparer = new(
        (a, b) => (a ?? new()).SequenceEqual(b ?? new()),
        v => v.Aggregate(0, (hash, s) => HashCode.Combine(hash, s.GetHashCode())),
        v => v.ToList());

    private static readonly ValueComparer<List<Guid>> GuidListComparer = new(
        (a, b) => (a ?? new()).SequenceEqual(b ?? new()),
        v => v.Aggregate(0, (hash, g) => HashCode.Combine(hash, g.GetHashCode())),
        v => v.ToList());

    private static readonly ValueComparer<float[]> FloatArrayComparer = new(
        (a, b) => (a ?? Array.Empty<float>()).SequenceEqual(b ?? Array.Empty<float>()),
        v => v.Aggregate(0, (hash, f) => HashCode.Combine(hash, f.GetHashCode())),
        v => v.ToArray());

    private static readonly ValueComparer<IpsFitFacts> IpsFitFactsComparer = new(
        (a, b) => Equals(a, b),
        v => v == null ? 0 : v.GetHashCode(),
        v => v);

    private static readonly ValueComparer<ScoreEvidence> ScoreEvidenceComparer = new(
        (a, b) => Equals(a, b),
        v => v == null ? 0 : v.GetHashCode(),
        v => v);
}
