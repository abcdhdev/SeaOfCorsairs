# Sea Wars Backend (Local Dev)

This folder contains two backend services:

- `auth-service`: registration/login, JWT access tokens, refresh tokens
- `player-data-service`: per-player JSON state, and S3 (MinIO) presigned URLs for logs/assets

## Run Everything (Docker)

From the repo root:

```powershell
docker compose up --build
```

Ports:

- Auth API: `http://localhost:8081`
- Player Data API: `http://localhost:8082`
- Postgres: `localhost:5432` (databases: `authdb`, `playerdb`)
- Redis: `localhost:6379`
- MinIO (S3 API): `http://localhost:9000`
- MinIO console: `http://localhost:9001`

If you want to override secrets, create a `.env` file at the repo root (see `.env.example`).

Notes:

- `JWT_SIGNING_KEY` must be at least 32 bytes for HS256.
- Presigned MinIO URLs are generated using `S3__ServiceUrl` and should be reachable by the game client (default: `http://localhost:9000`).
- Netcode admission now rejects client/server version mismatches.
  - `SEAWARS_REQUIRED_PROTOCOL_VERSION` (default `1`)
  - `SEAWARS_REQUIRED_CLIENT_VERSION` (default: server build `Application.version`)

## API Quickstart

Auth:

- `POST /v1/auth/register` -> returns `{ accessToken, refreshToken, expiresInSeconds }`
- `POST /v1/auth/login` -> returns `{ accessToken, refreshToken, expiresInSeconds }`
- `POST /v1/auth/refresh` -> rotates refresh token
- `GET /v1/auth/me` -> requires `Authorization: Bearer <token>`

Player Data:

- `GET /v1/player/me` -> requires `Authorization: Bearer <token>`
- `PUT /v1/player/me/state` -> body `{ state: <any json>, expectedVersion?: number }`
- `POST /v1/logs/presign` -> presigned PUT for uploading logs to MinIO
- `GET /v1/assets/presign?key=...` -> presigned GET for assets

Swagger is enabled in `Development`:

- Auth Swagger: `http://localhost:8081/swagger`
- Player Data Swagger: `http://localhost:8082/swagger`
