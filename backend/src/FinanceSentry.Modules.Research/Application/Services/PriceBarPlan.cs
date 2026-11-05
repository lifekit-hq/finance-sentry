namespace FinanceSentry.Modules.Research.Application.Services;

/// <summary>The chart periods the interactive price chart offers.</summary>
public static class PricePeriod
{
    public const string OneDay = "1D";
    public const string FiveDays = "5D";
    public const string OneMonth = "1M";
    public const string ThreeMonths = "3M";
    public const string SixMonths = "6M";
    public const string YearToDate = "YTD";
    public const string OneYear = "1Y";
    public const string FiveYears = "5Y";
    public const string All = "ALL";

    public static IReadOnlyList<string> Values { get; } =
        [OneDay, FiveDays, OneMonth, ThreeMonths, SixMonths, YearToDate, OneYear, FiveYears, All];

    /// <summary>The canonical period for <paramref name="value"/> (case-insensitive), or null when it is not one.</summary>
    public static string? Normalize(string? value)
        => Values.FirstOrDefault(p => p.Equals(value?.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>The candle intervals the interactive price chart offers, as the API names them.</summary>
public static class PriceInterval
{
    public const string FiveMinutes = "5m";
    public const string FifteenMinutes = "15m";
    public const string OneHour = "1h";
    public const string OneDay = "1d";
    public const string OneWeek = "1w";
    public const string OneMonth = "1mo";

    public static IReadOnlyList<string> Values { get; } = [FiveMinutes, FifteenMinutes, OneHour, OneDay, OneWeek, OneMonth];

    public static string? Normalize(string? value)
        => Values.FirstOrDefault(i => i.Equals(value?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static bool IsIntraday(string interval)
        => interval is FiveMinutes or FifteenMinutes or OneHour;

    /// <summary>The interval token Yahoo's chart endpoint takes.</summary>
    public static string ToSource(string interval) => interval switch
    {
        FiveMinutes => "5m",
        FifteenMinutes => "15m",
        OneHour => "60m",
        OneWeek => "1wk",
        OneMonth => "1mo",
        _ => "1d",
    };
}

/// <summary>
/// What the price source can honestly serve, and how a request maps onto it: which intervals each period
/// offers (intraday bars only exist for the recent past, so a long period offers none), the source range that
/// covers the period plus the moving-average warm-up, the bars inside the period's window, and the 52-week range.
/// </summary>
public static class PriceBarPlan
{
    /// <summary>Bars kept before the window so the longest moving average (200) is defined from the window's first bar.</summary>
    public const int WarmupBars = 200;

    /// <summary>The source range the 52-week high and low are read from: daily bars over two years.</summary>
    public const string WeekRangeSourceRange = "2y";

    private const int WeeksInYear = 52;
    private const int DaysInWeek = 7;
    private const int FiveSessions = 5;
    private const int MonthsInYear = 12;
    private const int FiveYears = 5;

    private static readonly Dictionary<string, string[]> Allowed = new()
    {
        [PricePeriod.OneDay] = [PriceInterval.FiveMinutes, PriceInterval.FifteenMinutes, PriceInterval.OneHour],
        [PricePeriod.FiveDays] = [PriceInterval.FifteenMinutes, PriceInterval.OneHour, PriceInterval.OneDay],
        [PricePeriod.OneMonth] = [PriceInterval.OneHour, PriceInterval.OneDay],
        [PricePeriod.ThreeMonths] = [PriceInterval.OneDay, PriceInterval.OneWeek],
        [PricePeriod.SixMonths] = [PriceInterval.OneDay, PriceInterval.OneWeek],
        [PricePeriod.YearToDate] = [PriceInterval.OneDay, PriceInterval.OneWeek],
        [PricePeriod.OneYear] = [PriceInterval.OneDay, PriceInterval.OneWeek],
        [PricePeriod.FiveYears] = [PriceInterval.OneDay, PriceInterval.OneWeek, PriceInterval.OneMonth],
        [PricePeriod.All] = [PriceInterval.OneWeek, PriceInterval.OneMonth],
    };

    private static readonly Dictionary<string, string> Defaults = new()
    {
        [PricePeriod.OneDay] = PriceInterval.FiveMinutes,
        [PricePeriod.FiveDays] = PriceInterval.FifteenMinutes,
        [PricePeriod.OneMonth] = PriceInterval.OneDay,
        [PricePeriod.ThreeMonths] = PriceInterval.OneDay,
        [PricePeriod.SixMonths] = PriceInterval.OneDay,
        [PricePeriod.YearToDate] = PriceInterval.OneDay,
        [PricePeriod.OneYear] = PriceInterval.OneDay,
        [PricePeriod.FiveYears] = PriceInterval.OneWeek,
        [PricePeriod.All] = PriceInterval.OneMonth,
    };

    /// <summary>Intervals the source has bars for over <paramref name="period"/>, finest first.</summary>
    public static IReadOnlyList<string> AllowedIntervals(string period) => Allowed[period];

    public static string DefaultInterval(string period) => Defaults[period];

    /// <summary>
    /// The source range that covers <paramref name="period"/> plus <see cref="WarmupBars"/> at <paramref name="interval"/>.
    /// Intraday bars are served only for a short past (5m and 15m ~60 days, hourly ~2 years), so those ranges stay short.
    /// </summary>
    public static string SourceRange(string period, string interval) => interval switch
    {
        PriceInterval.FiveMinutes => "5d",
        PriceInterval.FifteenMinutes => "1mo",
        PriceInterval.OneHour => "6mo",
        PriceInterval.OneDay => period == PricePeriod.FiveYears ? "10y" : "2y",
        PriceInterval.OneWeek => period is PricePeriod.All ? "max" : "10y",
        _ => "max",
    };

    /// <summary>
    /// The window of <paramref name="period"/> inside <paramref name="bars"/>: the index of its first bar, and whether
    /// the series starts later than the period asks (a young listing), so the client can say the history starts there.
    /// A daily-or-longer series is judged against the period's nominal start with a slack of one interval.
    /// </summary>
    public static (int FirstIndex, bool Truncated) Window(IReadOnlyList<PriceBar> bars, string period, string interval)
    {
        if (bars.Count == 0)
        {
            return (0, false);
        }

        var last = bars[^1].Date;
        var start = NominalStart(bars, period, last);
        var first = 0;
        while (first < bars.Count - 1 && bars[first].Date < start)
        {
            first++;
        }

        var truncated = !PriceInterval.IsIntraday(interval)
            && period is not (PricePeriod.All or PricePeriod.OneDay or PricePeriod.FiveDays)
            && bars[0].Date > start.AddDays(SlackDays(interval));
        return (first, truncated);
    }

    /// <summary>The bars to send: the window plus at most <see cref="WarmupBars"/> before it.</summary>
    public static (IReadOnlyList<PriceBar> Bars, long WindowStart, bool Truncated) Trim(
        IReadOnlyList<PriceBar> bars, string period, string interval)
    {
        if (bars.Count == 0)
        {
            return ([], 0, false);
        }

        var (first, truncated) = Window(bars, period, interval);
        var from = Math.Max(0, first - WarmupBars);
        return (bars.Skip(from).ToList(), bars[first].Time, truncated);
    }

    /// <summary>
    /// The highest high and lowest low of the 52 weeks ending at the last daily bar, and whether the series
    /// reaches back that far (a younger listing reports the range since its first bar). Null for an empty series.
    /// </summary>
    public static (decimal High, decimal Low, DateOnly From, bool CoversYear)? WeekRange(IReadOnlyList<PriceBar> daily)
    {
        if (daily.Count == 0)
        {
            return null;
        }

        var last = daily[^1].Date;
        var floor = last.AddDays(-(WeeksInYear * DaysInWeek));
        var inside = daily.Where(b => b.Date > floor).ToList();
        return (inside.Max(b => b.High), inside.Min(b => b.Low), inside[0].Date,
            daily[0].Date <= floor.AddDays(DaysInWeek));
    }

    private static DateOnly NominalStart(IReadOnlyList<PriceBar> bars, string period, DateOnly last) => period switch
    {
        PricePeriod.OneDay => last,
        PricePeriod.FiveDays => FifthSession(bars),
        PricePeriod.OneMonth => last.AddMonths(-1),
        PricePeriod.ThreeMonths => last.AddMonths(-3),
        PricePeriod.SixMonths => last.AddMonths(-6),
        PricePeriod.YearToDate => new DateOnly(last.Year, 1, 1),
        PricePeriod.OneYear => last.AddMonths(-MonthsInYear),
        PricePeriod.FiveYears => last.AddMonths(-FiveYears * MonthsInYear),
        _ => bars[0].Date,
    };

    private static DateOnly FifthSession(IReadOnlyList<PriceBar> bars)
    {
        var sessions = bars.Select(b => b.Date).Distinct().ToList();
        return sessions[Math.Max(0, sessions.Count - FiveSessions)];
    }

    private static int SlackDays(string interval) => interval switch
    {
        PriceInterval.OneWeek => 10,
        PriceInterval.OneMonth => 35,
        _ => 5,
    };
}
