using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace OrderService.Api.Filters;

/// <summary>
/// Catches any exception an MVC action lets escape and turns it into a
/// generic 500 response - mirrors order-service (Java)'s ExceptionController
/// (<c>@ControllerAdvice</c> + <c>@ExceptionHandler(RuntimeException.class)</c>),
/// which logs the exception and returns a fixed "An unexpected error
/// occurred" body rather than leaking exception details to the client.
///
/// Registered as an MVC exception filter (Program.cs:
/// <c>options.Filters.Add&lt;GlobalExceptionFilter&gt;()</c>) so it applies to
/// every controller action without needing ASP.NET Core's separate
/// exception-handling middleware pipeline - the closest match to the Java
/// reference's controller-scoped @ControllerAdvice.
///
/// PII note: this filter only ever logs the caught <see cref="Exception"/>
/// itself, never an <see cref="OrderService.Api.Domain.Order"/> directly, so
/// there is no Address/userId field to accidentally log here.
/// </summary>
public class GlobalExceptionFilter : IExceptionFilter
{
    private readonly ILogger<GlobalExceptionFilter> _logger;

    public GlobalExceptionFilter(ILogger<GlobalExceptionFilter> logger)
    {
        _logger = logger;
    }

    public void OnException(ExceptionContext context)
    {
        _logger.LogError(context.Exception, "Unhandled exception");

        context.Result = new ObjectResult("An unexpected error occurred")
        {
            StatusCode = StatusCodes.Status500InternalServerError,
        };
        context.ExceptionHandled = true;
    }
}
