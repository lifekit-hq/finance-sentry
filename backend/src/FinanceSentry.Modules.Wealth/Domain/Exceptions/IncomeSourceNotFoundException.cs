namespace FinanceSentry.Modules.Wealth.Domain.Exceptions;

using FinanceSentry.Core.Exceptions;

public class IncomeSourceNotFoundException()
    : ApiException(404, "INCOME_SOURCE_NOT_FOUND", "Income source not found.");
