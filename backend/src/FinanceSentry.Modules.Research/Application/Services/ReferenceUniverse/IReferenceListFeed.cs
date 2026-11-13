namespace FinanceSentry.Modules.Research.Application.Services.ReferenceUniverse;

/// <summary>One ticker on a fetched list.</summary>
public sealed record ReferenceMember(string Ticker, string? Name);

/// <summary>One list as an upstream dataset currently states it.</summary>
public sealed record ReferenceListSnapshot(string ListKey, IReadOnlyList<ReferenceMember> Members);

/// <summary>
/// A dataset that supplies one or more reference lists. A feed returns the complete current picture of its
/// <see cref="Source"/> (the refresher replaces everything the source held), and throws
/// <see cref="ReferenceFeedException"/> when the payload is unusable rather than returning a partial one.
/// </summary>
public interface IReferenceListFeed
{
    /// <summary>One of <c>ReferenceSources</c>.</summary>
    string Source { get; }

    Task<IReadOnlyList<ReferenceListSnapshot>> FetchAsync(CancellationToken ct = default);
}

/// <summary>The upstream payload is missing, malformed or implausibly small.</summary>
public sealed class ReferenceFeedException : Exception
{
    public ReferenceFeedException(string message) : base(message)
    {
    }

    public ReferenceFeedException(string message, Exception inner) : base(message, inner)
    {
    }
}
