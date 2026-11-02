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
│       └── Pingo.Identity                  # Business layer: users, password hashing, tokens
└── Frontend/
    └── Pingo.Frontend              # Blazor WebAssembly + MudBlazor
```

## Message service API

| Method | Route                         | Description |
|--------|-------------------------------|-------------|
| GET    | `/api/messages`               | Returns all messages, oldest first |
| PUT    | `/api/messages/{messageId}`   | Sends a message (`{ "text": "..." }`). Idempotent, see below |

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
| POST   | `/api/auth/login`    | Signs in with `{ "email": "...", "password": "..." }`. Returns an access token and a refresh token, or `401` |

- **Storage:** users and their email/password sign-ins are kept in separate tables (`users`,
  `password_logins`), so other ways to sign in can be added later.
- **Passwords:** hashed with PBKDF2-HMAC-SHA256, 600 000 iterations and a random salt per password.
- **Emails:** compared case-insensitively.
- **Access token:** a JWT signed with HS256, valid for 15 minutes, with the user id in `sub`. Send it
  as `Authorization: Bearer <accessToken>`. JWT settings are in the `Jwt` section of
  `appsettings.json`; the development signing key is in `appsettings.Development.json`.
- **Refresh token:** a random 256-bit string, valid for 30 days. Only its SHA-256 hash is stored.

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
- The clock is a `FakeTimeProvider`, so send and edit times can be checked exactly.

| Test class | Database | Covers |
|---|---|---|
| `MessagesApiTests` | EF Core in-memory | Listing, sending, ordering, editing, trimming, length limit, validation errors |
| `MessagesIdempotencyTests` | EF Core in-memory | Repeated requests, repeated and multiple edits, whitespace, stale retries |
| `MessagesCorsTests` | EF Core in-memory | CORS headers for the frontend's origin and for unknown origins |
| `MessagesConcurrencyTests` | Real PostgreSQL in Docker (Testcontainers) | Simultaneous creates and edits of the same message, migrations |
| `MessagesPostgresStorageTests` | Real PostgreSQL in Docker (Testcontainers) | Time precision round-trip, text column limit |

The PostgreSQL test classes need Docker running. The concurrency tests force a real race: an EF Core interceptor holds
every insert until all requests have reached it, then releases them together. To skip them:

```bash
dotnet test Pingo.slnx --filter "Category!=Docker"
```

## Running locally

Requires the .NET 10 SDK and Docker.

Start PostgreSQL (local dev credentials are in `docker-compose.yml` and the API's
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