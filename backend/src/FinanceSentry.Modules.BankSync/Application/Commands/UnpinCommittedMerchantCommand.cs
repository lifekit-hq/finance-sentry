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

    /// <summary>Returns false when the user held no pin for that merchant.</summary>
    public Task<bool> Handle(UnpinCommittedMerchantCommand command, CancellationToken ct)
        => _pins.RemoveAsync(command.UserId, CommittedMerchantKey.Derive(command.Merchant), ct);
}
