namespace FinanceSentry.Tests.Unit.Wealth;

using FinanceSentry.Modules.Wealth.Domain;
using FinanceSentry.Modules.Wealth.Domain.Repositories;

/// <summary>
/// An in-memory career-event repository for handler tests. <see cref="CanReadEncryptedFields"/> is settable so a test can
/// play the MCP host, which holds no encryption key. Every read returns the person's rows regardless of the Owner filter,
/// which the persistence tests cover.
/// </summary>
internal sealed class FakeCareerEventRepository : ICareerEventRepository
{
    public List<CareerEvent> Events { get; } = [];

    public bool CanReadEncryptedFields { get; set; } = true;

    public Task<IReadOnlyList<CareerEvent>> ListAsync(Guid userId, CancellationToken ct = default) => Rows(userId);

    public Task<IReadOnlyList<CareerEvent>> ListUnscopedAsync(Guid userId, CancellationToken ct = default) => Rows(userId);

    public Task<IReadOnlyList<CareerEventOutline>> ListOutlinesAsync(Guid userId, CancellationToken ct = default) => Outlines(userId);

    public Task<IReadOnlyList<CareerEventOutline>> ListOutlinesUnscopedAsync(Guid userId, CancellationToken ct = default) => Outlines(userId);

    public Task<CareerEvent?> GetAsync(Guid userId, Guid id, CancellationToken ct = default)
        => Task.FromResult(Events.FirstOrDefault(e => e.Id == id && e.UserId == userId));

    public Task AddAsync(CareerEvent career, CancellationToken ct = default)
    {
        Events.Add(career);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(CareerEvent career, CancellationToken ct = default)
    {
        Events.Remove(career);
        return Task.CompletedTask;
    }

    public Task SaveAsync(CancellationToken ct = default) => Task.CompletedTask;

    private Task<IReadOnlyList<CareerEvent>> Rows(Guid userId)
        => Task.FromResult<IReadOnlyList<CareerEvent>>([.. Events.Where(e => e.UserId == userId).OrderBy(e => e.Date)]);

    private Task<IReadOnlyList<CareerEventOutline>> Outlines(Guid userId)
        => Task.FromResult<IReadOnlyList<CareerEventOutline>>(
            [.. Events.Where(e => e.UserId == userId).OrderBy(e => e.Date)
                .Select(e => new CareerEventOutline(e.Id, e.Kind, e.Date, e.EndDate, e.Currency, e.RetiredAt))]);
}
