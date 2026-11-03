using System.Net.Mime;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;

namespace Pingo.Identity.Api.HttpResults;

// Wraps ProblemHttpResult, so the response is written exactly as by TypedResults.Problem():
// application/problem+json, with a traceId from IProblemDetailsService. Unlike ProblemHttpResult,
// each subclass has a fixed status that it reports to OpenAPI, so actions need no
// [ProducesResponseType] for it.
public abstract class ProblemResult : IResult
{
    private readonly ProblemHttpResult _problem;

    private protected ProblemResult(int statusCode, string code, string title) =>
        _problem = TypedResults.Problem(type: $"urn:pingo:problem:{code}", title: title, statusCode: statusCode);

    public Task ExecuteAsync(HttpContext httpContext) => _problem.ExecuteAsync(httpContext);

    private protected static void AddMetadata(EndpointBuilder builder, int statusCode) =>
        builder.Metadata.Add(new ProducesResponseTypeMetadata(
            statusCode, typeof(ProblemDetails), [MediaTypeNames.Application.ProblemJson]));
}
