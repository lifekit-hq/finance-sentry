namespace FinanceSentry.Modules.BankSync.Domain.Repositories;

public interface IPaydayTransferSettingsRepository
{
    /// <summary>The user's stored settings, or null when they never saved any (callers fall back to <see cref="PaydayTransferDefaults"/>).</summary>
    Task<PaydayTransferSettings?> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Creates or updates the user's single settings row.</summary>
    Task<PaydayTransferSettings> UpsertAsync(
        Guid userId, decimal fixedOutflowUah, decimal roundingStepEur, CancellationToken cancellationToken = default);
}
