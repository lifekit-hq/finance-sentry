namespace FinanceSentry.Modules.Research.Application.Commands;

using System.Text.RegularExpressions;
using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.Research.API.Responses;
using FinanceSentry.Modules.Research.Domain;
using FinanceSentry.Modules.Research.Domain.Exceptions;
using FinanceSentry.Modules.Research.Domain.Repositories;

/// <summary>
/// Sets (or replaces) the user's own fair-value target for one symbol, shown on the dossier's
/// "Is it cheap?" bar. <paramref name="Currency"/> is the quote currency the price is in (optional, ISO 4217).
/// </summary>
public sealed record SetAssetPriceTargetCommand(Guid UserId, string Symbol, decimal TargetPrice, string? Currency)
    : ICommand<DossierUserTarget>;

public sealed partial class SetAssetPriceTargetCommandHandler(IAssetPriceTargetRepository targets, TimeProvider clock)
    : ICommandHandler<SetAssetPriceTargetCommand, DossierUserTarget>
{
    /// <summary>The column holds 18 digits with 4 decimals; no listed price comes near this.</summary>
    public const decimal MaxTargetPrice = 1_000_000_000m;
    public const int MaxSymbolLength = 20;
    private const int PriceDecimals = 4;

    public async Task<DossierUserTarget> Handle(SetAssetPriceTargetCommand command, CancellationToken ct)
    {
        var symbol = command.Symbol.Trim().ToUpperInvariant();
        if (symbol.Length == 0 || symbol.Length > MaxSymbolLength)
        {
            throw new InvalidPriceTargetException($"symbol must be 1-{MaxSymbolLength} characters.");
        }

        if (command.TargetPrice <= 0m || command.TargetPrice > MaxTargetPrice)
        {
            throw new InvalidPriceTargetException($"target price must be above 0 and at most {MaxTargetPrice:0}.");
        }

        var currency = string.IsNullOrWhiteSpace(command.Currency) ? null : command.Currency.Trim().ToUpperInvariant();
        if (currency is not null && !CurrencyCode().IsMatch(currency))
        {
            throw new InvalidPriceTargetException("currency must be a three-letter ISO code.");
        }

        var target = new AssetPriceTarget
        {
            UserId = command.UserId,
            Symbol = symbol,
            TargetPrice = Math.Round(command.TargetPrice, PriceDecimals),
            Currency = currency,
            UpdatedAt = clock.GetUtcNow(),
        };
        await targets.UpsertAsync(target, ct);
        return new DossierUserTarget(target.TargetPrice, target.Currency, target.UpdatedAt);
    }

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CurrencyCode();
}

/// <summary>Removes the user's target for one symbol; a no-op when none is set.</summary>
public sealed record ClearAssetPriceTargetCommand(Guid UserId, string Symbol) : ICommand<bool>;

public sealed class ClearAssetPriceTargetCommandHandler(IAssetPriceTargetRepository targets)
    : ICommandHandler<ClearAssetPriceTargetCommand, bool>
{
    public Task<bool> Handle(ClearAssetPriceTargetCommand command, CancellationToken ct)
        => targets.DeleteAsync(command.UserId, command.Symbol, ct);
}
