namespace FinanceSentry.Modules.Research.Tests.Jobs;

using FinanceSentry.Modules.Research.Application.Services.Coverage;
using FluentAssertions;
using Xunit;

public sealed class TickerRotationTests
{
    private static readonly IReadOnlyList<string> Hundred = [.. Enumerable.Range(0, 100).Select(i => $"T{i:000}")];

    [Fact]
    public void A_list_that_fits_in_one_run_is_read_whole_every_run()
    {
        TickerRotation.Slice(Hundred.Take(30).ToList(), 40, 7).Should().Equal(Hundred.Take(30));
    }

    [Fact]
    public void A_long_list_is_read_in_slices_that_together_cover_every_name_exactly_once_per_cycle()
    {
        var cycle = Enumerable.Range(0, 3).SelectMany(run => TickerRotation.Slice(Hundred, 40, run)).ToList();

        cycle.Should().BeEquivalentTo(Hundred);
        cycle.Should().OnlyHaveUniqueItems();
        TickerRotation.Slice(Hundred, 40, 0).Should().HaveCount(40);
        TickerRotation.Slice(Hundred, 40, 2).Should().HaveCount(20);
    }

    [Fact]
    public void The_rotation_repeats_after_a_full_cycle()
    {
        TickerRotation.Slice(Hundred, 40, 1).Should().Equal(TickerRotation.Slice(Hundred, 40, 4));
    }

    [Fact]
    public void A_budget_of_zero_or_an_empty_list_reads_nothing()
    {
        TickerRotation.Slice(Hundred, 0, 3).Should().BeEmpty();
        TickerRotation.Slice([], 40, 3).Should().BeEmpty();
    }

    [Fact]
    public void The_run_index_advances_once_per_period()
    {
        var at = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

        TickerRotation.RunIndex(at.AddMinutes(30), 30).Should().Be(TickerRotation.RunIndex(at, 30) + 1);
        TickerRotation.RunIndex(at.AddMinutes(29), 30).Should().Be(TickerRotation.RunIndex(at, 30));
        TickerRotation.RunIndex(at, 0).Should().Be(at.ToUnixTimeSeconds() / 60, "a period under a minute is clamped to a minute");
    }
}
