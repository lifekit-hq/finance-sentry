using System.Text.RegularExpressions;

namespace FinanceSentry.Core.Auditing;

/// <summary>
/// One thing to put on the audit trail: who acted, on whose data, what was done, to which resource, and which fields.
/// <para>
/// An entry carries identifiers and field NAMES only - never a value. The constructor enforces the shape that makes
/// that checkable: the resource type and every field name must be a short identifier (letters, digits, <c>_</c> and
/// <c>.</c>, starting with a letter), the resource is named by its id, and there is no free-text member at all. A
/// caller cannot put an amount, a label or a note on the trail without it throwing.
/// </para>
/// </summary>
public sealed partial class AuditEntry
{
    /// <summary>Longest resource type or field name accepted.</summary>
    public const int MaxNameLength = 64;

    /// <summary>Most field names one entry may carry.</summary>
    public const int MaxFieldNames = 64;

    public AuditEntry(
        Guid subjectUserId,
        Guid? actorUserId,
        AuditActorKind actorKind,
        AuditAction action,
        string resourceType,
        Guid? resourceId = null,
        IEnumerable<string>? fieldNames = null)
    {
        if (subjectUserId == Guid.Empty)
            throw new ArgumentException("An audit entry needs the person whose data it concerns.", nameof(subjectUserId));

        SubjectUserId = subjectUserId;
        ActorUserId = actorUserId;
        ActorKind = actorKind;
        Action = action;
        ResourceType = RequireIdentifier(resourceType, nameof(resourceType));
        ResourceId = resourceId;

        var names = (fieldNames ?? [])
            .Select(name => RequireIdentifier(name, nameof(fieldNames)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (names.Length > MaxFieldNames)
            throw new ArgumentException($"An audit entry names at most {MaxFieldNames} fields.", nameof(fieldNames));

        FieldNames = names;
    }

    /// <summary>The person whose data this concerns.</summary>
    public Guid SubjectUserId { get; }

    /// <summary>The person the actor acted as; null for a background job.</summary>
    public Guid? ActorUserId { get; }

    public AuditActorKind ActorKind { get; }

    public AuditAction Action { get; }

    /// <summary>What kind of thing: a table-like name (<c>income_source</c>) or, for a read, the MCP tool's name.</summary>
    public string ResourceType { get; }

    /// <summary>Which one, when the action names a single row.</summary>
    public Guid? ResourceId { get; }

    /// <summary>The fields written or read, by name, sorted and distinct.</summary>
    public IReadOnlyList<string> FieldNames { get; }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_.]*$")]
    private static partial Regex IdentifierPattern();

    private static string RequireIdentifier(string? value, string parameter)
    {
        if (value is null || value.Length is 0 or > MaxNameLength || !IdentifierPattern().IsMatch(value))
        {
            throw new ArgumentException(
                $"An audit entry holds identifiers only (letters, digits, '_' and '.', at most {MaxNameLength} characters, "
                + "starting with a letter) - never a value.",
                parameter);
        }

        return value;
    }
}
