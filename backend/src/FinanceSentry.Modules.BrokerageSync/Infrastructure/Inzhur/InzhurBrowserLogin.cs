using System.Collections.Concurrent;
using System.Net;
using FinanceSentry.Modules.BrokerageSync.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PuppeteerSharp;

namespace FinanceSentry.Modules.BrokerageSync.Infrastructure.Inzhur;

/// <summary>
/// Signs in to the Inzhur cabinet the only way it accepts: from its own sign-in page, where reCAPTCHA v3 scores the
/// page before the login call. Owner-initiated only — nothing in the daily sync calls this, so an SMS is only ever
/// sent because the owner pressed Connect or Reconnect.
/// </summary>
public interface IInzhurBrowserLogin
{
    /// <summary>Whether a sign-in browser is configured at all.</summary>
    bool IsAvailable { get; }

    /// <summary>Submits phone + password; Inzhur either signs in at once or sends an SMS and waits for the code.</summary>
    Task<InzhurLoginOutcome> StartAsync(Guid userId, string identifier, string password, CancellationToken ct);

    /// <summary>Submits the SMS code for the user's waiting sign-in.</summary>
    Task<InzhurLoginOutcome> VerifyAsync(Guid userId, string code, CancellationToken ct);
}

/// <summary>
/// Runs the sign-in in a headless Chromium sidecar over the DevTools protocol (PuppeteerSharp, pure managed — the API
/// image is Alpine and carries no browser). Each sign-in gets its own incognito browser context on the real
/// <see cref="InzhurOptions.SignInUrl"/>: the page's own reCAPTCHA key scores the page, the login and 2FA calls run
/// inside it with the cabinet's headers, and the resulting cookies (HttpOnly refresh cookie included) are read from
/// the context before it is closed. Between the SMS being sent and the code arriving the page stays open, held here
/// per user for <see cref="InzhurOptions.PendingLoginLifetime"/>; one API process, so an in-memory hold is enough.
/// The user agent is left as Chromium's own (it says HeadlessChrome).
/// </summary>
public sealed class PuppeteerInzhurBrowserLogin(
    IOptions<InzhurOptions> options,
    ILoggerFactory loggerFactory,
    TimeProvider clock) : IInzhurBrowserLogin, IAsyncDisposable
{
    private const string InzhurCookieDomain = "inzhur.reit";

    // A helper both steps share: one cabinet-style POST, reduced to the fields the cabinet's auth store reads.
    private const string PostHelper = """
        async function fsPost(url, body, extra) {
          const headers = Object.assign({ 'content-type': 'application/json', 'x-client-platform': 'web', 'X-Lang': 'uk' }, extra || {});
          const res = await fetch(url, { method: 'POST', credentials: 'include', headers, body });
          let json = null;
          try { json = await res.json(); } catch (e) { json = null; }
          const err = json && typeof json.error === 'object' ? json.error : null;
          return {
            status: res.status,
            mode: (json && json.mode) || null,
            accessToken: (json && json.accessToken) || null,
            challengeId: (json && json.challengeId != null) ? String(json.challengeId) : null,
            actionProof: (json && json.actionProof) || null,
            message: (err && typeof err.message === 'string' && err.message) || (json && typeof json.message === 'string' && json.message) || null,
            attemptsLeft: (err && err.details && typeof err.details.attempts_left === 'number') ? err.details.attempts_left : null
          };
        }
        """;

    private const string StartScript = "async (identifier, password, authBase) => {\n" + PostHelper + """
          const cfg = window.__NUXT__ && window.__NUXT__.config && window.__NUXT__.config.public;
          const siteKey = cfg && cfg.recaptcha && cfg.recaptcha.v3SiteKey;
          if (!siteKey) return { stage: 'start', status: 0, message: 'sign-in page carries no reCAPTCHA site key' };
          if (!(window.grecaptcha && window.grecaptcha.execute)) {
            await new Promise((resolve, reject) => {
              const s = document.createElement('script');
              s.src = 'https://www.google.com/recaptcha/api.js?render=' + encodeURIComponent(siteKey);
              s.onload = resolve;
              s.onerror = () => reject(new Error('reCAPTCHA script failed to load'));
              document.head.appendChild(s);
            });
          }
          await new Promise(r => window.grecaptcha.ready(r));
          const recaptchaToken = await window.grecaptcha.execute(siteKey, { action: 'submit' });
          const body = JSON.stringify({ identifier, password, recaptchaToken });
          const r = await fsPost(authBase + '/api/v1/auth/login', body);
          window.__fs = { body, challengeId: r.challengeId };
          r.stage = 'start';
          delete r.actionProof;
          return r;
        }
        """;

    // The cabinet's 2FA dance: verify the code for an action proof, then repeat the same login (same reCAPTCHA token)
    // with the proof attached.
    private const string VerifyScript = "async (code, authBase) => {\n" + PostHelper + """
          const st = window.__fs;
          if (!st || !st.challengeId) return { stage: 'verify', status: 400, message: 'Invalid or expired 2FA challenge' };
          const v = await fsPost(authBase + '/api/v1/2fa/challenges/verification',
            JSON.stringify({ challengeId: st.challengeId, verificationData: code }));
          if (v.status < 200 || v.status >= 300 || !v.actionProof) {
            v.stage = 'verify';
            delete v.actionProof;
            return v;
          }
          const r = await fsPost(authBase + '/api/v1/auth/login', st.body, { 'X-Action-Proof': v.actionProof });
          r.stage = 'login';
          delete r.actionProof;
          if (r.status >= 200 && r.status < 300) window.__fs = null;
          return r;
        }
        """;

    // Inzhur's code lives 179 s; the browser hold lasts a little longer so a late code gets Inzhur's own answer.
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromSeconds(179);

    private readonly ConcurrentDictionary<Guid, PendingLogin> _pending = new();
    private readonly ILogger _logger = loggerFactory.CreateLogger<PuppeteerInzhurBrowserLogin>();

    public bool IsAvailable => !string.IsNullOrWhiteSpace(options.Value.BrowserUrl);

    public async Task<InzhurLoginOutcome> StartAsync(Guid userId, string identifier, string password, CancellationToken ct)
    {
        await DiscardAsync(userId);

        if (!IsAvailable)
            return new InzhurLoginOutcome.Failed(InzhurErrorCodes.LoginUnavailable, "No Inzhur sign-in browser is configured.");

        PendingLogin? login = null;
        try
        {
            login = await OpenSignInPageAsync(ct);
            var step = await login.Page.EvaluateFunctionAsync<InzhurLoginStep>(
                StartScript, identifier, password, AuthBase);

            var outcome = await InterpretAsync(login, step);
            if (outcome is InzhurLoginOutcome.CodeRequired)
            {
                _pending[userId] = login;
                _ = ExpireAsync(userId, login);
                login = null;
            }

            return outcome;
        }
        catch (Exception ex) when (ex is PuppeteerException or TimeoutException or HttpRequestException or System.Net.Sockets.SocketException)
        {
            _logger.LogWarning("Inzhur sign-in could not start: {ErrorType}", ex.GetType().Name);
            return new InzhurLoginOutcome.Failed(InzhurErrorCodes.LoginUnavailable, $"Sign-in browser failed: {ex.GetType().Name}.");
        }
        finally
        {
            if (login is not null)
                await CloseAsync(login);
        }
    }

    public async Task<InzhurLoginOutcome> VerifyAsync(Guid userId, string code, CancellationToken ct)
    {
        if (!_pending.TryGetValue(userId, out var login) || login.ExpiresAt <= clock.GetUtcNow().UtcDateTime)
        {
            await DiscardAsync(userId);
            return new InzhurLoginOutcome.Failed(InzhurErrorCodes.ChallengeExpired, "No sign-in is waiting for a code.");
        }

        // One code at a time per sign-in: a double-submitted form must not spend two of Inzhur's attempts.
        if (!await login.Gate.WaitAsync(TimeSpan.Zero, ct))
            return new InzhurLoginOutcome.Failed(InzhurErrorCodes.LoginFailed, "A code for this sign-in is already being checked.");

        var keep = false;
        try
        {
            var step = await login.Page.EvaluateFunctionAsync<InzhurLoginStep>(VerifyScript, code, AuthBase);
            var outcome = await InterpretAsync(login, step);
            keep = outcome is InzhurLoginOutcome.InvalidCode;
            return outcome;
        }
        catch (Exception ex) when (ex is PuppeteerException or TimeoutException)
        {
            _logger.LogWarning("Inzhur code check failed in the sign-in browser: {ErrorType}", ex.GetType().Name);
            return new InzhurLoginOutcome.Failed(InzhurErrorCodes.LoginUnavailable, $"Sign-in browser failed: {ex.GetType().Name}.");
        }
        finally
        {
            login.Gate.Release();
            if (!keep)
                await DiscardAsync(userId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var userId in _pending.Keys)
            await DiscardAsync(userId);
    }

    private string AuthBase => options.Value.AuthBaseUrl.TrimEnd('/');

    private async Task<InzhurLoginOutcome> InterpretAsync(PendingLogin login, InzhurLoginStep step)
    {
        var reading = InzhurLoginStepInterpreter.Read(step);
        _logger.LogInformation(
            "Inzhur sign-in {Stage} answered {StatusCode} ({Message}); read as {Verdict}",
            step.Stage, step.Status, step.Message ?? step.Mode ?? "no message", reading.ErrorCode ?? reading.Verdict.ToString());

        return reading.Verdict switch
        {
            InzhurLoginStepInterpreter.Verdict.Authenticated =>
                new InzhurLoginOutcome.Authenticated(new InzhurSession(step.AccessToken!, await HarvestCookiesAsync(login))),
            InzhurLoginStepInterpreter.Verdict.CodeRequired =>
                new InzhurLoginOutcome.CodeRequired(clock.GetUtcNow().UtcDateTime + CodeLifetime),
            InzhurLoginStepInterpreter.Verdict.InvalidCode =>
                new InzhurLoginOutcome.InvalidCode(reading.AttemptsLeft),
            _ => new InzhurLoginOutcome.Failed(reading.ErrorCode!, step.Message ?? $"HTTP {step.Status}"),
        };
    }

    private async Task<PendingLogin> OpenSignInPageAsync(CancellationToken ct)
    {
        var timeout = (int)options.Value.BrowserStepTimeout.TotalMilliseconds;
        var browser = await Puppeteer.ConnectAsync(
            new ConnectOptions { BrowserURL = await ResolveBrowserUrlAsync(ct), ProtocolTimeout = timeout },
            loggerFactory);

        IBrowserContext? context = null;
        try
        {
            context = await browser.CreateBrowserContextAsync();
            var page = await context.NewPageAsync();
            page.DefaultTimeout = timeout;
            page.DefaultNavigationTimeout = timeout;

            await page.GoToAsync(options.Value.SignInUrl, WaitUntilNavigation.Load);
            await page.WaitForFunctionAsync("() => !!(window.__NUXT__ && window.__NUXT__.config)");

            return new PendingLogin(browser, context, page, clock.GetUtcNow().UtcDateTime + options.Value.PendingLoginLifetime);
        }
        catch
        {
            if (context is not null)
                await TryAsync(context.CloseAsync);
            browser.Disconnect();
            throw;
        }
    }

    // Chromium's DevTools HTTP endpoint refuses a Host header that is not an IP or localhost, so the compose
    // service name is resolved here and the browser is addressed by IP.
    private async Task<string> ResolveBrowserUrlAsync(CancellationToken ct)
    {
        var uri = new Uri(options.Value.BrowserUrl);
        if (uri.HostNameType != UriHostNameType.Dns || uri.IsLoopback)
            return uri.ToString();

        var addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, ct);
        var address = addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                      ?? addresses.First();
        return new UriBuilder(uri) { Host = address.ToString() }.Uri.ToString();
    }

    private static async Task<IReadOnlyList<InzhurCookie>> HarvestCookiesAsync(PendingLogin login)
    {
        var cookies = await login.Context.GetCookiesAsync();
        return cookies
            .Where(c => c.Domain is not null && c.Domain.TrimStart('.').EndsWith(InzhurCookieDomain, StringComparison.OrdinalIgnoreCase))
            .Select(c => new InzhurCookie(
                c.Name,
                c.Value,
                c.Domain,
                string.IsNullOrEmpty(c.Path) ? "/" : c.Path,
                c.Expires is > 0 ? DateTimeOffset.FromUnixTimeMilliseconds((long)(c.Expires.Value * 1000)).UtcDateTime : null,
                c.Secure ?? false,
                c.HttpOnly ?? false))
            .ToList();
    }

    private async Task ExpireAsync(Guid userId, PendingLogin login)
    {
        await Task.Delay(options.Value.PendingLoginLifetime);
        if (_pending.TryGetValue(userId, out var current) && ReferenceEquals(current, login))
            await DiscardAsync(userId);
    }

    private async Task DiscardAsync(Guid userId)
    {
        if (_pending.TryRemove(userId, out var login))
            await CloseAsync(login);
    }

    // Disconnect, never close: closing the connected browser would stop the shared sidecar's Chromium.
    private static async Task CloseAsync(PendingLogin login)
    {
        await TryAsync(login.Context.CloseAsync);
        login.Browser.Disconnect();
    }

    private static async Task TryAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is PuppeteerException or TimeoutException or ObjectDisposedException)
        {
            // Best effort: the context may already be gone with the connection.
        }
    }

    private sealed record PendingLogin(IBrowser Browser, IBrowserContext Context, IPage Page, DateTime ExpiresAt)
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
    }
}
