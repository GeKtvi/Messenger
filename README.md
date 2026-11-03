# Pingo

A learning project: a simple messenger built as a set of microservices.

For now it is a single public chat with no users — anyone can post anonymously.

Architecture and convention decisions, with their reasons, are recorded in
[docs/decision-log.md](docs/decision-log.md).

## Homework stages

Each finished homework stage is marked with an annotated git tag, numbered with
two digits starting at `01` (`01`, `02`, `03`, ...).

| Tag  | Stage |
|------|-------|
| `01` | Message service (send/list, EF Core) + Blazor WASM/MudBlazor frontend |
| `02` | PostgreSQL, API/business layers, idempotent PUT with edit time, pending messages in the UI, component tests |
| `03` | Identity service: Dapper + SQL migrations, registration with PBKDF2, login with JWT and refresh tokens, refresh token rotation |
| `04` | Distinct localized Problem Details errors, CSharpFunctionalExtensions results, Identity component tests with Testcontainers and Respawn |

To finish a stage:

1. Commit all the stage's work, including a new row in the table above.
2. Tag that commit with the next number and a short description:
   ```bash
   git tag -a 02 -m "Stage 02: <short description>"
   ```
3. Push the commit and the tag:
   ```bash
   git push origin main --follow-tags
   ```

To view a stage's code: `git checkout 01` (or browse the tag on GitHub).

## Structure

```
Pingo.slnx
├── Services/
│   ├── Message/
│   │   ├── Pingo.Messages.Api              # API layer: ASP.NET Core controllers, request/response contracts
│   │   ├── Pingo.Messages                  # Business layer: IMessageService, EF Core (PostgreSQL), entities
│   │   └── Pingo.Messages.ComponentTests   # Component tests: the whole service over HTTP, in-memory DB
│   └── Identity/
│       ├── Pingo.Identity.Api              # API layer: registration, login, token refresh (Swagger UI)
│       ├── Pingo.Identity                  # Business layer: users, password hashing, tokens
│       └── Pingo.Identity.ComponentTests   # Component tests: the whole service over HTTP, real PostgreSQL
└── Frontend/
    └── Pingo.Frontend              # Blazor WebAssembly + MudBlazor
```

## Message service API

| Method | Route                         | Description |
|--------|-------------------------------|-------------|
| GET    | `/api/messages`               | Returns all messages, oldest first |
| PUT    | `/api/messages/{messageId}`   | Sends a message (`{ "text": "..." }`). Idempotent, see below |

Every endpoint needs an access token from the Identity service, sent as
`Authorization: Bearer <accessToken>`. Without a valid one (missing, expired, tampered, or from
another issuer or audience) the answer is `401`. The service checks the token itself with the same
`Jwt` issuer, audience and signing key as Identity, so it never calls Identity per request.

Each message has a `sentAt` time and an `updatedAt` time, which is `null` until the message is edited.

Sending is idempotent. The client picks the message id (a `Guid`):

- **New id:** creates the message, returns `201 Created`.
- **Existing id, new text:** updates the text and `updatedAt`, returns `200 OK`.
- **Existing id, same text:** changes nothing, returns `200 OK`. Retrying a request is always safe.

Messages are stored in PostgreSQL. The schema is managed with EF Core migrations, which the API
applies automatically on startup. Example requests are in `Pingo.Messages.Api.http`.

### Changing the database schema

After changing an entity, add a migration (the `dotnet-ef` tool is pinned in `dotnet-tools.json`):

```bash
dotnet tool restore
```

```bash
dotnet ef migrations add <MigrationName> --project Services/Message/Pingo.Messages --startup-project Services/Message/Pingo.Messages.Api --output-dir Data/Migrations
```

## Identity service

Handles registration, login and tokens. It uses PostgreSQL through Dapper, with no EF Core:

- **Database:** its own database, `pingo_identity`, in the same local PostgreSQL container. The
  service creates it on first start.
- **Migrations:** hand-written SQL in `Services/Identity/Pingo.Identity/Data/Migrations.cs`. A
  `BackgroundService` applies them on startup and records each one in `schema_migrations`.
- **New migration:** to change the schema, add a new `Migration` with the next version number.
  Never edit one that has already been applied.

| Method | Route                | Description |
|--------|----------------------|-------------|
| POST   | `/api/auth/register` | Registers `{ "email": "...", "password": "..." }`. Returns `201` with the user id, `409` if the email is taken |
| POST   | `/api/auth/login`    | Signs in with `{ "email": "...", "password": "..." }`. Returns an access token and a refresh token, or `401` (unknown email, or wrong password) |
| POST   | `/api/auth/refresh`  | Exchanges `{ "refreshToken": "..." }` for a new access token and a new refresh token, or `401` |

- **Storage:** users and their email/password sign-ins are kept in separate tables (`users`,
  `password_logins`), so other ways to sign in can be added later.
- **Passwords:** hashed with PBKDF2-HMAC-SHA256, 600 000 iterations and a random salt per password.
- **Emails:** compared case-insensitively.
- **Access token:** a JWT signed with HS256, valid for 15 minutes, with the user id in `sub`. Send it
  as `Authorization: Bearer <accessToken>`. JWT settings are in the `Jwt` section of
  `appsettings.json`; the development signing key is in `appsettings.Development.json`.
- **Refresh token:** a random 256-bit string. Only its SHA-256 hash is stored. Each refresh token
  works once: refreshing returns a new one and invalidates the old one. The session ends 30 days
  after login, however often it's refreshed.
- **Token cleanup:** a Hangfire job deletes expired refresh tokens once a day at 03:00 UTC, using an
  index on `expires_at`. Hangfire keeps its jobs in Redis (the `redis` service in
  `docker-compose.yml`, keys prefixed `pingo-identity:hangfire:`). In Development its dashboard is
  at http://localhost:5240/hangfire, where the job can also be triggered by hand.
- **Errors:** RFC 7807 problems (`application/problem+json`). Tell them apart by `type`
  (`urn:pingo:problem:email-taken`, `user-not-found`, `wrong-password`, `invalid-refresh-token`).
  The `title` is in English, or in Russian when the request sends `Accept-Language: ru`.
- **Deviation from homework 04:** errors aren't built with the controller's `Problem()` method as
  the task asks. They use `TypedResults.Problem()` wrapped in our own `ConflictProblem` and
  `UnauthorizedProblem` result types, so the signatures document every response. That's cleaner and
  reusable, but over-engineered for a service this size (D-047, D-048).

## Frontend: pending messages

The UI sends messages the way most messengers do:

- A sent message appears in the chat right away as **Sending…**, and you can keep typing and send
  more without waiting.
- Once the server confirms it, it becomes a normal message.
- If sending fails, it's marked **Not sent** with **Retry** and **Discard** buttons.
- Pending messages are kept in the browser's `localStorage` (`pingo.outbox`), so they survive a
  reload and are resent automatically when the page opens. Each one keeps its client-generated id,
  so resending never creates a duplicate.

## Tests

```bash
dotnet test Pingo.slnx
```

`Pingo.Messages.ComponentTests` starts the real Message service in memory with
`WebApplicationFactory` and talks to it over HTTP (xUnit, FluentAssertions):

- The app starts once per test class (`IClassFixture`), and the database is emptied before each test.
- Every client sends a valid access token built like Identity's (`TestAccessTokens`), so the tests
  don't need the Identity service running.
- The clock is a `FakeTimeProvider`, so send and edit times can be checked exactly.

| Test class | Database | Covers |
|---|---|---|
| `MessagesApiTests` | EF Core in-memory | Listing, sending, ordering, editing, trimming, length limit, validation errors |
| `MessagesIdempotencyTests` | EF Core in-memory | Repeated requests, repeated and multiple edits, whitespace, stale retries |
| `MessagesCorsTests` | EF Core in-memory | CORS headers for the frontend's origin and for unknown origins, preflights without a token, CORS headers on a 401 |
| `MessagesAuthorizationTests` | EF Core in-memory | Valid token accepted; missing, expired, wrongly signed, wrong issuer or audience, unsigned and malformed tokens rejected |
| `MessagesConcurrencyTests` | Real PostgreSQL in Docker (Testcontainers) | Simultaneous creates and edits of the same message, migrations |
| `MessagesPostgresStorageTests` | Real PostgreSQL in Docker (Testcontainers) | Time precision round-trip, text column limit |

`Pingo.Identity.ComponentTests` runs the real Identity service the same way, always against PostgreSQL:

- **Testcontainers** starts one PostgreSQL and one Redis container for all test classes (an xUnit
  collection fixture) and removes them when the run ends. The app applies its own migrations on startup.
- **Respawn** deletes all rows after each test, keeping the schema and the `schema_migrations`
  table, so every test starts from an empty database without restarting the app or the container.
- The clock is a `FakeTimeProvider`, so token expiry times are checked exactly and expiry is tested
  by moving the clock forward.

| Test class | Covers |
|---|---|
| `RegisterTests` | Successful registration, email taken (also in other letter case) |
| `LoginTests` | Successful login (token lifetimes, `sub` claim), email case, unknown email, wrong password |
| `RefreshTests` | Successful refresh, rotated, used, expired and unknown tokens, session end and its boundary, two sessions |
| `AccessTokenTests` | JWT signature and key, exact claims, `exp` matching the response, unique `jti`, refreshed token |
| `StorageTests` | Normalized email, salted PBKDF2 hash, refresh token stored as SHA-256, no orphan users, microsecond times |
| `ConcurrencyTests` | One refresh token used 10 times at once (forced race), one email registered 10 times at once |
| `ValidationTests` | Email, password and refresh token length limits, invalid email, empty, null and malformed bodies |
| `LocalizationTests` | Russian titles for every error, `?culture=ru`, language fallback order, English fallback |
| `RefreshTokenCleanupTests` | Expired tokens deleted and valid ones kept, the exact expiry boundary, the daily Hangfire schedule, a run triggered through Hangfire, the `expires_at` index |
| `StartupAndErrorTests` | Invalid JWT settings stop startup, an unhandled exception returns a 500 problem without details |

The PostgreSQL test classes need Docker running. The concurrency tests force a real race: an EF Core interceptor holds
every insert until all requests have reached it, then releases them together. To skip all Docker tests:

```bash
dotnet test Pingo.slnx --filter "Category!=Docker"
```

## Running locally

Requires the .NET 10 SDK and Docker.

Start PostgreSQL and Redis (local dev credentials are in `docker-compose.yml` and the APIs'
`appsettings.Development.json`):

```bash
docker compose up -d
```

Then start both projects (in separate terminals):

```bash
dotnet run --project Services/Message/Pingo.Messages.Api --launch-profile http
```

```bash
dotnet run --project Frontend/Pingo.Frontend --launch-profile http
```

Then open http://localhost:5221.

- API: http://localhost:5230 (OpenAPI document at `/openapi/v1.json` in Development)
- Identity API: `dotnet run --project Services/Identity/Pingo.Identity.Api --launch-profile http`,
  then Swagger UI at http://localhost:5240/swagger
- The frontend reads the API address from `Frontend/Pingo.Frontend/wwwroot/appsettings.json`.
- Allowed CORS origins are configured in the API's `appsettings.json`.