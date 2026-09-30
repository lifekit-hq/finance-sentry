namespace FinanceSentry.Modules.Research.Domain;

/// <summary>
/// The look-back a <see cref="BenchmarkRelativeRecord"/> measures. Calendar windows end at the
/// latest priced event and start at the latest priced event on or before the window start;
/// <see cref="SinceInception"/> starts at the subject's first priced event.
/// </summary>
public enum TrackRecordWindow
{
    OneMonth,
    ThreeMonths,
    OneYear,
    SinceInception,
}
