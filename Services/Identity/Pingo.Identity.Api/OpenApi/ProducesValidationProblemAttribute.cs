using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;

namespace Pingo.Identity.Api.OpenApi;

// Short form of [ProducesResponseType] for the 400 that [ApiController] returns automatically for an
// invalid body. The action's return type can't describe it, because the action never runs.
// ApiExplorer reads subclasses like the attribute itself.
public sealed class ProducesValidationProblemAttribute() : ProducesResponseTypeAttribute(
    typeof(HttpValidationProblemDetails), StatusCodes.Status400BadRequest, MediaTypeNames.Application.ProblemJson);
