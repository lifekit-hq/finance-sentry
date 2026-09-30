namespace FinanceSentry.Tests.Integration.Auth;

using System.Net;
using System.Net.Http.Json;
using FinanceSentry.Core.Auth;
using FinanceSentry.Modules.Auth.Domain.Entities;
using FinanceSentry.Modules.Auth.Infrastructure.Persistence;
using FinanceSentry.Tests.Integration.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Hangfire dashboard access outside Development (FR-004): only a signed-in user holding the ops.admin
/// permission (the <see cref="AuthRoles.Owner"/> role) is served, whatever client address the request carries.
/// </summary>
[Collection(HangfireDashboardCollection.Name)]
public class HangfireDashboardAuthorizationTests(HangfireDashboardApiFactory factory) : IClassFixture<HangfireDashboardApiFactory>
{
    private const string Password = "TestPass123!";
    private const string ForwardedClient = "100.101.102.103";

    [Fact]
    public async Task Dashboard_WithoutSignedInUser_IsUnauthorized_WhateverTheForwardedClientAddress()
    {
        var response = await GetDashboardAsync(accessToken: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dashboard_ForSignedInUserWithoutOwnerRole_IsForbidden()
    {
        var token = await SignInAsync("hangfire-member@test.com", owner: false);

        var response = await GetDashboardAsync(token);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Dashboard_ForSignedInOwner_IsServed()
    {
        var token = await SignInAsync("hangfire-owner@test.com", owner: true);

        var response = await GetDashboardAsync(token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<HttpResponseMessage> GetDashboardAsync(string? accessToken)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/hangfire");
        request.Headers.Add(ForwardedHeadersDefaults.XForwardedForHeaderName, ForwardedClient);
        if (accessToken is not null)
            request.Headers.Add("Cookie", $"fs_access_token={accessToken}");
        return await client.SendAsync(request);
    }

    private async Task<string> SignInAsync(string email, bool owner)
    {
        await factory.EnsureUserExistsAsync(email, Password);
        if (owner)
        {
            using var scope = factory.Services.CreateScope();
            var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email);
            TestUsers.GrantRole(scope.ServiceProvider, user!.Id, AuthRoles.Owner);
        }

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        return ExtractCookieValue(login, "fs_access_token")
            ?? throw new InvalidOperationException("Login did not set the access-token cookie.");
    }

    private static string? ExtractCookieValue(HttpResponseMessage response, string cookieName)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
            return null;

        var prefix = $"{cookieName}=";
        var cookie = cookies.FirstOrDefault(c => c.StartsWith(prefix, StringComparison.Ordinal));
        return cookie?[prefix.Length..].Split(';')[0];
    }
}

/// <summary>
/// <see cref="AuthApiFactory"/> with an Auth database of its own: every host seeds the roles at
/// startup, and the in-memory provider has no unique index to stop parallel hosts sharing one database
/// from each inserting it.
/// </summary>
public sealed class HangfireDashboardApiFactory : AuthApiFactory
{
    private readonly InMemoryDatabaseRoot _authDbRoot = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
            ReplaceWithInMemory<AuthDbContext>(services, "hangfire-dashboard-auth", _authDbRoot));
    }
}

/// <summary>
/// Runs alone, after the parallel classes: Hangfire keeps its job storage in a process-wide static, so a
/// test host shutting down in parallel can dispose the storage the dashboard under test is rendering from.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HangfireDashboardCollection
{
    public const string Name = "Hangfire dashboard";
}
