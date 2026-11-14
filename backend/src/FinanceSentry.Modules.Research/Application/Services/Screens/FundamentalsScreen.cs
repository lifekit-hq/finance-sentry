namespace FinanceSentry.Modules.Research.Application.Services.Screens;

using System.Globalization;
using FinanceSentry.Modules.Research.Application.Services.Coverage;
using FinanceSentry.Modules.Research.Application.Services.Fundamentals;
using FinanceSentry.Modules.Research.Domain.Screens;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// The fundamentals screen (discovery funnel stage 5): revenue growth, operating cash flow margin, capex intensity and
/// equity of the latest fiscal year over the reference lists, from the EDGAR facts the fundamentals chain serves
/// (<see cref="SecEdgarService"/> behind it keeps SEC's rate limit with its concurrency cap and caches). The list is read
/// in rotating daily slices of <c>BatchSize</c>, so one run costs a bounded number of EDGAR reads however long the
/// universe is. No model call.
/// </summary>
public sealed class FundamentalsScreen(
    ScreenUniverse universe,
    IFundamentalsService fundamentals,
    IOptions<ScreenOptions> options,
    ILogger<FundamentalsScreen> logger) : IScreen
{
    private const int MinutesPerDay = 24 * 60;
    private const decimal Percent = 100m;

    public string Tag => ScreenTags.Fundamentals;

    public bool Enabled => options.Value.Fundamentals.Enabled;

    public int MaxNominationsPerRun => options.Value.Fundamentals.MaxNominationsPerRun;

    public async Task<ScreenRun> RunAsync(DateTimeOffset asOf, CancellationToken ct = default)
    {
        var settings = options.Value.Fundamentals;
        var thresholds = new FundamentalsScreenThresholds(
            settings.MinRevenueGrowth, settings.MinOperatingCashFlowMargin, settings.MaxCapexIntensity,
            settings.MinStockholdersEquity, settings.MaxFactAgeDays);
        var today = DateOnly.FromDateTime(asOf.UtcDateTime);

        var slice = TickerRotation.Slice(await universe.TickersAsync(ct), settings.BatchSize, TickerRotation.RunIndex(asOf, MinutesPerDay));
        var hits = new List<(ScreenHit Hit, decimal Growth)>();
        var failures = 0;
        var first = true;
        foreach (var ticker in slice)
        {
            if (!first && settings.PauseBetweenTickersMs > 0)
            {
                await Task.Delay(settings.PauseBetweenTickersMs, ct);
            }

            first = false;
            try
            {
                var facts = (await fundamentals.GetFundamentalsAsync(ticker, settings.FactsPerConcept, ct)).Facts;
                var reading = FundamentalsScreenRules.Evaluate(facts, thresholds, today);
                if (reading is { Passed: true, PeriodEnd: { } periodEnd })
                {
                    var evidence = new DateTimeOffset(periodEnd.AddDays(settings.FilingLagDays).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                    hits.Add((new ScreenHit(Tag, ticker, [], Describe(reading, periodEnd), evidence), reading.RevenueGrowth ?? 0m));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures++;
                logger.LogWarning(ex, "Fundamentals screen could not read {Ticker}; it is skipped this run", ticker);
            }
        }

        // One name failing is a skipped name; every name failing is the source down, which must not read as "nothing passed".
        if (slice.Count > 0 && failures == slice.Count)
        {
            throw new InvalidOperationException($"The fundamentals screen could not read any of its {slice.Count} names; the EDGAR source looks down.");
        }

        return new ScreenRun(
            Tag,
            [.. hits.OrderByDescending(h => h.Growth).ThenBy(h => h.Hit.Ticker, StringComparer.Ordinal).Select(h => h.Hit)],
            slice.Count,
            null);
    }

    private static string Describe(FundamentalsScreenReading reading, DateOnly periodEnd)
        => string.Create(CultureInfo.InvariantCulture,
            $"Fiscal year ended {periodEnd:yyyy-MM-dd}: revenue growth {Pct(reading.RevenueGrowth)}, operating cash flow margin {Pct(reading.OperatingCashFlowMargin)}, capex intensity {Pct(reading.CapexIntensity)}, equity positive.");

    private static string Pct(decimal? value)
        => value is { } v ? string.Create(CultureInfo.InvariantCulture, $"{v * Percent:0.#}%") : "n/a";
}
