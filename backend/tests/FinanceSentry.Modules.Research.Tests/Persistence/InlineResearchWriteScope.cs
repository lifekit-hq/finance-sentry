namespace FinanceSentry.Modules.Research.Tests.Persistence;

using FinanceSentry.Modules.Research.Application.Services;
using FinanceSentry.Modules.Research.Domain;

/// <summary>For handler tests over list-backed fakes, which have no transaction to open: the work just runs.</summary>
public sealed class InlineResearchWriteScope : IResearchWriteScope
{
    public Task<T> RunAsync<T>(ThesisEventOrigin origin, Func<Task<T>> work, CancellationToken ct = default) => work();
}
