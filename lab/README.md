# Multi-region lab

Pingo behind a reverse proxy in Docker, for trying out multi-region setups locally. It has its own
containers and data and doesn't touch the development `docker-compose.yml`.

So far there is one region (EU):

```
browser → localhost:8001 → edge-eu (YARP)
                             /api/auth/*      → identity-eu
                             /api/messages/*  → messages-eu
                             everything else  → frontend (nginx)
```

## Run

From the repository root:

```bash
docker compose -f lab/docker-compose.yml up -d --build
```

- Pingo: http://localhost:8001 (the lab has its own empty database, so create an account first)
- Jaeger: http://localhost:16687

Stop it, keeping the messages (add `-v` to delete them too):

```bash
docker compose -f lab/docker-compose.yml down
```

## Good to know

- The browser talks to one origin, so there are no CORS preflights, and the `SameSite=Strict` refresh
  cookie simply works.
- Cookies ignore ports, so the lab and a locally run Pingo share `pingo_refresh` on `localhost`:
  logging in to one can sign you out of the other on its next reload.
- A trace starts at `pingo-edge` and continues through the API behind it.
