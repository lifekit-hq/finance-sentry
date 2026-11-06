namespace FinanceSentry.Modules.Radar.Domain;

/// <summary>A ticker in the Radar universe. De-activated (not deleted) when it leaves holdings/watchlist/theses.</summary>
public sealed class RadarUniverseMember
{
    public Guid Id { get; init; }
    public string Ticker { get; set; } = string.Empty;
    public UniverseKind Kind { get; set; }
    public UniverseSource Source { get; set; }
    public bool Active { get; set; } = true;
}
