namespace FinanceSentry.Tests.Unit.BankSync.PaydayTransfer;

using System.ComponentModel.DataAnnotations;
using FinanceSentry.Modules.BankSync.API.Controllers;
using FinanceSentry.Modules.BankSync.Application.Commands;
using FinanceSentry.Modules.BankSync.Domain;
using FinanceSentry.Modules.BankSync.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

public class UpdatePaydayTransferSettingsTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly Mock<IPaydayTransferSettingsRepository> _settings = new();

    private UpdatePaydayTransferSettingsCommandHandler Handler() => new(_settings.Object);

    [Fact]
    public async Task Saves_the_values_for_the_named_user_and_returns_what_was_stored()
    {
        _settings.Setup(s => s.UpsertAsync(UserId, 20000m, 100m, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaydayTransferSettings { UserId = UserId, FixedOutflowUah = 20000m, RoundingStepEur = 100m });

        var result = await Handler().Handle(new UpdatePaydayTransferSettingsCommand(UserId, 20000m, 100m), CancellationToken.None);

        result.Should().Be(new PaydayTransferSettingsDto(20000m, 100m));
        _settings.Verify(s => s.UpsertAsync(UserId, 20000m, 100m, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Accepts_a_zero_outflow_and_the_maximum_amounts()
    {
        _settings.Setup(s => s.UpsertAsync(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid u, decimal o, decimal r, CancellationToken _) =>
                new PaydayTransferSettings { UserId = u, FixedOutflowUah = o, RoundingStepEur = r });

        var zero = await Handler().Handle(new UpdatePaydayTransferSettingsCommand(UserId, 0m, 1m), CancellationToken.None);
        var max = await Handler().Handle(
            new UpdatePaydayTransferSettingsCommand(UserId, PaydayTransferDefaults.MaxAmount, PaydayTransferDefaults.MaxAmount),
            CancellationToken.None);

        zero.FixedOutflowUah.Should().Be(0m);
        max.RoundingStepEur.Should().Be(PaydayTransferDefaults.MaxAmount);
    }

    [Theory]
    [InlineData(-0.01, 50)]
    [InlineData(1_000_000_000.01, 50)]
    [InlineData(15000, 0)]
    [InlineData(15000, -1)]
    [InlineData(15000, 1_000_000_000.01)]
    public async Task Rejects_out_of_range_values_without_touching_the_store(double outflow, double step)
    {
        var act = () => Handler().Handle(
            new UpdatePaydayTransferSettingsCommand(UserId, (decimal)outflow, (decimal)step), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        _settings.Verify(
            s => s.UpsertAsync(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(0, 0.01, true)]
    [InlineData(15000, 50, true)]
    [InlineData(1_000_000_000, 1_000_000_000, true)]
    [InlineData(-1, 50, false)]
    [InlineData(15000, 0, false)]
    [InlineData(1_000_000_001, 50, false)]
    [InlineData(15000, 1_000_000_001, false)]
    public void The_request_binder_rejects_what_the_handler_would_reject(double outflow, double step, bool valid)
    {
        var request = new UpdatePaydayTransferSettingsRequest((decimal)outflow, (decimal)step);

        Validator.TryValidateObject(request, new ValidationContext(request), [], validateAllProperties: true)
            .Should().Be(valid);
    }
}
