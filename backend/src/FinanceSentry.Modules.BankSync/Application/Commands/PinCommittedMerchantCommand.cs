namespace FinanceSentry.Modules.BankSync.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.BankSync.API.Responses;
using FinanceSentry.Modules.BankSync.Application.Services;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;

/// <summary>
/// Pins a merchant as committed — rule (d) of <see cref="CommittedOutflowRules"/>. The caller
/// passes the merchant as they know it ("Mario Scalas", "Anytime Fitness"); the handler derives
/// the key.
/// </summary>
public sealed record PinCommittedMerchantCommand(Guid UserId, string Merchant)
    : ICommand<PinCommittedMerchantResult>;

/// <param name="AlreadyPinned">
/// True when the user already held this pin. Pinning is idempotent rather than a conflict: the
/// pin is set membership, and two spellings of one merchant ("Netflix.com", "NETFLIX") normalize
/// to the same key, so a caller re-pinning has asked for a state that already holds.
/// </param>
public sealed record PinCommittedMerchantResult(CommittedMerchantPinDto Pin, bool AlreadyPinned);

public sealed class PinCommittedMerchantCommandHandler(ICommittedMerchantPinRepository pins)
    : ICommandHandler<PinCommittedMerchantCommand, PinCommittedMerchantResult>
{
    private readonly ICommittedMerchantPinRepository _pins =
        pins ?? throw new ArgumentNullException(nameof(pins));

    public async Task<PinCommittedMerchantResult> Handle(
        PinCommittedMerchantCommand command, CancellationToken ct)
    {
        var displayName = command.Merchant?.Trim() ?? string.Empty;
        var merchantKey = CommittedMerchantKey.Derive(displayName);

        var existing = await _pins.FindAsync(command.UserId, merchantKey, ct);
        if (existing is not null)
        {
            return new PinCommittedMerchantResult(
                new CommittedMerchantPinDto(
                    existing.Id, existing.MerchantKey, existing.DisplayName, existing.CreatedAt),
                AlreadyPinned: true);
        }

        var pin = new CommittedMerchantPin
        {
            UserId = command.UserId,
            MerchantKey = merchantKey,
            DisplayName = displayName,
        };

        await _pins.AddAsync(pin, ct);

        return new PinCommittedMerchantResult(
            new CommittedMerchantPinDto(pin.Id, pin.MerchantKey, pin.DisplayName, pin.CreatedAt),
            AlreadyPinned: false);
    }
}
