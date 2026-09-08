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
    /// listing returned, and reports false when the user held neither.
    /// <para>
    /// Both are accepted because normalization is not a fixed point over every key it emits:
    /// pinning <c>"*MOBI TOP-UP 0857860057"</c> stores <c>mobile top-up 0057</c>, and re-deriving
    /// THAT strips the trailing digits to <c>mobile top-up</c>. Since the listing advertises the
    /// key and list-then-unpin is the obvious client flow (it is the only one an MCP caller
    /// has), a derived-key-only lookup would 404 on the pin it just showed.
    /// </para>
    /// </summary>
    public async Task<bool> Handle(UnpinCommittedMerchantCommand command, CancellationToken ct)
    {
        // Derive first: it is the mirror of pinning, and it also rejects unnameable input.
        if (await _pins.RemoveAsync(command.UserId, CommittedMerchantKey.Derive(command.Merchant), ct))
            return true;

        return await _pins.RemoveAsync(command.UserId, command.Merchant.Trim().ToLowerInvariant(), ct);
    }
}
