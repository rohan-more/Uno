# Uno server

A Go runtime plugin for [Nakama](https://heroiclabs.com/docs/nakama/) 3.40.0.

## Run locally

From the repo root:

```bash
docker compose up -d --build
```

- API and realtime socket: `localhost:7350`
- Developer console: http://localhost:7351 (admin / password)

Host ports come from `.env` at the repo root, because another local Nakama
already uses the defaults:

```
NAKAMA_GRPC_PORT=7449
NAKAMA_HTTP_PORT=7450
NAKAMA_CONSOLE_PORT=7451
```

So the real addresses are `localhost:7450` for the client and
http://localhost:7451 for the console. Delete `.env` to go back to 7349-7351.

Both containers use `restart: unless-stopped`, so they come back on their own
when Docker Desktop starts. If the client reports "Could not connect", check
`docker ps` first: a running Docker daemon does not mean this stack is up.

After changing Go code, run the same `up -d --build` command again; the plugin is compiled inside Docker.

Stop the stack with `docker compose down`. Add `-v` to also delete the database.

## Version pinning

Nakama only loads a plugin built with exactly matching dependencies. These must move together:

| Where | Value |
|---|---|
| `Dockerfile`, both `FROM` lines | `3.40.0` |
| `go.mod`, `nakama-common` | `v1.47.0` |
| `go.mod`, `go` directive | not newer than the pluginbuilder's Go (1.26.5) |
| `go.mod`, shared deps such as `protobuf` | same as Nakama's own `go.mod` at that tag |

## Layout

- `main.go` — `InitModule`, where RPCs and match handlers get registered.
- `data/cards.json` — the 108-card deck. IDs match the Unity `CardDatabase`.
