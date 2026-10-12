[assembly: Xunit.TestFramework(
    "FinanceSentry.Tests.Integration.Shared.ContainerTeardownTestFramework",
    "FinanceSentry.Tests.Integration")]

namespace FinanceSentry.Tests.Integration.Shared;

using System.Reflection;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

/// <summary>
/// xUnit 2 has no assembly fixture, so this framework is the assembly-level hook that removes the containers
/// of <see cref="PostgresServer"/> as soon as the last test has finished.
/// <para>
/// The removal runs before the assembly-finished message is forwarded to the runner, not at disposal of the
/// framework: <c>vstest</c> ends the test host as soon as it has that message, which can be before (or in the
/// middle of) a dispose — one run of the whole suite removed the Postgres 14 container and was killed before
/// the Postgres 16 one. Without this the containers depend on Ryuk alone, and wherever it is disabled
/// (<c>TESTCONTAINERS_RYUK_DISABLED=true</c>) each run leaves a container and its anonymous volume behind.
/// </para>
/// </summary>
public sealed class ContainerTeardownTestFramework(IMessageSink diagnosticMessageSink)
    : XunitTestFramework(diagnosticMessageSink)
{
    protected override ITestFrameworkExecutor CreateExecutor(AssemblyName assemblyName) =>
        new Executor(assemblyName, SourceInformationProvider, DiagnosticMessageSink);

    private sealed class Executor(
        AssemblyName assemblyName,
        ISourceInformationProvider sourceInformationProvider,
        IMessageSink diagnosticMessageSink)
        : XunitTestFrameworkExecutor(assemblyName, sourceInformationProvider, diagnosticMessageSink)
    {
        protected override void RunTestCases(
            IEnumerable<IXunitTestCase> testCases,
            IMessageSink executionMessageSink,
            ITestFrameworkExecutionOptions executionOptions) =>
            base.RunTestCases(
                testCases,
                new TeardownBeforeFinishedSink(executionMessageSink, DiagnosticMessageSink),
                executionOptions);
    }

    private sealed class TeardownBeforeFinishedSink(IMessageSink inner, IMessageSink diagnosticMessageSink) : LongLivedMarshalByRefObject, IMessageSink
    {
        public bool OnMessage(IMessageSinkMessage message)
        {
            if (message is ITestAssemblyFinished)
            {
                try
                {
                    Task.Run(PostgresServer.DisposeAllAsync).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    diagnosticMessageSink.OnMessage(
                        new DiagnosticMessage($"Postgres container teardown failed; Ryuk is the backstop: {ex}"));
                }
            }

            return inner.OnMessage(message);
        }
    }
}
