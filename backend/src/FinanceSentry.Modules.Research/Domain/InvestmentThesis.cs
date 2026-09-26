namespace FinanceSentry.Modules.Research.Domain;

public class InvestmentThesis
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Ticker { get; set; } = string.Empty;
    public string ThesisText { get; set; } = string.Empty;
    // Anchor for price_drawdown triggers. If null, drawdown falls back to the first daily close on or after CreatedAt.
    public decimal? EntryPrice { get; set; }
    public List<ThesisDataPoint> KeyDataPoints { get; set; } = [];
    public List<ThesisCatalyst> Catalysts { get; set; } = [];
    public List<ThesisInvalidationTrigger> InvalidationTriggers { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? BrokenAt { get; set; }
    public string? BrokenReason { get; set; }
}

public record ThesisDataPoint(string Metric, string Direction, decimal Threshold, string Source);

public record ThesisCatalyst(DateOnly Date, string Event);

public record ThesisInvalidationTrigger(
    string Metric,
    string Direction,
    decimal Threshold,
    string? ProxyTicker = null,
    int ConsecutivePeriods = 1,
    ThesisPeriodType PeriodType = ThesisPeriodType.Quarter,
    // relative_return only (#697): the benchmark ticker (e.g. "SPY") and trailing trading-day
    // window the subject's return is measured against.
    string? BenchmarkTicker = null,
    int? WindowDays = null);

public enum ThesisPeriodType
{
    Quarter,
    Annual,
}
