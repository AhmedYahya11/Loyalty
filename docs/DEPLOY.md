# Deployment · النشر

The whole platform ships as four containers via `docker-compose.yml`:

| Service | Image | Port | Purpose |
|---|---|---|---|
| `postgres` | postgres:16-alpine | 5432 | database + durable Wolverine queue |
| `api` | `./backend` (Loyalty.Api) | 8080 | REST API; runs EF migrations at startup |
| `worker` | `./backend` (Loyalty.Worker) | — | dedicated event processor (competing consumer) |
| `dashboard` | `./dashboard` (Next.js) | 3000 | admin UI + customer card (`/card`) |

## Quick start (local / single host)

```bash
cp .env.example .env         # then edit the secrets (see below)
docker compose up --build -d # build + start all four services
docker compose ps            # all "running"/"healthy"
```

- **Dashboard** → http://localhost:3000  · **API** → http://localhost:8080 (Scalar at `/scalar`)
- First boot with an empty database: bootstrap the root tenant once:
  ```bash
  curl -X POST http://localhost:8080/api/v1/bootstrap \
    -H "X-Bootstrap-Token: $BOOTSTRAP_TOKEN" -H "Content-Type: application/json" \
    -d '{"name":"My Company","nameAr":"شركتي"}'
  ```
  Save the returned `rootAccount.apiKey` — it is shown **once**. Then onboard apps, set loyalty
  settings, and start emitting events (see `README.md` + `integration/`).

## Configuration (`.env`)

| Var | Meaning |
|---|---|
| `POSTGRES_DB/USER/PASSWORD` | database credentials. **Changing the password needs a fresh `pgdata` volume** (Postgres initializes it once). |
| `BOOTSTRAP_TOKEN` | strong random secret gating the one-time `/api/v1/bootstrap`. |
| `CORS_ORIGINS` | comma-separated browser origins allowed to call the API (`*` = any; set your dashboard origin in prod). |
| `ASPNETCORE_ENVIRONMENT` | `Production` for deploys. |
| `DASHBOARD_API_BASE` | public URL the **browser** uses to reach the API (baked into the dashboard bundle at build time). |
| `DASHBOARD_API_KEY` | optional tenant key baked in for turnkey auto-connect; leave blank to connect via the UI. |

> `DASHBOARD_API_BASE`/`DASHBOARD_API_KEY` are `NEXT_PUBLIC_*` — they are compiled into the client
> bundle, so **rebuild the dashboard image** (`docker compose build dashboard`) after changing them.

## Operate

```bash
docker compose logs -f api worker      # tail logs
docker compose up -d --build api       # redeploy the API after a code change
docker compose down                    # stop (keeps data)
docker compose down -v                 # stop + WIPE the database volume
```

- **Backup / restore**:
  ```bash
  docker compose exec postgres pg_dump -U "$POSTGRES_USER" "$POSTGRES_DB" > backup.sql
  cat backup.sql | docker compose exec -T postgres psql -U "$POSTGRES_USER" "$POSTGRES_DB"
  ```
- **Health**: `GET /health` (liveness), `GET /health/ready` (readiness incl. Postgres).
- **Scale processing**: run extra workers — `docker compose up -d --scale worker=3` (they are
  competing consumers on the durable Postgres queue; processing is idempotent).

## Public / production hardening (beyond this local stack)

The compose is production-shaped but still single-host. For a public deployment also:

1. **TLS + reverse proxy** — put Caddy/Nginx/Traefik in front; terminate HTTPS; route
   `api.<domain>` → api:8080 and `app.<domain>` → dashboard:3000. Set `CORS_ORIGINS=https://app.<domain>`
   and build the dashboard with `DASHBOARD_API_BASE=https://api.<domain>`.
2. **Secrets** — use a secret manager / Docker secrets, not a committed `.env`. Rotate
   `BOOTSTRAP_TOKEN` and DB credentials.
3. **Managed Postgres** — point `LOYALTY_DB` at a managed instance with backups + encryption at rest;
   drop the `postgres` service.
4. **Don't publish 5432** — remove the Postgres `ports:` mapping so the DB is reachable only on the
   internal network.
5. **Rate limits / body cap** — tune `RateLimit:EventsPerSecond` and `Limits:MaxRequestBodyBytes`.
6. **Observability** — set `OTEL_EXPORTER_OTLP_ENDPOINT` to ship traces/metrics.
7. **Images** — build + push to a registry in CI and deploy by tag instead of building on the host.
