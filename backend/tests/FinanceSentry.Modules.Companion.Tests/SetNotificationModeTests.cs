namespace FinanceSentry.Modules.Companion.Tests;

using FinanceSentry.Modules.Companion.Application.Commands;
using FinanceSentry.Modules.Companion.Application.Queries;
using FinanceSentry.Modules.Companion.Domain;
using FinanceSentry.Modules.Companion.Domain.Repositories;
using FluentAssertions;
using Xunit;

/// <summary>Mode get/set + validation (feature 031, US1).</summary>
public sealed class SetNotificationModeTests
{
    private static readonly Guid User = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private sealed class FakeSettingRepository : INotificationSettingRepository
    {
        public CompanionNotificationSetting? Saved { get; private set; }

        public Task<CompanionNotificationSetting> GetOrDefaultAsync(Guid userId, CancellationToken ct = default)
            => Task.FromResult(Saved ?? new CompanionNotificationSetting { UserId = userId, Mode = NotificationMode.Scan });

        public Task<CompanionNotificationSetting> GetOrDefaultUnscopedAsync(Guid userId, CancellationToken ct = default)
            => GetOrDefaultAsync(userId, ct);

        public Task UpsertAsync(CompanionNotificationSetting setting, CancellationToken ct = default)
        {
            Saved = setting;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Default_mode_is_scan()
    {
        var result = await new GetNotificationModeQueryHandler(new FakeSettingRepository())
            .Handle(new GetNotificationModeQuery(User), default);

        result.Mode.Should().Be("Scan");
    }

    [Theory]
    [InlineData("quiet", "Quiet")]
    [InlineData("Digest", "Digest")]
    [InlineData("REALTIME", "Realtime")]
    public async Task Valid_mode_is_persisted_case_insensitively(string input, string expected)
    {
        var repo = new FakeSettingRepository();

        var result = await new SetNotificationModeCommandHandler(repo)
            .Handle(new SetNotificationModeCommand(User, input), default);

        result.Mode.Should().Be(expected);
        repo.Saved!.Mode.ToString().Should().Be(expected);
    }

    [Fact]
    public async Task Invalid_mode_is_rejected_and_previous_unchanged()
    {
        var repo = new FakeSettingRepository();
        await new SetNotificationModeCommandHandler(repo).Handle(new SetNotificationModeCommand(User, "realtime"), default);

        var act = async () => await new SetNotificationModeCommandHandler(repo)
            .Handle(new SetNotificationModeCommand(User, "bogus"), default);

        await act.Should().ThrowAsync<ArgumentException>();
        repo.Saved!.Mode.Should().Be(NotificationMode.Realtime, "a rejected set must not change the stored mode");
    }
}
