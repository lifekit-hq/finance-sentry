namespace FinanceSentry.Modules.BankSync.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain.Repositories;

/// <summary>
/// Drops a committed pin. Addressed by merchant text rather than by pin id, so unpinning is the
/// exact mirror of pinning and needs no listing round trip first — the normalized key is the
/// pin's identity, and it is unique per user.
/// </summary>
public sealed record UnpinCommittedMerchantCommand(Guid UserId, string Merchant) : ICommand<bool>;

public sealed class UnpinCommittedMerchantCommandHandler(ICommittedMerchantPinRepository pins)
    : ICommandHandler<UnpinCommittedMerchantCommand, bool>
{
    private readonly ICommittedMerchantPinRepository _pins =
        pins ?? throw new ArgumentNullException(nameof(pins));

    /// <summary>
    /// Accepts either the merchant text the pin was created with or the <c>MerchantKey</c> a
    /// listing returned — <see cref="MerchantNameNormalizer.NormalizeDetectionKey"/> is a fixed
    /// point over its own output, so both derive to the one key the pin is stored under.
    /// Reports false when the user holds no pin for that key.
    /// </summary>
    public Task<bool> Handle(UnpinCommittedMerchantCommand command, CancellationToken ct)
        => _pins.RemoveAsync(command.UserId, CommittedMerchantKey.Derive(command.Merchant), ct);
}
