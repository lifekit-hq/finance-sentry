namespace FinanceSentry.Tests.Unit.Wealth;

using FinanceSentry.Core.Interfaces;
using Moq;

/// <summary>A coverage-hole reader that finds no hole, for readers whose test is about something else.</summary>
internal static class NoCoverageHoles
{
    public static ICoverageHoleReader Reader { get; } = Create();

    private static ICoverageHoleReader Create()
    {
        var reader = new Mock<ICoverageHoleReader>();
        reader.Setup(r => r.GetHolesAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        return reader.Object;
    }
}
