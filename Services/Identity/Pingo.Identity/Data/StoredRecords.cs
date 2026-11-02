using Pingo.Identity.Passwords;

namespace Pingo.Identity.Data;

// What the repositories store and return: one record per table row. Internal, not API contracts.

internal record User(Guid Id, DateTimeOffset RegisteredAt);

internal record PasswordLogin(Guid UserId, string NormalizedEmail, PasswordHash Password, DateTimeOffset CreatedAt);

// TokenHash is the SHA-256 of the refresh token; the token itself is never stored.
internal record StoredRefreshToken(byte[] TokenHash, Guid UserId, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);
