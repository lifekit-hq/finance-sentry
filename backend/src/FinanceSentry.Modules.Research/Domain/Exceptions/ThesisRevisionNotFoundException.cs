namespace FinanceSentry.Modules.Research.Domain.Exceptions;

using FinanceSentry.Core.Exceptions;

public class ThesisRevisionNotFoundException()
    : ApiException(404, "THESIS_REVISION_NOT_FOUND", "Thesis revision not found.");
