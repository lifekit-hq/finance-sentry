using FinanceSentry.Modules.Radar.Domain;

namespace FinanceSentry.Modules.Radar.Application.Services;

public interface IBookIncomeService
{
    /// <summary>Income received by the user's book, trailing 12 months and year to date, by holding, with the forward estimate and yield on cost.</summary>
    Task<BookIncome> GetAsync(Guid userId, CancellationToken ct = default);
}
