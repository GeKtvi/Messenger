namespace Pingo.Identity.Models.LoginResults;

public sealed record Succeeded(AuthTokens Tokens) : LoginResult;
