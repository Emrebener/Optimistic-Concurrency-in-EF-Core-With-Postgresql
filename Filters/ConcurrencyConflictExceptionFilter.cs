using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace OptimisticConcurrencyDemo.Filters;

public class ConcurrencyConflictExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not DbUpdateConcurrencyException) return;

        var problem = new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.10",
            Title = "Conflict",
            Status = StatusCodes.Status409Conflict,
            Detail = "The resource was modified by another caller. Refetch and retry.",
        };

        context.Result = new ConflictObjectResult(problem);
        context.ExceptionHandled = true;
    }
}
