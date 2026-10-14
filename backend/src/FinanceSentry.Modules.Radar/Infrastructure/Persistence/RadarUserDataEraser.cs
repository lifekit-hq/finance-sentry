namespace FinanceSentry.Modules.Radar.Infrastructure.Persistence;

using FinanceSentry.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Erases the person's own radar signals. A signal with no <c>UserId</c> is a global one every person reads and stays.
/// </summary>
internal sealed class RadarUserDataEraser(RadarDbContext db) : IUserDataEraser
{
    public string Module => "Radar";

    public Task<int> EraseAsync(Guid userId, CancellationToken ct = default) =>
        db.RadarSignals.IgnoreQueryFilters().Where(s => s.UserId == userId).ExecuteDeleteAsync(ct);
}
