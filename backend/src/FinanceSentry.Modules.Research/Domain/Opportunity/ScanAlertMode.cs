namespace FinanceSentry.Modules.Research.Domain.Opportunity;

/// <summary>
/// Whether the machine scan records its top-tier findings only, or also raises domain Alerts
/// (#558 clause 4). Mirrors Radar's <c>ScannerMode</c>, which launched the structure scanner the
/// same way: a widened scan reaches names nobody asked about, so the Alert lane opens only once
/// the nominations have been read for a while.
/// </summary>
public enum ScanAlertMode
{
    LogOnly,
    Alerting,
}
