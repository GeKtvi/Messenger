namespace Pingo.Identity.Models.RefreshResults;

public sealed record Succeeded(AuthTokens Tokens) : RefreshResult;
