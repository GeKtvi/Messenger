# Multi-region lab

Pingo in three pretend regions behind reverse proxies in Docker, sharing one primary database in
the EU, with a read replica in every region. It has its own containers and data and doesn't touch the
development `docker-compose.yml`.

```
user in   EU               US               Asia
          localhost:8001   localhost:8002   localhost:8003
            │                │                │
          edge-eu          edge-us          edge-asia       YARP; routes by path, picks a region
            │                │                │             (any edge can reach any region)
          identity-eu      identity-us      identity-asia
          messages-eu      messages-us      messages-asia
            │                │ 90 ms          │ 200 ms      Toxiproxy (wan-us, wan-asia)
            └────────────────┴────────────────┘
              postgres, redis, rabbitmq (in the EU)

Each region's Message API lists messages from the replica next to it (postgres-eu, postgres-us,
postgres-asia). The replicas follow postgres, the US and Asian ones through the same delays. Everything
else goes to postgres.
```

The delays are per round trip and set in `wan-setup.sh`: EU–US 90 ms, EU–Asia 200 ms, US–Asia 150 ms.
They apply between an edge and another region's APIs, and between an API and the EU's PostgreSQL, Redis
and RabbitMQ. Toxiproxy accepts connections locally, so the TCP handshake itself isn't delayed. The
frontend's files aren't delayed either, as if each edge had them cached.

## Run

From the repository root:

```bash
docker compose -f deploy/multi-region/docker-compose.yml up -d --build
```

- Pingo, as seen from each region: http://localhost:8001 (EU), http://localhost:8002 (US),
  http://localhost:8003 (Asia). The lab has its own empty database, so create an account first.
- Jaeger: http://localhost:16687. Each span's process has a `cloud.region` tag.
- Every API response has an `X-Pingo-Region` header: the region that answered.

Stop it, keeping the messages (add `-v` to delete them too):

```bash
docker compose -f deploy/multi-region/docker-compose.yml down
```

## Routing

Each edge has an ordered list of regions (`Edge__RegionOrder`) and uses the first available one. By
default the nearest region comes first. To send everyone to the EU, next to the database, set the
same order on every edge:

```powershell
$env:REGION_ORDER = 'eu,us,asia'; docker compose -f deploy/multi-region/docker-compose.yml up -d
```

And back to the nearest region:

```powershell
$env:REGION_ORDER = $null; docker compose -f deploy/multi-region/docker-compose.yml up -d
```

## Experiment: is the nearest region faster?

```bash
python deploy/multi-region/measure.py
```

| User in | Routing | List messages | Send message | Log in |
|---|---|---|---|---|
| EU | either | 6 ms | 7 ms | 134 ms |
| US | nearest | 101 ms | 196 ms | 403 ms |
| US | everyone to the EU | 98 ms | 100 ms | 247 ms |
| Asia | nearest | 210 ms | 417 ms | 727 ms |
| Asia | everyone to the EU | 223 ms | 210 ms | 318 ms |

With one database, the nearest region is never faster. Listing is one query, so one long trip either
way. Sending is two queries and logging in is more, and from a nearby API each of them is a long
trip, while one trip to the EU covers the whole request. (The numbers are from one run, before the
read replicas, and vary a bit.)

## Experiment: failover

Every edge asks each API for `/health` once a second. After two failures in a row the API gets no more
traffic until a check succeeds again. This is per service, so logins stay in Asia while its chat moves:

```bash
docker compose -f deploy/multi-region/docker-compose.yml kill messages-asia
```

Use the chat on http://localhost:8003 and watch `X-Pingo-Region` change from `asia` to `us`. Then:

```bash
docker compose -f deploy/multi-region/docker-compose.yml start messages-asia
```

| What happens in Asia | Result |
|---|---|
| `kill messages-asia` | Errors for 4 to 5.5 s, then the US answers |
| `start messages-asia` | The US keeps answering for 4 to 5 s, with no errors, then Asia again |
| `pause messages-asia` (frozen, port open) | A request already waiting fails after 10 s; later ones go to the US |
| `stop messages-us` as well | The EU answers |
| `stop postgres` | Every region answers `500`: the APIs are alive, so there's nothing to fail over to |

The frontend retries Message API calls, so a message sent during the gap still arrives, just later.
Identity calls aren't retried, so a login during its gap fails and has to be repeated.

## Experiment: read replicas

`python deploy/multi-region/measure.py`, nearest-region routing:

| User in | List messages, without a replica | With a replica | Send message (unchanged) |
|---|---|---|---|
| EU | 5 to 30 ms | 5 to 20 ms | 8 ms |
| US | 101 ms | 5 to 20 ms | 195 ms |
| Asia | 210 ms | 5 to 20 ms | 415 ms |

Only reading from far away gets faster. The EU's replica takes reads off the primary and keeps them
working without it. A replica refuses writes, so sending still makes two trips to the EU.

**Lag.** Replicas follow the primary with a delay. Stop Asia's replica from following:

```bash
docker compose -f deploy/multi-region/docker-compose.yml exec postgres-asia psql -U messenger -d messenger_messages -c "select pg_wal_replay_pause()"
```

Send a message on http://localhost:8003 and reload: it's still there, although the list no longer
contains it. The frontend keeps its own sent messages until a list does (D-069). Open the same chat in a
private window, as another user, and the message is missing until `pg_wal_replay_resume()`.

| What stops | Result |
|---|---|
| `postgres-asia` | Asia's list comes from the primary again, after at most a failed request or two |
| `postgres` (the primary) | Every region can still list messages. Sending and logging in fail |

## Good to know

- The first request over a database connection that a PostgreSQL restart has cut answers `500`; the next
  one works. The frontend retries that for messages, but not for login or restoring the session, which
  then shows the login form.

- The browser talks to one origin, so there are no CORS preflights, and the `SameSite=Strict` refresh
  cookie simply works.
- Cookies ignore ports, so the three edges share a login on `localhost`, like one user travelling. A
  locally run Pingo shares it too: logging in to one can sign you out of the other on its next reload.
- Any region accepts any region's tokens: they share the JWT key and the database.
- The Message APIs share one RabbitMQ queue, so one of the three regions handles each login event.
- A trace starts at the edge and continues through the API behind it.
