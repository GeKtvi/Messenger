# Messenger

A learning project: a simple messenger built as a set of microservices.

For now it is a single public chat with no users — anyone can post anonymously.

## Homework stages

Each finished homework stage is marked with an annotated git tag, numbered with
two digits starting at `01` (`01`, `02`, `03`, ...).

| Tag  | Stage |
|------|-------|
| `01` | Message service (send/list, EF Core) + Blazor WASM/MudBlazor frontend |

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
Messenger.sln
├── Services/
│   └── Message/
│       └── Messenger.Message.Api   # ASP.NET Core minimal API + EF Core (PostgreSQL)
└── Frontend/
    └── Messenger.Frontend          # Blazor WebAssembly + MudBlazor
```

## Message service API

| Method | Route           | Description            |
|--------|-----------------|------------------------|
| GET    | `/api/messages` | Returns all messages   |
| POST   | `/api/messages` | Sends a message (`{ "text": "..." }`) |

Messages are stored in PostgreSQL. The schema is created automatically on first start.
Example requests are in `Messenger.Message.Api.http`.

## Running locally

Requires the .NET 10 SDK and Docker.

Start PostgreSQL (local dev credentials are in `docker-compose.yml` and the API's
`appsettings.Development.json`):

```bash
docker compose up -d
```

Then start both projects (in separate terminals):

```bash
dotnet run --project Services/Message/Messenger.Message.Api --launch-profile http
```

```bash
dotnet run --project Frontend/Messenger.Frontend --launch-profile http
```

Then open http://localhost:5221.

- API: http://localhost:5230 (OpenAPI document at `/openapi/v1.json` in Development)
- The frontend reads the API address from `Frontend/Messenger.Frontend/wwwroot/appsettings.json`.
- Allowed CORS origins are configured in the API's `appsettings.json`.
