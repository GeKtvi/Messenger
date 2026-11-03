using System.Reflection;
using Microsoft.AspNetCore.Http.Metadata;

namespace Pingo.Identity.Api.HttpResults;

// A 409 problem. Created through the TypedResults extensions, for example TypedResults.EmailTaken().
public sealed class ConflictProblem : ProblemResult, IEndpointMetadataProvider
{
    internal ConflictProblem(string code, string title)
        : base(StatusCodes.Status409Conflict, code, title)
    {
    }

    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder) =>
        AddMetadata(builder, StatusCodes.Status409Conflict);
}
