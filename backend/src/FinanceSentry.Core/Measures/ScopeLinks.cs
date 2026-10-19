namespace FinanceSentry.Core.Measures;

/// <summary>
/// A value series with its scope changes bridged. <paramref name="Values"/> is the input series without the
/// closes a link makes ambiguous, its ends moved when a link sits at an edge. <paramref name="Flows"/> are the
/// input flows plus one synthetic external flow per bridged link, sized to the link's whole value change, so
/// the measures neither count the linked value as growth (M2, M3, M4) nor as a contribution (M2 reads
/// <paramref name="NewlyLinkedUsd"/> on its own line). <paramref name="Links"/> are the link days that touched
/// the window. <paramref name="VanishedDays"/> are closes that read zero after a valued close with no recorded
/// withdrawal: a failed read stores zero, so they are dropped as missing valuations rather than read as a total
/// loss, and a figure with any of them is an estimate (reason <c>UNKNOWN_FLOW</c>, rule 4).
/// <paramref name="LinkedInUsd"/> and <paramref name="LinkedOutUsd"/> are <paramref name="NewlyLinkedUsd"/> split by
/// sign, both positive: the stretches whose value rose and those whose value fell (M5's newly linked and unlinked lines).
/// </summary>
public sealed record LinkBridge(
    IReadOnlyList<ValuePoint> Values,
    IReadOnlyList<ClassifiedFlow> Flows,
    decimal NewlyLinkedUsd,
    IReadOnlyList<DateOnly> Links,
    IReadOnlyList<DateOnly> VanishedDays,
    decimal LinkedInUsd,
    decimal LinkedOutUsd)
{
    /// <summary>Whether a link touched the window (reason <c>SCOPE_CHANGED</c>, informative: the measures bridged it).</summary>
    public bool ScopeChanged => Links.Count > 0;

    /// <summary>The close the bridged window opens on, or null when nothing is left.</summary>
    public ValuePoint? Opening => Values.Count > 0 ? Values[0] : null;

    /// <summary>The close the bridged window closes on, or null when nothing is left.</summary>
    public ValuePoint? Closing => Values.Count > 0 ? Values[^1] : null;
}

/// <summary>
/// Rule 3 of finance-framework §3.2 for a scope measured from one aggregate value series: a newly linked
/// account is not growth. A link on day L is seen by the first close after L and maybe by L's own close
/// (the snapshot runs at 01:00 UTC), so the sub-period from the last valued close before L to the first one
/// after it carries the linked value. That stretch becomes one sub-period whose whole change, less its
/// recorded external flows, is a synthetic external flow at its end: its return is exactly zero in M3, its
/// value is capital, not gain, in M4, and M2 names it "newly linked". A link at the window's first close moves
/// the opening to the first close after it; a link at the last close moves the closing back before it. Links
/// outside the window are ignored. The window opens on the first close that holds value: a scope that held nothing
/// yet (closes of zero or none before the first account was linked) has no base, and a later close of zero that
/// no recorded withdrawal explains is dropped (<see cref="LinkBridge.VanishedDays"/>). Market moves inside the
/// bridged stretch are lost into the linked line, a day or two at most. Pure: no I/O, no clock.
/// </summary>
public static class ScopeLinks
{
    /// <param name="values">The scope's closes over the window, any order.</param>
    /// <param name="flows">The scope's classified flows (already without each source's pre-link history).</param>
    /// <param name="linkDays">The UTC days a source joined the scope.</param>
    public static LinkBridge Bridge(
        IReadOnlyList<ValuePoint> values, IReadOnlyList<ClassifiedFlow> flows, IEnumerable<DateOnly> linkDays)
    {
        var allFlows = flows.ToList();
        var (points, vanished) = WithoutVanishedCloses(
            [.. values.OrderBy(v => v.Date).SkipWhile(v => v.ValueUsd is null or 0m)], allFlows);
        var links = new List<DateOnly>();
        var linkedIn = 0m;
        var linkedOut = 0m;

        foreach (var link in linkDays.Distinct().Order())
        {
            if (points.Count < 2 || link < points[0].Date || link > points[^1].Date)
                continue;

            links.Add(link);
            var before = points.LastOrDefault(p => p.Date < link && p.ValueUsd is not null);
            var after = points.FirstOrDefault(p => p.Date > link && p.ValueUsd is not null);
            if (before.ValueUsd is null)
            {
                points.RemoveAll(p => p.Date <= link);
                continue;
            }

            if (after.ValueUsd is null)
            {
                points.RemoveAll(p => p.Date >= link);
                continue;
            }

            points.RemoveAll(p => p.Date > before.Date && p.Date < after.Date);
            var recorded = MeasureWindow.FlowsWithin(allFlows, before.Date, after.Date);
            var linked = after.ValueUsd.Value - before.ValueUsd.Value
                - ContributionsAndGrowth.Sum(recorded, FlowClass.ExternalInflow)
                + ContributionsAndGrowth.Sum(recorded, FlowClass.ExternalOutflow);
            if (linked == 0m)
                continue;

            if (linked > 0m)
                linkedIn += linked;
            else
                linkedOut -= linked;
            allFlows.Add(new ClassifiedFlow(
                after.Date, linked > 0m ? FlowClass.ExternalInflow : FlowClass.ExternalOutflow, Math.Abs(linked)));
        }

        return new LinkBridge(points, allFlows, linkedIn - linkedOut, links, vanished, linkedIn, linkedOut);
    }

    // A close of zero after a valued one is a real emptying only when a withdrawal was recorded in between.
    private static (List<ValuePoint> Points, List<DateOnly> Vanished) WithoutVanishedCloses(
        List<ValuePoint> points, List<ClassifiedFlow> flows)
    {
        var kept = new List<ValuePoint>(points.Count);
        var vanished = new List<DateOnly>();
        ValuePoint? lastValued = null;
        foreach (var point in points)
        {
            if (point.ValueUsd == 0m && lastValued is { ValueUsd: > 0m } previous
                && !MeasureWindow.FlowsWithin(flows, previous.Date, point.Date).Any(f => f.Class == FlowClass.ExternalOutflow))
            {
                vanished.Add(point.Date);
                continue;
            }

            kept.Add(point);
            if (point.ValueUsd is not null)
                lastValued = point;
        }

        return (kept, vanished);
    }
}
