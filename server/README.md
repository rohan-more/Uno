# Uno server

A Go runtime plugin for [Nakama](https://heroiclabs.com/docs/nakama/) 3.40.0.

## Run locally

From the repo root:

```bash
docker compose up -d --build
```

- API and realtime socket: `localhost:7350`
- Developer console: http://localhost:7351 (admin / password)

If another Nakama is already using those ports, override them:

```bash
NAKAMA_GRPC_PORT=7449 NAKAMA_HTTP_PORT=7450 NAKAMA_CONSOLE_PORT=7451 docker compose up -d --build
```

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
