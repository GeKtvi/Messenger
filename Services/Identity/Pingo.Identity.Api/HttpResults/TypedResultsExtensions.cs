using Microsoft.Extensions.Localization;

namespace Pingo.Identity.Api.HttpResults;

// C# 14 static extension members, one per error, so errors read like the built-in results:
// TypedResults.EmailTaken(messages). Each sets the status, the "type" URN and the localized title.
public static class TypedResultsExtensions
{
    extension(TypedResults)
    {
        public static ConflictProblem EmailTaken(IStringLocalizer<ErrorMessages> messages) =>
            new("email-taken", messages["EmailTaken"]);

        public static UnauthorizedProblem UserNotFound(IStringLocalizer<ErrorMessages> messages) =>
            new("user-not-found", messages["UserNotFound"]);

        public static UnauthorizedProblem WrongPassword(IStringLocalizer<ErrorMessages> messages) =>
            new("wrong-password", messages["WrongPassword"]);

        public static UnauthorizedProblem InvalidRefreshToken(IStringLocalizer<ErrorMessages> messages) =>
            new("invalid-refresh-token", messages["InvalidRefreshToken"]);
    }
}
