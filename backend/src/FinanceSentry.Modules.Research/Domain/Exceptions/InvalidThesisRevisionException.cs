namespace FinanceSentry.Modules.Research.Domain.Exceptions;

using FinanceSentry.Core.Exceptions;

/// <summary>A proposal or decision that breaks a rule of the propose-then-confirm flow.</summary>
public class InvalidThesisRevisionException(string reason)
    : ApiException(400, "INVALID_THESIS_REVISION", $"Invalid thesis revision: {reason}");
