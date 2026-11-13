namespace FinanceSentry.Modules.Research.Domain.Exceptions;

using FinanceSentry.Core.Exceptions;

public class InvalidNarrativeFalsifierException(string detail)
    : ApiException(422, "INVALID_NARRATIVE_FALSIFIER", detail);
