using System.Net;
using Inventory.Web.Api;
using Inventory.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Inventory.Web.Infrastructure;

/// <summary>
/// One place where a failing service call becomes a screen. A page whose data could
/// not be read must never render as an empty table: that is indistinguishable from
/// "there is no stock", which is exactly the wrong thing to imply in an inventory
/// console. The filter answers 503 for an unreachable service and 502 for a service
/// that answered badly, and names the service that failed.
/// </summary>
public sealed class ApiExceptionFilter : IExceptionFilter
{
    private readonly ILogger<ApiExceptionFilter> _logger;

    public ApiExceptionFilter(ILogger<ApiExceptionFilter> logger)
    {
        _logger = logger;
    }

    public void OnException(ExceptionContext context)
    {
        if (context.ExceptionHandled || context.Exception is not ApiException exception)
        {
            return;
        }

        _logger.LogWarning(
            exception,
            "{Service} failed while rendering {Path}: {Explain}",
            exception.Service,
            context.HttpContext.Request.Path,
            exception.Explain());

        var statusCode = exception.StatusCode == HttpStatusCode.ServiceUnavailable
            ? HttpStatusCode.ServiceUnavailable
            : HttpStatusCode.BadGateway;

        var viewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), context.ModelState)
        {
            Model = new ServiceErrorViewModel(
                exception.Service,
                exception.Explain(),
                (int)exception.StatusCode,
                (int)statusCode)
        };

        // Handled here, in this filter: without this the view below is produced and the exception
        // is *still* rethrown out of the action, so the console's own fault page is replaced by the
        // exception handler's — which, on a failed form post, is re-executed as a POST and answered
        // with a redirect to the sign-in screen instead of the fault that actually happened.
        context.ExceptionHandled = true;

        // A live fragment is injected into a region that is already on screen: it has to
        // answer with the block alone. A full view there would put a second document inside
        // the first one — a page inside a page, and the shell rendered twice.
        // The view is named as the file is: an underscore, because the same block is answered whole to
        // a page and alone to a live region. Naming it without the underscore finds nothing, and a fault
        // page that cannot render is a 500 in place of the sentence the person needed.
        context.Result = context.HttpContext.Request.Path.StartsWithSegments("/live")
            ? new PartialViewResult
            {
                ViewName = "_ServiceError",
                ViewData = viewData,
                StatusCode = (int)statusCode
            }
            : new ViewResult
            {
                ViewName = "_ServiceError",
                ViewData = viewData,
                StatusCode = (int)statusCode
            };

        context.ExceptionHandled = true;
    }
}
