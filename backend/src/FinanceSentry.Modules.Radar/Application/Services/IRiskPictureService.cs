using FinanceSentry.Modules.Radar.Domain;

namespace FinanceSentry.Modules.Radar.Application.Services;

public interface IRiskPictureService
{
    Task<RiskPicture> GetAsync(Guid userId, CancellationToken ct = default);
}
