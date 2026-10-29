using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace NAVDataApi.Data;

/// <summary>
/// Turns <see cref="SandboxCapacityExceededException"/> into a 503 with a Retry-After header
/// indicating when the oldest dataset expires.
/// </summary>
public sealed class SandboxCapacityExceptionHandler(IProblemDetailsService problemDetails, TimeProvider time) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not SandboxCapacityExceededException capacity)
            return false;

        var retryAfter = Math.Max(1, (int)Math.Ceiling((capacity.NextSlotUtc - time.GetUtcNow()).TotalSeconds));
        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        httpContext.Response.Headers.RetryAfter = retryAfter.ToString();

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Sandbox capacity reached",
                Detail = capacity.Message
            }
        });
    }
}
