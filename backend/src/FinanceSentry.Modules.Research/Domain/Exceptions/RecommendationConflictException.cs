namespace FinanceSentry.Modules.Research.Domain.Exceptions;

using FinanceSentry.Core.Exceptions;

/// <summary>Another pending proposal on the ticker landed at the same moment.</summary>
public class RecommendationConflictException(string ticker)
    : ApiException(409, "RECOMMENDATION_CONFLICT", $"Another recommendation on {ticker} was proposed at the same time.");
