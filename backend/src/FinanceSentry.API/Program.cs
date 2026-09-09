using Serilog;
using FinanceSentry.API.Adapters;
using FinanceSentry.API.Commands;
using FinanceSentry.API.Conventions;
using FinanceSentry.Integration;
using FinanceSentry.API.Hangfire;
using FinanceSentry.API.Migrations;
using FinanceSentry.API.Modules;
using FinanceSentry.Infrastructure.Fx;
using FinanceSentry.Infrastructure.Logging;
using FinanceSentry.Infrastructure.Observability;
using FinanceSentry.Infrastructure.Observability.HealthChecks;
using FinanceSentry.Modules.BankSync.API.Middleware;
using FinanceSentry.Modules.BankSync.Infrastructure.Jobs;
using FinanceSentry.Modules.Research.Domain.Ports;
using Hangfire;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.OpenApi;
using DashboardObservability = FinanceSentry.Infrastructure.Observability.Hangfire;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog(SerilogConfiguration.Configure);

// Edge gateway (025, FR-006): honor X-Forwarded-* set by the reverse proxy so the real client IP
// and scheme reach Serilog request logs and any IP-based logic — instead of the gateway's bridge
// address. KnownIPNetworks/KnownProxies are cleared because the only hop in front of the API is the
// trusted in-network gateway (itself fronted by Tailscale Serve).
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
        | ForwardedHeaders.XForwardedProto
        | ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddCors(options =>
    options.AddPolicy("Frontend", policy => policy
        .WithOrigins("http://localhost:4200")
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));

builder.Services.AddControllers(options =>
    options.Conventions.Add(new ApiVersionPrefixConvention("api/v1")));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Finance Sentry API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Enter JWT Bearer token"
    });
});

builder.Services.AddAllModules(builder.Configuration);

// 039: cross-module read ports (adapters live in the host — neither module references the other).
builder.Services.AddCrossModulePorts();

// 421/US3: Research → Agent narration port. Registered here rather than in AddCrossModulePorts
// because Mcp→Integration and Agent→Mcp make an Integration→Agent reference circular.
builder.Services.AddScoped<ILedgerNarrator, LedgerNarratorAdapter>();

builder.Services.AddExchangeRates(builder.Configuration);

builder.Services.AddHangfireServices(builder.Configuration, builder.Environment);

// OpenTelemetry metrics (FR-001/002) — ASP.NET Core + runtime + custom job meter, exposed at /metrics.
builder.Services.AddObservabilityMetrics();

builder.Services.AddHealthChecks()
    .AddNpgSql(
        builder.Configuration.GetConnectionString("Default")!,
        name: "database",
        tags: ["ready"])
    .AddHangfire(
        options => options.MinimumAvailableServers = 1,
        name: "hangfire",
        tags: ["ready"]);

builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter(RateLimitingPolicies.Authenticated, cfg =>
    {
        cfg.PermitLimit = 100;
        cfg.Window = TimeSpan.FromMinutes(1);
        cfg.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        cfg.QueueLimit = 0;
    });
    options.AddFixedWindowLimiter(RateLimitingPolicies.Anonymous, cfg =>
    {
        cfg.PermitLimit = 10;
        cfg.Window = TimeSpan.FromMinutes(1);
        cfg.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        cfg.QueueLimit = 0;
    });
    options.RejectionStatusCode = 429;
});

var app = builder.Build();

app.MigrateAllModules();

// One-shot maintenance verb (e.g. `dotnet FinanceSentry.API.dll recategorize [userId]`).
// Runs the task and exits without ever starting the web server.
if (args.Length > 0 && args[0] == RecategorizationCommand.Verb)
{
    await RecategorizationCommand.RunAsync(app.Services, args);
    return;
}

if (args.Length > 0 && args[0] == SubscriptionDetectionCommand.Verb)
{
    await SubscriptionDetectionCommand.RunAsync(app.Services, args);
    return;
}

// Feature 024 retention/backup verbs (retention-purge [--dry-run] · db-backup · db-restore-verify).
if (args.Length > 0 && RetentionCommand.Handles(args[0]))
{
    await RetentionCommand.RunAsync(app.Services, args);
    return;
}

// Must run before request logging so logs capture the real client IP (025, FR-006).
app.UseForwardedHeaders();
app.UseSerilogRequestLogging();
app.UseCors("Frontend");
app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<JwtAuthenticationMiddleware>();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Hangfire dashboard (FR-004): permissive locally; loopback/Tailscale-only in every other environment,
// backed by durable Postgres storage so history/schedule survive restarts.
IDashboardAuthorizationFilter dashboardFilter = app.Environment.IsDevelopment()
    ? new DevDashboardAuthorizationFilter()
    : new DashboardObservability.DashboardAuthorizationFilter();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [dashboardFilter],
    DisplayStorageConnectionString = false,
    DashboardTitle = "Finance Sentry · Hangfire",
});

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

// Prometheus exposition (FR-001) — scrape-only / not on the public funnel (FR-006).
app.MapObservabilityMetricsEndpoint();

// Readiness (SC-003): overall + per-dependency status; JWT-exempt via /api/v1/health prefix.
app.MapHealthChecks("/api/v1/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = ReadinessResponseWriter.WriteAsync,
});

// Record per-job outcome + duration metrics as jobs reach terminal states (FR-002).
GlobalJobFilters.Filters.Add(
    new DashboardObservability.JobMetricsFilter(app.Services.GetRequiredService<JobMetrics>()));

// Alert on N consecutive job failures → Telegram (US4/FR-009); N configurable, default 3.
GlobalJobFilters.Filters.Add(new DashboardObservability.ConsecutiveFailureAlertFilter(
    app.Services,
    new DashboardObservability.HangfireJobFailureStreakStore(),
    app.Configuration.GetValue("Observability:JobFailureAlertThreshold", 3)));

app.RegisterAllModuleJobs();

// Live FX rates: refresh daily, and once immediately so we leave the hardcoded
// fallback table behind as soon as the app is up.
var recurringJobs = app.Services.GetRequiredService<IRecurringJobManager>();
recurringJobs.AddOrUpdate<ExchangeRateRefreshJob>(
    "exchange-rate-refresh",
    job => job.RunAsync(CancellationToken.None),
    Cron.Daily());
app.Services.GetRequiredService<IBackgroundJobClient>()
    .Enqueue<ExchangeRateRefreshJob>(job => job.RunAsync(CancellationToken.None));

app.Run();

public partial class Program { }
