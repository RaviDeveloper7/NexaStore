
// IN: Every real production API has this. Without it, unhandled exceptions produce
// ASP.NET Core's default error page (HTML) or a raw 500 with a stack trace —
// both are wrong for a JSON API and potentially expose internal implementation details.
//
// IN: What is the relationship between ExceptionMiddleware and UnhandledExceptionBehaviour?
// UnhandledExceptionBehaviour (Application layer):
//   - Catches exceptions from MediatR handlers specifically
//   - Logs with rich APPLICATION context (RequestName, UserId, Request object)
//   - Rethrows — does NOT produce an HTTP response
//   - Works for both API and Azure Functions
//
// ExceptionMiddleware (API layer):
//   - Catches everything UnhandledExceptionBehaviour rethrew
//   - Maps exception TYPE to HTTP status code
//   - Produces a structured JSON error response
//   - Only runs for HTTP requests
//
// Together: Application Insights gets a rich log entry WITH business context.
// The client gets a clean JSON response WITH no stack trace leakage.
//
// IN: We use RFC 7807 Problem Details format — the HTTP API standard for errors.
// https://datatracker.ietf.org/doc/html/rfc7807
// ASP.NET Core's built-in validation uses this same format.
// Consistency means API consumers handle all error responses the same way.

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using NexaStore.Application.Common.Exceptions;
using NexaStore.Domain.Exceptions;

namespace NexaStore.Api.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    // IN: IHostEnvironment injected to detect Development vs Production.
    // In Development: include exception details in the response (useful for debugging).
    // In Production:  return generic messages only (never leak stack traces or internals).
    public ExceptionMiddleware(
        RequestDelegate next,
        ILogger<ExceptionMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        // Determine HTTP status code and Problem Details based on exception type
        var (statusCode, problemDetails) = exception switch
        {
            // --- Application validation failure → 400 Bad Request ---
            // IN: ValidationException carries a dictionary of field-level errors.
            // Mapped to the RFC 7807 "errors" extension field — same format as
            // ASP.NET Core's built-in model validation response.
            // Client can display per-field error messages directly.
            ValidationException validationEx => (
                HttpStatusCode.BadRequest,
                new ValidationProblemDetails(validationEx.Errors)
                {
                    Title = "One or more validation errors occurred.",
                    Status = (int)HttpStatusCode.BadRequest,
                    Instance = context.Request.Path
                }),

            // --- Domain not found → 404 Not Found ---
            // IN: NotFoundException message format: "Product (Id: xxx) was not found."
            // Included in the response — safe to expose, contains no internals.
            NotFoundException notFoundEx => (
                HttpStatusCode.NotFound,
                new ProblemDetails
                {
                    Title = "Resource not found.",
                    Detail = notFoundEx.Message,
                    Status = (int)HttpStatusCode.NotFound,
                    Instance = context.Request.Path
                }),

            // --- Domain business rule violation → 400 Bad Request ---
            // IN: BadRequestException message is a human-readable business rule:
            // "Cannot cancel a Shipped order."
            // "Product cannot be deleted because it has existing orders."
            // Safe to expose — written for end-user consumption.
            BadRequestException badRequestEx => (
                HttpStatusCode.BadRequest,
                new ProblemDetails
                {
                    Title = "Bad request.",
                    Detail = badRequestEx.Message,
                    Status = (int)HttpStatusCode.BadRequest,
                    Instance = context.Request.Path
                }),

            // --- Stock validation failure → 400 Bad Request ---
            // IN: InsufficientStockException carries structured data.
            // We include it in the Detail so the client can display:
            // "Only 3 units available — you requested 10."
            InsufficientStockException stockEx => (
                HttpStatusCode.BadRequest,
                new ProblemDetails
                {
                    Title = "Insufficient stock.",
                    Detail = stockEx.Message,
                    Status = (int)HttpStatusCode.BadRequest,
                    Instance = context.Request.Path
                }),

            // --- Authentication/ownership failure → 401 Unauthorized ---
            // IN: UnauthorizedAccessException is thrown by handlers when
            // ownership checks fail or the user is not authenticated.
            // 401 not 403 because we use JWT — 401 prompts the client to
            // re-authenticate or refresh the token.
            UnauthorizedAccessException unauthorizedEx => (
                HttpStatusCode.Unauthorized,
                new ProblemDetails
                {
                    Title = "Unauthorized.",
                    Detail = unauthorizedEx.Message,
                    Status = (int)HttpStatusCode.Unauthorized,
                    Instance = context.Request.Path
                }),

            // --- Auth service failures → 400 Bad Request ---
            // IN: InvalidOperationException from AuthService:
            // "Email already registered", "Registration failed", etc.
            // These are user-facing business messages — safe to expose.
            InvalidOperationException invalidOpEx => (
                HttpStatusCode.BadRequest,
                new ProblemDetails
                {
                    Title = "Invalid operation.",
                    Detail = invalidOpEx.Message,
                    Status = (int)HttpStatusCode.BadRequest,
                    Instance = context.Request.Path
                }),

            // --- Everything else → 500 Internal Server Error ---
            // IN: Unknown exceptions are bugs. Never expose internal details.
            // The exception was already logged by UnhandledExceptionBehaviour
            // with full context. The client gets a generic message only.
            _ => (
                HttpStatusCode.InternalServerError,
                new ProblemDetails
                {
                    Title = "An unexpected error occurred.",
                    Detail = _environment.IsDevelopment()
                                   ? exception.Message
                                   : "An internal server error occurred. Please try again later.",
                    Status = (int)HttpStatusCode.InternalServerError,
                    Instance = context.Request.Path
                })
        };

        // Log 500s here as Error — known exceptions were already logged
        // as Warning by UnhandledExceptionBehaviour in the pipeline.
        // We only need to log the unknowns at this level.
        if (statusCode == HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception,
                "Unhandled exception for {Method} {Path}",
                context.Request.Method,
                context.Request.Path);
        }

        // Build the JSON response
        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = (int)statusCode;

        // IN: TraceId from the current activity — correlates this response
        // with the Application Insights trace for the same request.
        // Client support teams can provide this ID for debugging.
        problemDetails.Extensions["traceId"] =
            System.Diagnostics.Activity.Current?.Id
            ?? context.TraceIdentifier;

        var json = JsonSerializer.Serialize(problemDetails,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

        await context.Response.WriteAsync(json);
    }
}
