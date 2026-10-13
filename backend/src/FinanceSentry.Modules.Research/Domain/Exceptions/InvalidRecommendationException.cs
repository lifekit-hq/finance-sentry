namespace FinanceSentry.Modules.Research.Domain.Exceptions;

using FinanceSentry.Core.Exceptions;

/// <summary>A proposal that breaks a rule of the decision loop: no trigger behind it, no position to trim, no size.</summary>
public class InvalidRecommendationException(string reason)
    : ApiException(400, "INVALID_RECOMMENDATION", $"Invalid recommendation: {reason}");
