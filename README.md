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
| `05` | Hangfire + Redis cleanup of expired refresh tokens, JWT auth in the Message service, login UI with an HttpOnly refresh cookie, refresh on 401, retries with Http.Resilience |
| `06` | Login event over RabbitMQ with MassTransit (greeting in the chat), OpenTelemetry tracing to Jaeger across both services, login unit tests with Moq |

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
│       ├── Pingo.Identity.ComponentTests   # Component tests: the whole service over HTTP, real PostgreSQL
│       └── Pingo.Identity.UnitTests        # Unit tests: login logic with Moq, no database
├── Shared/
│   ├── Pingo.Contracts                     # Events exchanged over RabbitMQ (UserLoggedIn)
│   └── Pingo.ServiceDefaults               # Setup shared by the services: OpenTelemetry tracing, /health
├── Edge/
│   ├── Pingo.Edge                          # YARP reverse proxy in front of the frontend and APIs
│   └── Pingo.Edge.Tests                    # Region choice and failover against fake APIs
├── deploy/
│   └── multi-region                        # Docker Compose lab: three regions with artificial delays
└── Frontend/
    ├── Pingo.Frontend              # Blazor WebAssembly + MudBlazor
    └── Pingo.Frontend.Tests        # Unit tests: token handling against fake APIs
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

The service also listens to RabbitMQ: when Identity publishes `UserLoggedIn`, it posts
"Привет, user@example.com!" to the chat. The event's id becomes the message id, so a redelivered
event doesn't greet twice.

If PostgreSQL restarted, the first query over a pooled connection fails; it's repeated on a new one
(D-071). The same goes for Identity.

Sending is idempotent. The client picks the message id (a `Guid`):

- **New id:** creates the message, returns `201 Created`.
- **Existing id, new text:** updates the text and `updatedAt`, returns `200 OK`.
- **Existing id, same text:** changes nothing, returns `200 OK`. Retrying a request is always safe.

Lists can come from a read replica (`ConnectionStrings:MessagesRead`; without it, everything uses the
primary). Sending always uses the primary (D-068).

Messages are stored in PostgreSQL. The schema is managed with EF Core migrations, which the API
applies automatically on startup. Example requests are in `Pingo.Messages.Api.http`.

### Changing the database schema

After changing an entity, add a migration (the `dotnet-ef` tool is pinned in `dotnet-tools.json`):

```bash
dotnet tool restore
```

```bash
dotnet ef migrations add <MigrationName> --project Services/Message/Pingo.Messages --startup-project Services/Message/Pingo.Messages.Api --context MessageDbContext --output-dir Data/Migrations
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
| POST   | `/api/auth/login`    | Signs in with `{ "email": "...", "password": "..." }`. Returns an access token and sets the refresh token cookie, or `401` (unknown email, or wrong password) |
| POST   | `/api/auth/refresh`  | No body: uses the refresh token cookie. Returns a new access token and sets a new cookie, or `401` |
| POST   | `/api/auth/logout`   | Ends the session behind the cookie and deletes the cookie. Always `204` |

- **Storage:** users and their email/password sign-ins are kept in separate tables (`users`,
  `password_logins`), so other ways to sign in can be added later.
- **Passwords:** hashed with PBKDF2-HMAC-SHA256, 600 000 iterations and a random salt per password.
- **Emails:** compared case-insensitively.
- **Access token:** a JWT signed with HS256, valid for 15 minutes, with the user id in `sub`. Send it
  as `Authorization: Bearer <accessToken>`. JWT settings are in the `Jwt` section of
  `appsettings.json`; the development signing key is in `appsettings.Development.json`.
- **Refresh token:** a random 256-bit string, sent only as an `HttpOnly`, `Secure`,
  `SameSite=Strict` cookie (`pingo_refresh`, path `/api/auth`), never in a response body, so page
  scripts can't read it. Only its SHA-256 hash is stored. Each refresh token works once: refreshing
  sets a new one and invalidates the old one. The session ends 30 days after login, however often
  it's refreshed.
- **CORS:** the frontend's origins may call Identity with credentials (the cookie).
- **Login event:** each successful login publishes `UserLoggedIn { Email }` to RabbitMQ through
  MassTransit 8 (the Message service greets the user). If RabbitMQ is unreachable, the login still
  succeeds after at most 5 seconds and the event is lost.
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

## Tracing

Both services send traces to Jaeger through OpenTelemetry (setup in `Pingo.ServiceDefaults`). A
login shows as one trace across both services: the HTTP request, Identity's PostgreSQL calls, the
`UserLoggedIn` publish, and the Message service's consumer with its own PostgreSQL calls. The trace
context travels in the `traceparent` HTTP header and in RabbitMQ message headers.

Open http://localhost:16686, pick the service `pingo-identity` and the operation
`POST api/auth/login`, then *Find Traces*.

## Health check

Both APIs answer `GET /health` with `200 Healthy`, without a token. It says only that the instance is
running, not that PostgreSQL or RabbitMQ is reachable (D-062), and it isn't traced.

## Frontend: signing in

- Opening the app shows a login form, which can also create an account. Identity's error messages
  are shown as they come, in the browser's language.
- After login, the access token is kept only in memory. The refresh token stays in its `HttpOnly`
  cookie, which the browser sends to Identity by itself; the app never sees it.
- Reloading the page restores the session from the cookie with one `/refresh` call, so there's no
  new login while the 30-day session lasts.
- Every Message API request carries `Authorization: Bearer <access token>`, added by
  `AccessTokenHandler` (a `DelegatingHandler` on the typed `HttpClient`).
- When the access token has expired, the Message API answers `401`. The handler then refreshes it
  through the cookie and sends the request once more, so the user doesn't notice. Stream bodies
  aren't resent (they may already be consumed, and buffering them would cost memory); the token is
  still refreshed for the caller's next attempt. Requests that get
  `401` at the same time share one refresh, because each refresh token works only once. If the
  session has ended, the login form comes back.
- **Log out** ends the session in Identity and clears unsent messages.
- **Retries:** if the Message API fails for a moment (a 5xx, a timeout, or the server being
  unreachable), the request is repeated up to 3 times with growing delays (about 0.5 s, 1 s, 2 s).
  This uses `Microsoft.Extensions.Http.Resilience` (Polly v8 underneath) and sits under the token
  handler. Calls to Identity aren't retried, so a refresh never spends its single-use cookie twice.

## Frontend: pending messages

The UI sends messages the way most messengers do:

- A sent message appears in the chat right away as **Sending…**, and you can keep typing and send
  more without waiting.
- Once the server confirms it, it becomes a normal message.
- If sending fails, it's marked **Not sent** with **Retry** and **Discard** buttons.
- Pending messages are kept in the browser's `localStorage` (`pingo.outbox`), so they survive a
  reload and are resent automatically when the page opens. Each one keeps its client-generated id,
  so resending never creates a duplicate.
- Once confirmed, a message is kept there with the server's copy until a loaded list contains it,
  because a list from a read replica may lag behind (D-069). Such a message is
  never resent.

After each sent message the list is loaded again, which also shows other people's new messages. A new
list is merged into what's shown and never removes anything. Without sending or reloading the page,
other people's messages don't appear: that would need a push channel such as SignalR.

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
| `MessagesUserLoggedInTests` | EF Core in-memory, MassTransit test harness | Greeting posted for a `UserLoggedIn` event, a redelivered event greets once, two logins greet twice |
| `MessagesTracingTests` | Real PostgreSQL in Docker, MassTransit test harness | The consumer continues the publisher's trace, with the greeting's `INSERT` under its span |
| `MessagesReplicaTests` | EF Core in-memory, a second database as a lagging replica | Lists read the replica, sends look ids up on the primary, the read-only context refuses to save |
| `MessagesDatabaseRestartTests` | Real PostgreSQL in Docker | Listing and sending right after the server ended every connection, with one host and with two listed |
| `MessagesHealthTests` | EF Core in-memory | `/health` without a token, with a failing dependency, and not traced |
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
| `UserLoggedInEventTests` | `UserLoggedIn` published with the normalized email on every login, not on failed logins, registration or refresh |
| `TracingTests` | A login is one trace: the HTTP span contains the PostgreSQL spans and the `UserLoggedIn` publish; a failed login publishes nothing |
| `RefreshCookieTests` | Cookie attributes, no token in the body, missing, invalid and overlong cookies, logout, CORS with credentials, a browser-like cookie jar |
| `RefreshTokenCleanupTests` | Expired tokens deleted and valid ones kept, the exact expiry boundary, the daily Hangfire schedule, a run triggered through Hangfire, the `expires_at` index |
| `DatabaseRestartTests` | Login, refresh and registration right after the server ended every connection, also with two hosts listed |
| `HealthTests` | `/health` answers |
| `StartupAndErrorTests` | Invalid JWT settings stop startup, an unhandled exception returns a 500 problem without details |

`Pingo.Identity.UnitTests` tests `AuthService.LoginAsync` alone, with Moq mocks for the
repositories, the password hasher and MassTransit's `IPublishEndpoint` (no web host, database or
broker): a correct login returns tokens, stores the refresh token and publishes `UserLoggedIn`; an
unknown email or a wrong password returns the matching `LoginError` and stores and publishes nothing;
the email is normalized; a failing publish doesn't fail the login.

`Pingo.Frontend.Tests` runs the frontend's HTTP pipeline against fake Message and Identity APIs,
with no browser or Docker:

| Test class | Covers |
|---|---|
| `AccessTokenHandlerTests` | Bearer header, refresh and retry on `401`, one shared refresh for parallel `401`s, signing out when the session has ended, resending JSON bodies but not streams |
| `MessageFeedTests` | Loading again after a send, merging lists without losing messages or edits, failed and overlapping loads, starting fresh after logout |
| `MessageOutboxTests` | Sent messages shown until a list contains them, also after a reload; merging without duplicates, no resending of delivered ones, logout, retries |
| `MessageApiResilienceTests` | The app's real registration: retries on 5xx, unreachable server and slow attempts, giving up after 3 retries, no retry on 4xx, sends retried with the same id, a `503` followed by an expired token |

`Pingo.Edge.Tests` runs the real edge in front of fake APIs on local ports, with no Docker
(`FailoverTests`): region order, failing over when an API stops or reports unhealthy, returning when
it's back, each service on its own, and every region being down.

The PostgreSQL test classes need Docker running. The concurrency tests force a real race: an EF Core interceptor holds
every insert until all requests have reached it, then releases them together. To skip all Docker tests:

```bash
dotnet test Pingo.slnx --filter "Category!=Docker"
```

## Running locally

Requires the .NET 10 SDK and Docker.

Start PostgreSQL, Redis, RabbitMQ and Jaeger (local dev credentials are in `docker-compose.yml` and the APIs'
`appsettings.Development.json`):

```bash
docker compose up -d
```

Then start the three projects (in separate terminals):

```bash
dotnet run --project Services/Identity/Pingo.Identity.Api --launch-profile http
```

```bash
dotnet run --project Services/Message/Pingo.Messages.Api --launch-profile http
```

```bash
dotnet run --project Frontend/Pingo.Frontend --launch-profile http
```

Then open http://localhost:5221.

- API: http://localhost:5230 (OpenAPI document at `/openapi/v1.json` in Development)
- Identity API: http://localhost:5240, with Swagger UI at http://localhost:5240/swagger
- RabbitMQ management UI: http://localhost:15672 (guest / guest): queues, exchanges, message rates
- Jaeger UI: http://localhost:16686: traces from both services
- The frontend reads both API addresses from `Frontend/Pingo.Frontend/wwwroot/appsettings.json`.
- Allowed CORS origins are configured in the API's `appsettings.json`.

## Docker images

Each API and the frontend has a `Dockerfile`. Build them from the repository root:

```bash
docker build -f Services/Identity/Pingo.Identity.Api/Dockerfile -t pingo-identity .
```

```bash
docker build -f Services/Message/Pingo.Messages.Api/Dockerfile -t pingo-messages .
```

```bash
docker build -f Frontend/Pingo.Frontend/Dockerfile -t pingo-frontend .
```

All three listen on port 8080 and run as a non-root user. The images contain no passwords or keys:
the APIs get connection strings and the JWT key from environment variables, e.g.
`ConnectionStrings__Messages` and `Jwt__SigningKey`.

## Multi-region lab

[deploy/multi-region/README.md](deploy/multi-region/README.md) runs Pingo from these images in three pretend regions,
with artificial delays between them.
