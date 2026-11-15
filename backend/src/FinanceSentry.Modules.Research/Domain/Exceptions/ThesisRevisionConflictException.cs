namespace FinanceSentry.Modules.Research.Domain.Exceptions;

using FinanceSentry.Core.Exceptions;

/// <summary>Another pending proposal on the subject landed at the same moment.</summary>
public class ThesisRevisionConflictException(string ticker)
    : ApiException(409, "THESIS_REVISION_CONFLICT", $"Another revision of {ticker} was proposed at the same time.");
