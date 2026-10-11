namespace FinanceSentry.API.Errors;

using FinanceSentry.Core.Api;
using Microsoft.AspNetCore.Mvc;

public static class ApiErrorHandlingExtensions
{
    private const string InvalidInputMessage = "The input was not valid.";

    /// <summary>
    /// Registers <see cref="ApiExceptionHandler"/> for <c>UseExceptionHandler()</c> and points the
    /// <c>[ApiController]</c> model-binding 400 at the same <see cref="ApiErrorBody"/> shape, so clients parse
    /// one error format. <c>AddProblemDetails()</c> is what lets <c>UseExceptionHandler()</c> be configured
    /// without a re-execute path; the handler answers every exception, so it is never the writer.
    /// </summary>
    public static IServiceCollection AddApiErrorHandling(this IServiceCollection services)
    {
        services.AddExceptionHandler<ApiExceptionHandler>();
        services.AddProblemDetails();
        services.Configure<ApiBehaviorOptions>(options =>
            options.InvalidModelStateResponseFactory = context => new BadRequestObjectResult(
                ApiErrorBody.From(
                    "Validation failed.", "VALIDATION_ERROR",
                    context.ModelState.Values
                        .SelectMany(entry => entry.Errors)
                        .Select(error => string.IsNullOrEmpty(error.ErrorMessage) ? InvalidInputMessage : error.ErrorMessage))));
        return services;
    }
}
