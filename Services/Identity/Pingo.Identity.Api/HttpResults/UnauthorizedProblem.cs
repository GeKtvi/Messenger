using System.Reflection;
using Microsoft.AspNetCore.Http.Metadata;

namespace Pingo.Identity.Api.HttpResults;

// A 401 problem. Created through the TypedResults extensions, for example TypedResults.WrongPassword().
public sealed class UnauthorizedProblem : ProblemResult, IEndpointMetadataProvider
{
    internal UnauthorizedProblem(string code, string title)
        : base(StatusCodes.Status401Unauthorized, code, title)
    {
    }

    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder) =>
        AddMetadata(builder, StatusCodes.Status401Unauthorized);
}
