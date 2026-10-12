using FinanceSentry.Modules.Auth.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FinanceSentry.Modules.Auth.API.Authentication;

/// <summary>
/// Makes an endpoint exist only while the break-glass password sign-in is on (<c>Auth:PasswordLogin:Enabled</c>).
/// With it off the request is answered 404 before model binding, validation or the handler run, so the route is
/// indistinguishable from one that was never mapped.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class PasswordLoginGateAttribute : Attribute, IResourceFilter
{
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var options = context.HttpContext.RequestServices.GetRequiredService<IOptions<AuthSignInOptions>>();
        if (!options.Value.PasswordLogin.Enabled)
            context.Result = new NotFoundResult();
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}
