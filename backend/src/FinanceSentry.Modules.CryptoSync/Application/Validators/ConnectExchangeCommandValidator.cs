using FinanceSentry.Modules.CryptoSync.Application.Commands;
using FinanceSentry.Modules.CryptoSync.Domain;
using FluentValidation;

namespace FinanceSentry.Modules.CryptoSync.Application.Validators;

public sealed class ConnectExchangeCommandValidator : AbstractValidator<ConnectExchangeCommand>
{
    public ConnectExchangeCommandValidator()
    {
        RuleFor(x => x.Provider)
            .Must(p => p is CryptoExchangeProvider.Binance or CryptoExchangeProvider.RevolutX)
            .WithMessage("provider is not a supported crypto exchange.");

        RuleFor(x => x.ApiKey)
            .NotEmpty().WithMessage("apiKey is required.");

        RuleFor(x => x.ApiSecret)
            .NotEmpty()
            .WithMessage(x => x.Provider == CryptoExchangeProvider.RevolutX
                ? "privateKey is required."
                : "apiSecret is required.");
    }
}
