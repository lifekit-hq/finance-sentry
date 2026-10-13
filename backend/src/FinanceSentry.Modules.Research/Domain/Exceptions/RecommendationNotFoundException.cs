namespace FinanceSentry.Modules.Research.Domain.Exceptions;

using FinanceSentry.Core.Exceptions;

public class RecommendationNotFoundException()
    : ApiException(404, "RECOMMENDATION_NOT_FOUND", "Recommendation not found.");
