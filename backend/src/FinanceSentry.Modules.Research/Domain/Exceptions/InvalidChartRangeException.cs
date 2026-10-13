namespace FinanceSentry.Modules.Research.Domain.Exceptions;

using FinanceSentry.Core.Exceptions;

public class InvalidChartRangeException(string? range)
    : ApiException(400, "INVALID_CHART_RANGE", $"Unsupported chart range '{range}'; use 1M, 6M, YTD, 1Y, 5Y or MAX.");
