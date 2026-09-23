namespace FinanceSentry.Tests.Unit.Observability;

using System.Text.Json;
using FinanceSentry.Infrastructure.Observability.HealthChecks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

/// <summary>
/// The readiness body is served without authentication, so it must name checks and statuses but never
/// republish a driver exception message (host, port, database name, user). A check that explains itself
/// with a plain description (the <c>migrations</c> check) keeps it; an exception-backed check does not.
/// </summary>
public class ReadinessResponseWriterTests
{
    private const string DriverMessage = "28P01: password authentication failed for user \"finance_user\"";
    private const string PlainDescription = "Module migrations were skipped at startup. Pending migrations: ResearchDbContext: M013.";

    [Fact]
    public async Task ExceptionBackedCheck_CarriesNameAndStatusOnly()
    {
        var report = Report(("database", HealthCheckResult.Unhealthy(DriverMessage, new InvalidOperationException(DriverMessage))));

        var body = await Write(report);

        var database = Check(body, "database");
        database.GetProperty("status").GetString().Should().Be("Unhealthy");
        database.TryGetProperty("description", out _).Should().BeFalse();
        body.Should().NotContain("finance_user").And.NotContain("28P01");
    }

    [Fact]
    public async Task PlainDescriptionCheck_CarriesItsDescription()
    {
        var report = Report(("migrations", HealthCheckResult.Unhealthy(PlainDescription)));

        var body = await Write(report);

        var migrations = Check(body, "migrations");
        migrations.GetProperty("status").GetString().Should().Be("Unhealthy");
        migrations.GetProperty("description").GetString().Should().Be(PlainDescription);
    }

    [Fact]
    public async Task CheckWithoutDescription_OmitsTheField()
    {
        var report = Report(("hangfire", HealthCheckResult.Healthy()));

        var body = await Write(report);

        Check(body, "hangfire").TryGetProperty("description", out _).Should().BeFalse();
    }

    private static HealthReport Report(params (string Name, HealthCheckResult Result)[] checks)
    {
        var entries = checks.ToDictionary(
            c => c.Name,
            c => new HealthReportEntry(c.Result.Status, c.Result.Description, TimeSpan.Zero, c.Result.Exception, c.Result.Data));
        return new HealthReport(entries, TimeSpan.Zero);
    }

    private static async Task<string> Write(HealthReport report)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await ReadinessResponseWriter.WriteAsync(context, report);

        context.Response.Body.Position = 0;
        return await new StreamReader(context.Response.Body).ReadToEndAsync();
    }

    private static JsonElement Check(string body, string name)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("checks").EnumerateArray()
            .Single(check => check.GetProperty("name").GetString() == name)
            .Clone();
    }
}
