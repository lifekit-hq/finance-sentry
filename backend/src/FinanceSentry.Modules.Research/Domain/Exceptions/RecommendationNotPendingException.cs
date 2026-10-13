namespace FinanceSentry.Modules.Research.Domain.Exceptions;

using FinanceSentry.Core.Exceptions;

/// <summary>The proposal is not pending any more (already decided, expired or superseded).</summary>
public class RecommendationNotPendingException(string status)
    : ApiException(409, "RECOMMENDATION_NOT_PENDING", $"This recommendation is already {status.ToLowerInvariant()}.");
