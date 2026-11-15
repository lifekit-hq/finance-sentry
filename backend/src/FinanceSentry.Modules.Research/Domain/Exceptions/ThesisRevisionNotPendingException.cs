namespace FinanceSentry.Modules.Research.Domain.Exceptions;

using FinanceSentry.Core.Exceptions;

/// <summary>The proposal is not pending any more (already decided, expired or superseded).</summary>
public class ThesisRevisionNotPendingException(string status)
    : ApiException(409, "THESIS_REVISION_NOT_PENDING", $"This thesis revision is already {status.ToLowerInvariant()}.");
