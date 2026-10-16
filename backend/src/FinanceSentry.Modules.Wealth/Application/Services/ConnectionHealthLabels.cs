namespace FinanceSentry.Modules.Wealth.Application.Services;

using FinanceSentry.Core.Connections;

/// <summary>
/// The connection-health labels of one wealth summary (slice S5): for a provider in
/// <see cref="ConnectionHealthOptions.ActiveProviders"/>, what <see cref="ConnectionHealthSurface"/> makes of the health
/// its module stores; for any other provider nothing, so its rows keep the status their sync path gave them. Read once
/// per summary through every module's <see cref="IConnectionHealthReader"/>; with no provider listed nothing is read.
/// </summary>
internal sealed class ConnectionHealthLabels
{
    /// <summary>No labels: every provider keeps its own status.</summary>
    public static readonly ConnectionHealthLabels None = new([], new ConnectionHealthOptions());

    private readonly ConnectionHealthOptions _options;
    private readonly Dictionary<Guid, ConnectionHealthEntry> _connections;
    private readonly Dictionary<Guid, ConnectionHealthEntry> _accounts;

    private ConnectionHealthLabels(IReadOnlyList<ConnectionHealthEntry> entries, ConnectionHealthOptions options)
    {
        _options = options;
        var active = entries.Where(e => options.IsActive(e.Provider)).ToList();
        _connections = active.Where(e => !e.IsAccount).ToDictionary(e => e.Id);
        _accounts = active.Where(e => e.IsAccount).ToDictionary(e => e.Id);
    }

    /// <summary>
    /// Reads every module's stored health for <paramref name="userId"/>, or <see cref="None"/> when no provider is
    /// listed or a module cannot be read (best-effort: a label never fails the summary).
    /// </summary>
    public static async Task<ConnectionHealthLabels> ReadAsync(
        IEnumerable<IConnectionHealthReader> readers, ConnectionHealthOptions options, Guid userId, CancellationToken ct)
    {
        if (options.ActiveProviders.Count == 0)
            return None;

        try
        {
            var entries = new List<ConnectionHealthEntry>();
            foreach (var reader in readers)
                entries.AddRange(await reader.GetForUserAsync(userId, ct));

            return new ConnectionHealthLabels(entries, options);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return None;
        }
    }

    /// <summary>Whether <paramref name="provider"/>'s rows are labelled from its health.</summary>
    public bool IsActive(string provider) => _options.IsActive(provider);

    /// <summary>
    /// A bank account's label (<see cref="ConnectionHealthSurface.Effective"/>: its connection's state while that is not
    /// Healthy, else its own), or null to keep the sync's status.
    /// </summary>
    public string? ForAccount(Guid accountId, string provider, Guid? connectionId)
    {
        var account = _accounts.TryGetValue(accountId, out var own) ? own.Health.State : (ConnectionHealthState?)null;
        var connection = connectionId is { } id && _connections.TryGetValue(id, out var shared)
            ? shared.Health.State
            : (ConnectionHealthState?)null;

        return ConnectionHealthSurface.StatusFor(provider, ConnectionHealthSurface.Effective(connection, account), _options);
    }

    /// <summary>
    /// A provider-level institution's label (a crypto venue, a broker): the most urgent state of its listed connections,
    /// or null to keep the status the holdings give it.
    /// </summary>
    public string? ForInstitution(string provider)
    {
        var states = _connections.Values
            .Where(e => string.Equals(ConnectionHealthSurface.InstitutionOf(e.Provider), provider, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Health.State)
            .ToList();

        return ConnectionHealthSurface.Worst(states) is { } state ? ConnectionHealthSurface.StatusFor(state) : null;
    }
}
