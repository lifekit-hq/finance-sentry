namespace FinanceSentry.Core.Interfaces;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registrations for the WORKER role: the one process that owns background work and the
/// bank-credential key. Hosted services, key rotation, credential encryption.
///
/// <see cref="IModuleRegistrar"/> is loaded by every host that serves a module's handlers — the API
/// and the MCP host. This one is loaded by the API host only, next to <see cref="IJobRegistrar"/>
/// (Hangfire schedules — the same role, after the container is built).
///
/// Why the split exists (issue #613): the MCP host loads every module for its tool handlers, so a
/// hosted service or a startup validation that a module registered ran there too. After #609 the
/// MCP crash-looped for three days because it inherited the API's <c>Encryption:Keys</c> startup
/// validation without holding the key — and Wealth's net-worth catch-up had been running in the MCP
/// beside the API since the day it shipped. A module registers here what only the worker may do;
/// the read-side host never sees it.
/// </summary>
public interface IWorkerRegistrar
{
    void Register(IServiceCollection services, IConfiguration config);
}
