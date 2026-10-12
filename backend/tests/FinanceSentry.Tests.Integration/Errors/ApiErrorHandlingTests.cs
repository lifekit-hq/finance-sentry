namespace FinanceSentry.Tests.Integration.Errors;

using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using FinanceSentry.API.Errors;
using FinanceSentry.Core.Api;
using FinanceSentry.Core.Exceptions;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

/// <summary>
/// The API's one error format: <see cref="ApiExceptionHandler"/> behind <c>UseExceptionHandler()</c> and the
/// model-binding 400 both answer with <see cref="ApiErrorBody"/>, and the error is logged.
/// Requests go through the real pipeline in <c>Program.cs</c> to anonymous probe endpoints that throw.
/// </summary>
public class ApiErrorHandlingTests(ErrorHandlingApiFactory factory) : IClassFixture<ErrorHandlingApiFactory>
{
    [Fact]
    public async Task MappedDomainException_ReturnsItsStatusCodeAndBody_AndRetryAfter()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/error-probe/domain");

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(42));
        var body = await response.Content.ReadFromJsonAsync<ErrorShape>();
        body.Should().BeEquivalentTo(new ErrorShape("Probe is rate limited.", "PROBE_RATE_LIMITED", null));
    }

    [Fact]
    public async Task FluentValidationException_Returns400WithDetails()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/error-probe/validation");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorShape>();
        body!.ErrorCode.Should().Be("VALIDATION_ERROR");
        body.Details.Should().Equal("Name is required.", "Amount must be positive.");
    }

    [Fact]
    public async Task UnhandledException_Returns500SanitisedBody_WithoutLeakingTheException()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/error-probe/unhandled");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain("hunter2").And.NotContain("InvalidOperationException").And.NotContain(" at ");
        var body = await response.Content.ReadFromJsonAsync<ErrorShape>();
        body.Should().BeEquivalentTo(
            new ErrorShape("An unexpected error occurred. Please try again.", "INTERNAL_ERROR", null));
    }

    [Fact]
    public async Task UniqueKeyViolation_Returns409_NotDatabaseUnavailable()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/error-probe/unique-violation");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<ErrorShape>();
        body.Should().BeEquivalentTo(new ErrorShape("This record already exists.", "DATABASE_ERROR", null));
    }

    [Fact]
    public async Task OtherDbUpdateFailure_Returns503DatabaseUnavailable()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/error-probe/db-failure");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadFromJsonAsync<ErrorShape>();
        body.Should().BeEquivalentTo(
            new ErrorShape("Database unavailable. Try again in 1 minute.", "DATABASE_ERROR", null));
    }

    [Fact]
    public async Task UnauthorizedAccessException_Returns401()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/error-probe/unauthorized-access");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<ErrorShape>())!.ErrorCode.Should().Be("UNAUTHORIZED");
    }

    [Fact]
    public async Task ModelBindingFailure_Returns400InTheApiErrorBodyShape()
    {
        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/v1/error-probe/bind", new { name = "", amount = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain("\"errors\"").And.NotContain("\"title\"").And.NotContain("\"type\"");
        var body = await response.Content.ReadFromJsonAsync<ErrorShape>();
        body!.Error.Should().Be("Validation failed.");
        body.ErrorCode.Should().Be("VALIDATION_ERROR");
        body.Details.Should().Contain(d => d.Contains("Name")).And.Contain(d => d.Contains("Amount"));
    }

    [Fact]
    public async Task UnhandledException_IsLoggedAsAnError_AndTheResponseCarriesNoCorrelationHeader()
    {
        var logsBefore = factory.Logs.Count;

        var response = await factory.CreateClient().GetAsync("/api/v1/error-probe/unhandled");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Headers.Contains("X-Correlation-ID").Should().BeFalse();
        var entry = factory.Logs.Snapshot().Skip(logsBefore).Single(e =>
            e.Category == typeof(ApiExceptionHandler).FullName && e.Level == LogLevel.Error);
        entry.Exception.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task ErrorResponse_StillCarriesTheCorsHeaders()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/error-probe/unhandled");
        request.Headers.Add("Origin", "http://localhost:4200");

        var response = await factory.CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle("http://localhost:4200");
    }

    [Fact]
    public async Task ExceptionAfterTheResponseStarted_WritesNoErrorBody()
    {
        var logsBefore = factory.Logs.Count;

        using var response = await factory.CreateClient().GetAsync(
            "/api/v1/error-probe/mid-stream", HttpCompletionOption.ResponseHeadersRead);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var received = new System.Text.StringBuilder();
        try
        {
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
            received.Append(await reader.ReadToEndAsync());
        }
        catch (Exception)
        {
            // The aborted stream may surface as a read failure; only what arrived matters.
        }

        received.ToString().Should().NotContain("INTERNAL_ERROR");
        factory.Logs.Snapshot().Skip(logsBefore).Should().Contain(e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Handler_AfterTheResponseStarted_LogsTheErrorAndLeavesTheResponseAlone()
    {
        var logger = new CapturingLogger<ApiExceptionHandler>();
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());
        context.Response.Body = new MemoryStream();

        var handled = await new ApiExceptionHandler(logger)
            .TryHandleAsync(context, new InvalidOperationException("boom"), default);

        handled.Should().BeTrue();
        context.Response.Body.Length.Should().Be(0);
        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Error && e.Exception!.Message == "boom");
    }

    private sealed record ErrorShape(string Error, string ErrorCode, List<string>? Details);

    private sealed class StartedResponseFeature : HttpResponseFeature
    {
        public override bool HasStarted => true;
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, exception));
    }
}

public class ErrorHandlingApiFactory : WebApplicationFactory<Program>
{
    public CapturingLoggerProvider Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.AddControllers().AddApplicationPart(typeof(ErrorProbeController).Assembly);

            // The host logs through Serilog; swap in a factory whose provider records the entries.
            services.RemoveAll<ILoggerFactory>();
            services.AddSingleton<ILoggerFactory>(_ => LoggerFactory.Create(b =>
                b.SetMinimumLevel(LogLevel.Debug).AddProvider(Logs)));
        });

        builder.UseEnvironment("Testing");
        // Port 1 is deliberately unreachable: contexts this factory does not replace fail their
        // startup migration fast and inertly (MigrateContext catches), see AssetDossierApiFactory.
        builder.UseSetting("ConnectionStrings:Default",
            "Host=127.0.0.1;Port=1;Database=test;Username=test;Password=test;Timeout=1");
        builder.UseSetting("Deduplication:MasterKeyBase64",
            "dGVzdC1vbmx5LWtleS1ub3QtdGhlLWxlYWtlZC1vbmU=");
        builder.UseSetting("Encryption:CurrentKeyVersion", "1");
        builder.UseSetting("Encryption:Keys:1",
            "dGVzdC1vbmx5LWtleS1ub3QtdGhlLWxlYWtlZC1vbmU=");
        builder.UseSetting("Jwt:Secret",
            "test-jwt-secret-key-for-integration-tests-minimum-32-chars");
    }
}

public sealed record CapturedLog(string Category, LogLevel Level, string Message, Exception? Exception);

public sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly List<CapturedLog> _entries = [];
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public int Count
    {
        get { lock (_entries) { return _entries.Count; } }
    }

    public IReadOnlyList<CapturedLog> Snapshot()
    {
        lock (_entries) { return [.. _entries]; }
    }

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(CapturingLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
            owner._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (owner._entries)
            {
                owner._entries.Add(new CapturedLog(category, logLevel, formatter(state, exception), exception));
            }
        }
    }
}

/// <summary>Anonymous endpoints that throw, registered into the host by <see cref="ErrorHandlingApiFactory"/>.</summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting(RateLimitingPolicies.Exempt)]
[Route("error-probe")]
public class ErrorProbeController : ControllerBase
{
    [HttpGet("domain")]
    public IActionResult Domain() => throw new ProbeRateLimitedException();

    [HttpGet("validation")]
    public IActionResult Validation() => throw new FluentValidation.ValidationException(
    [
        new FluentValidation.Results.ValidationFailure("Name", "Name is required."),
        new FluentValidation.Results.ValidationFailure("Amount", "Amount must be positive."),
    ]);

    [HttpGet("unhandled")]
    public IActionResult Unhandled() =>
        throw new InvalidOperationException("connection string Host=db;Password=hunter2");

    [HttpGet("unique-violation")]
    public IActionResult UniqueViolation() => throw new DbUpdateException(
        "Failed to save.",
        new PostgresException(
            "duplicate key value violates unique constraint", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation));

    [HttpGet("db-failure")]
    public IActionResult DbFailure() => throw new DbUpdateException(
        "Failed to save.",
        new PostgresException(
            "could not serialize access", "ERROR", "ERROR", PostgresErrorCodes.SerializationFailure));

    [HttpGet("unauthorized-access")]
    public IActionResult UnauthorizedAccess() => throw new UnauthorizedAccessException();

    [HttpPost("bind")]
    public IActionResult Bind([FromBody] ProbeRequest request) => Ok(request);

    [HttpGet("mid-stream")]
    public async Task MidStream()
    {
        Response.ContentType = "text/event-stream";
        await Response.WriteAsync("event: partial\n\n");
        await Response.Body.FlushAsync();
        throw new InvalidOperationException("stream failed");
    }

    public sealed class ProbeRequest
    {
        [Required]
        public string? Name { get; init; }

        [Range(1, int.MaxValue)]
        public int Amount { get; init; }
    }

    private sealed class ProbeRateLimitedException : ApiException
    {
        public ProbeRateLimitedException()
            : base(StatusCodes.Status429TooManyRequests, "PROBE_RATE_LIMITED", "Probe is rate limited.") =>
            RetryAfterSeconds = 42;
    }
}
