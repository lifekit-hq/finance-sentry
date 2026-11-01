namespace FinanceSentry.Modules.BankSync.Application.Commands;

using FinanceSentry.Core.Cqrs;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;

public sealed record PaydayTransferSettingsDto(decimal FixedOutflowUah, decimal RoundingStepEur);

/// <summary>Saves the fixed monthly outflow and the rounding step of the payday-transfer figure.</summary>
public sealed record UpdatePaydayTransferSettingsCommand(Guid UserId, decimal FixedOutflowUah, decimal RoundingStepEur)
    : ICommand<PaydayTransferSettingsDto>;

public sealed class UpdatePaydayTransferSettingsCommandHandler(IPaydayTransferSettingsRepository settings)
    : ICommandHandler<UpdatePaydayTransferSettingsCommand, PaydayTransferSettingsDto>
{
    public async Task<PaydayTransferSettingsDto> Handle(UpdatePaydayTransferSettingsCommand command, CancellationToken ct)
    {
        if (command.FixedOutflowUah is < 0m or > PaydayTransferDefaults.MaxAmount)
            throw new ArgumentOutOfRangeException(nameof(command), "The fixed outflow is out of range.");
        if (command.RoundingStepEur is <= 0m or > PaydayTransferDefaults.MaxAmount)
            throw new ArgumentOutOfRangeException(nameof(command), "The rounding step is out of range.");

        var saved = await settings.UpsertAsync(command.UserId, command.FixedOutflowUah, command.RoundingStepEur, ct);
        return new PaydayTransferSettingsDto(saved.FixedOutflowUah, saved.RoundingStepEur);
    }
}
