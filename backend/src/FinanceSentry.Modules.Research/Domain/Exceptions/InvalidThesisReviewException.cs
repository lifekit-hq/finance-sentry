namespace FinanceSentry.Modules.Research.Domain.Exceptions;

using FinanceSentry.Core.Exceptions;

/// <summary>A thesis review that cannot be journaled as given (blank or over-long verdict or rationale).</summary>
public class InvalidThesisReviewException(string reason)
    : ApiException(400, "INVALID_THESIS_REVIEW", $"Invalid thesis review: {reason}");
