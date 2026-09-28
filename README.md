# Uno Multiplayer

A four-player online Uno game: **Unity** client, **Nakama** game server with an
authoritative match handler written in **Go**.

It started as a single-player prototype. The rules now live on the server, which
deals the cards, decides what is legal and tells each client only what that
player is allowed to see.

> **Status: in progress.** Login, matchmaking, lobbies with bots and dealing all
> work end to end. Card play over the network is being built next; see the
> [roadmap](#roadmap).

<!-- TODO: demo.gif — four windows finding each other and filling a lobby -->

## What makes it interesting

- **The server owns the game.** Clients send intents ("play card 57") and render
  what comes back. They never decide whether a move is legal, so a modified
  client cannot cheat.
- **Hidden information is enforced per player.** Each player's snapshot has their
  own hand; everyone else is a card count. When someone draws, only they receive
  the card, everyone else receives the number of cards. This is checked by tests.
- **Matches always have four seats.** A lobby counts down from 12 seconds, bots
  take empty seats at 8, 5 and 3 seconds, and a late player takes a bot's seat
  rather than waiting for another game.
- **The rules are a standalone Go package** with no server dependencies, so the
  whole card game is unit-tested in milliseconds and can drive bots and
  simulations.
- **Games are reproducible.** Every shuffle comes from a seeded generator, so a
  seed plus the list of actions replays a match exactly.

## Architecture

```
Unity client (C#)                      Nakama server (Go plugin)
─────────────────                      ─────────────────────────
NakamaConnection  ──── RPC ──────────▶ find_match / current_match
       │          ──── socket ───────▶ match handler: 4 seats, lobby
       │                                countdown, bots, turn order
 LobbyView                                        │
 MatchmakingPanel ◀─── LOBBY_STATE ───────────────┤
 (match scene)    ◀─── GAME_STATE, EVENTS ────────┘
                                                  │
                                       game/ — pure Uno rules,
                                       no Nakama imports
```

- [ARCHITECTURE.md](ARCHITECTURE.md) — what every script does, and how a match
  flows from login to game over.
- [server/PROTOCOL.md](server/PROTOCOL.md) — the client/server contract: every
  opcode, message shape, timer and error code.

## Tech

| | |
|---|---|
| Client | Unity 2022.3 (URP), C#, TextMeshPro, DOTween |
| Server | Go 1.26 plugin for Nakama 3.40 |
| Infra | Docker Compose, PostgreSQL 16 |
| Tests | `go test`: 73 cases covering rules, deck, lobby and message visibility |

## Run it

```bash
git clone https://github.com/rohan-more/Uno.git
cd Uno
docker compose up -d --build
```

The API is then on `localhost:7450` and the Nakama console on
http://localhost:7451 (admin / password). Open the project in Unity, load
`Assets/Scenes/Boot.unity` and press Play.

To test multiplayer on one machine, build for Windows and start the executable
several times: each window claims its own profile and logs in as a different
player, so no command-line arguments are needed.

Server tests:

```bash
cd server && go test ./...
```

## Roadmap

- [x] Uno rules engine in Go, with tests
- [x] Device login, generated names (`SpryCrane15`) and avatars
- [x] Matchmaking: lobbies, 12-second countdown, bots filling seats
- [x] Dealing and per-player snapshots
- [ ] Card play over the network: turns, 8-second timer, events
- [ ] Bots playing a real game, and taking over absent seats
- [ ] Match scene UI for four players
- [ ] Rankings, rematch, deployment

## Engineering notes

**Tests that earn their place.** The rules are checked by 300 random full games
that verify, after every single move, that all 108 cards still exist exactly
once and that no finished player is ever given a turn. To confirm the tests
actually catch regressions, I broke the code on purpose in a dozen ways (turn
order, stacking, the discard reshuffle, the negative modulo in seat order) and
required each one to fail a test. Two mutations survived, which showed up real
gaps in coverage.

**Protocol first.** The client/server contract was written and agreed before the
match handler existed, so both sides could be built against the same document
instead of one chasing the other.

**Matchmaking is an RPC, not Nakama's matchmaker.** The built-in matchmaker
pools players and matches them all at once. The design here is a lobby you sit
in: seats fill one at a time, bots appear on a countdown, and a late player can
take a bot's seat. Listing open matches and creating one when there are none
gives that; the matchmaker can still sit in front of it later if matches ever
need to be skill-based.

**Two bugs worth remembering.** "Close a match with no players" killed every
lobby 200 ms after creation, before its creator could join. And storing the
session under one key meant four test windows all restored the first window's
token and logged in as the same account. Both were correct rules that were wrong
in sequence.

## License

See [LICENSE](LICENSE). Card artwork and avatars belong to their respective
owners and are used here for a non-commercial portfolio project.
