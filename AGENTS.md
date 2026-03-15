# AGENTS

## Backend (Docker)

This repo includes a Dockerized backend stack used by the Unity client.

Key files:
- `docker-compose.yml` (all containers)
- `docker/postgres/init.sql` (bootstrap DBs/users)
- `backend/README.md` (backend-specific doc)
- `.env.example` (compose env overrides)

### Services

- `auth-service` (`backend/src/SeaWars.AuthService`): registration/login + JWT access tokens (HS256) + refresh tokens.
- `player-data-service` (`backend/src/SeaWars.PlayerDataService`): per-player JSON state (Postgres `jsonb`), Redis cache, and MinIO S3 presigned URLs for logs/assets.
- `postgres` (16): databases `authdb`, `playerdb` (role `seawars` / password `seawars`).
- `redis` (7): used for login rate-limit counters and caching.
- `minio` + `minio-init`: S3-compatible object storage.

### Run

From repo root:

```powershell
docker compose up --build
```

Ports:
- Auth API: `http://localhost:8081`
- Player Data API: `http://localhost:8082`
- Postgres: `localhost:5432`
- Redis: `localhost:6379`
- MinIO (S3 API): `http://localhost:9000`
- MinIO console: `http://localhost:9001`

Health:
- `GET http://localhost:8081/health`
- `GET http://localhost:8082/health`

Swagger (Development):
- `http://localhost:8081/swagger`
- `http://localhost:8082/swagger`

### Environment (.env)

Create a `.env` at repo root to override compose variables (see `.env.example`).

- `JWT_SIGNING_KEY`: used by both services; must be at least 32 bytes for HS256.
- `MINIO_ROOT_USER`, `MINIO_ROOT_PASSWORD`: MinIO root credentials (also used for presign signing keys).

### API Summary

Auth (`auth-service`):
- `POST /v1/auth/register` -> `{ accessToken, refreshToken, expiresInSeconds }`
- `POST /v1/auth/login` -> `{ accessToken, refreshToken, expiresInSeconds }`
- `POST /v1/auth/refresh` -> rotates refresh token
- `POST /v1/auth/logout` -> revokes refresh token (idempotent)
- `GET /v1/auth/me` -> requires `Authorization: Bearer <token>`

Player Data (`player-data-service`):
- `GET /v1/player/me` -> returns `{ version, state, updatedAt }` (requires JWT)
- `PUT /v1/player/me/state` -> body `{ state: <any json>, expectedVersion?: number }`
  - Uses optimistic concurrency when `expectedVersion` is provided; returns `409` with current version on conflict.
- `POST /v1/logs/presign` -> presigned `PUT` URL for uploading logs to MinIO
- `GET /v1/assets/presign?key=...` -> presigned `GET` URL for downloading assets

Notes:
- Presigned URLs are generated using `S3__ServiceUrl` and should be reachable by the game client (default is `http://localhost:9000`).

### Database Migrations (EF Core)

Both services auto-apply EF migrations on startup (`Database.Migrate()` with retry).

The repo uses a local `dotnet-ef` tool manifest under `backend/.config/dotnet-tools.json`.

Examples:

```powershell
cd backend

# Auth DB migration
dotnet tool run dotnet-ef migrations add AddSomething `
  --project .\src\SeaWars.AuthService\SeaWars.AuthService.csproj `
  --startup-project .\src\SeaWars.AuthService\SeaWars.AuthService.csproj `
  --context AuthDbContext `
  --output-dir Data\Migrations

# Player DB migration
dotnet tool run dotnet-ef migrations add AddSomething `
  --project .\src\SeaWars.PlayerDataService\SeaWars.PlayerDataService.csproj `
  --startup-project .\src\SeaWars.PlayerDataService\SeaWars.PlayerDataService.csproj `
  --context PlayerDbContext `
  --output-dir Data\Migrations
```

### Optional Codex Skill

I created a Codex skill at `C:\Users\Abcd\.codex\skills\sea-wars-backend` to help with recurring backend/compose work.

### Game Client

## Game Network Stack
Game uses Unity Netcode for GameObjects (NGO) for networking.

## Unity Editor
Use Unity MCP for Unity Editor tasks.

#### Project-specific docs:
- [Spawn System](docs/spawn-system.md) - Player spawn points, authored NPC spawn points, respawn rules, and setup guidance.