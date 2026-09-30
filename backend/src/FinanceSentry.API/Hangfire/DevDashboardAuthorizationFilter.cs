using Hangfire.Dashboard;

namespace FinanceSentry.API.Hangfire;

/// <summary>
/// Permissive Hangfire dashboard authorization filter for local/development use.
/// Allows every request — Docker port forwarding makes the host's IP look remote
/// to Hangfire's default <c>LocalRequestsOnlyAuthorizationFilter</c>, which would
/// otherwise return 403 when accessing the dashboard via <c>http://localhost</c>.
/// Every other environment uses <see cref="OwnerDashboardAuthorizationFilter"/>.
/// </summary>
public sealed class DevDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => true;
}
