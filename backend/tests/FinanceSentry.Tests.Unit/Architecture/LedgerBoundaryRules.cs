namespace FinanceSentry.Tests.Unit.Architecture;

/// <summary>
/// The pure rules behind constitution CC10 (Principle VII, Ledger boundary). Kept apart from the reflection that
/// feeds them so the blocked and allowed fixtures in <see cref="LedgerBoundaryTests"/> exercise exactly the
/// code that judges the real surfaces.
/// </summary>
internal static class LedgerBoundaryRules
{
    /// <summary>
    /// Every key the wake payload may carry. This is the contract of the hook (event id, kind, user id and entity
    /// ids, plus the event's own routing metadata); amounts, names and narrative are read back through MCP. Adding a
    /// key is a Principle VII decision: widen this list in the same change and say why.
    /// </summary>
    public static readonly IReadOnlySet<string> AllowedPayloadKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        "eventId", "userId", "kind", "subject", "severity", "occurredAt",
        "requiresAcknowledgement", "referenceId", "appUrl",
        "count", "includeHeldForDigest", "heldOverrideReason",
    };

    /// <summary>Principle VII clause 3: no surface is named after the consumer.</summary>
    public static bool IsNamedAfterLedger(string name)
        => name.Contains("ledger", StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns one message per route template, tool name or field that carries the consumer's name.</summary>
    public static IReadOnlyList<string> NamingViolations(string kind, IEnumerable<(string Owner, string Name)> surfaces)
        => surfaces
            .Where(s => IsNamedAfterLedger(s.Name))
            .Select(s => $"{kind} '{s.Name}' ({s.Owner}) is named after Ledger")
            .ToList();

    /// <summary>Principle VII clause 2: the wake payload carries only the allowed id-and-routing keys.</summary>
    public static IReadOnlyList<string> PayloadViolations(IEnumerable<string> keys)
    {
        var violations = new List<string>();
        foreach (var key in keys)
        {
            if (IsNamedAfterLedger(key))
            {
                violations.Add($"wake payload field '{key}' is named after Ledger");
            }
            else if (!AllowedPayloadKeys.Contains(key))
            {
                violations.Add($"wake payload field '{key}' is not an id or routing key (Principle VII: ids only)");
            }
        }

        return violations;
    }
}
