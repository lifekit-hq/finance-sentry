namespace FinanceSentry.Modules.Research.Tests.ReferenceUniverse;

using System.Net;
using FinanceSentry.Modules.Research.Application.Services.ReferenceUniverse;
using FinanceSentry.Core.Interfaces;
using FinanceSentry.Modules.Research.Infrastructure.Sources;
using FinanceSentry.Modules.Research.Infrastructure.Persistence;
using FinanceSentry.Modules.Research.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

/// <summary>The checked-in S&amp;P 500 seed as a port, for tests that need a realistic universe without a database.</summary>
internal sealed class EmbeddedSp500Seed : IIndexConstituentSource
{
    public IReadOnlyList<string> GetConstituents() => Sp500ConstituentSource.EmbeddedSeed;
}

internal sealed class ReferenceClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset now = start;

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan by) => now += by;
}

/// <summary>A feed whose payload (or failure) the test scripts.</summary>
internal sealed class ScriptedFeed(string source) : IReferenceListFeed
{
    public Func<IReadOnlyList<ReferenceListSnapshot>> Next { get; set; } = () => [];

    public string Source => source;

    public Task<IReadOnlyList<ReferenceListSnapshot>> FetchAsync(CancellationToken ct = default)
        => Task.FromResult(Next());

    public static ReferenceListSnapshot List(string key, params string[] tickers)
        => new(key, tickers.Select(t => new ReferenceMember(t, null)).ToList());
}

/// <summary>An <see cref="IHttpClientFactory"/> answering from a url -> body map; anything unmapped is a 404.</summary>
internal sealed class StubHttpClientFactory(Dictionary<string, string> bodies) : IHttpClientFactory
{
    public List<string> Requested { get; } = [];

    public HttpClient CreateClient(string name) => new(new Handler(this));

    private sealed class Handler(StubHttpClientFactory owner) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            owner.Requested.Add(url);
            return Task.FromResult(owner.bodies.TryGetValue(url, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private readonly Dictionary<string, string> bodies = bodies;
}

internal static class ReferenceUniverseTestKit
{
    public static readonly DateTimeOffset Start = new(2026, 11, 15, 4, 0, 0, TimeSpan.Zero);

    public static ResearchDbContext NewDb()
        => new(new DbContextOptionsBuilder<ResearchDbContext>()
            .UseInMemoryDatabase($"reference-universe-{Guid.NewGuid():N}")
            .Options, new FixedCurrentUser(null));

    public static ReferenceUniverseRefresher Refresher(
        ResearchDbContext db, ReferenceClock clock, ReferenceUniverseOptions? options, params IReferenceListFeed[] feeds)
        => new(feeds, new ReferenceUniverseRepository(db), Options.Create(options ?? new ReferenceUniverseOptions()),
            clock, Microsoft.Extensions.Logging.Abstractions.NullLogger<ReferenceUniverseRefresher>.Instance);

    public static ReferenceUniverseReader Reader(ResearchDbContext db, ReferenceClock clock)
        => new(new ReferenceUniverseRepository(db), Options.Create(new ReferenceUniverseOptions()), clock);

    public static string Sp500Csv(params (string Symbol, string Name)[] rows)
        => "Symbol,Security,GICS Sector,GICS Sub-Industry,Headquarters Location,Date added,CIK,Founded\n"
            + string.Concat(rows.Select(r => $"{r.Symbol},{r.Name},Industrials,Foo,\"City, ST\",2000-01-01,1,1900\n"));

    public static string Nasdaq100Csv(params (string Symbol, string Name)[] rows)
        => "﻿symbol,name,opt-in\n" + string.Concat(rows.Select(r => $"{r.Symbol},\"{r.Name}\",2006-01-01\n"));

    public static string FinanceDatabaseCsv(params (string Symbol, string Name, string Sector, string Industry, string Delisted)[] rows)
        => "symbol,name,summary,currency,sector,industry_group,industry,exchange,mic,market,country,delisted\n"
            + string.Concat(rows.Select(r =>
                $"{r.Symbol},\"{r.Name}\",\"Makes things, \"\"many\"\" things.\nOn two lines.\",USD,{r.Sector},Group,{r.Industry},NMS,XNAS,NASDAQ,United States,{r.Delisted}\n"));
}
