namespace FinanceSentry.Modules.Wealth.Domain.Exceptions;

using FinanceSentry.Core.Exceptions;

public class CareerEventNotFoundException()
    : ApiException(404, "CAREER_EVENT_NOT_FOUND", "Career event not found.");
