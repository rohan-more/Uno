# Uno Multiplayer

A four-player online Uno game: **Unity** client, **Nakama** game server with an
authoritative match handler written in **Go**.

It started as a single-player prototype. The rules now live on the server, which
deals the cards, decides what is legal and tells each client only what that
player is allowed to see.

> **Status: playable, in progress.** A full four-player match runs end to end
> over the network: login, matchmaking, lobbies with bots, dealing, turns with an
> 8-second timer, bots taking over absent players, and a match scene that plays
> it all out. A results screen, rematch and deployment are next; see the
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
- **The client plays events, it doesn't simulate.** A presenter applies each
  server event to the client's view of the table, then hands it to the UI one
  at a time and waits for its animation, so a burst of bot moves never
  overlaps on screen.

## Architecture

```
Unity client (C#)                      Nakama server (Go plugin)
─────────────────                      ─────────────────────────
NakamaConnection  ──── RPC ──────────▶ find_match / quick_match /
       │                                current_match
       │          ──── socket ───────▶ match handler: 4 seats, lobby
       │                                countdown, bots, turn timer,
       │                                seat takeover
 LobbyView                                        │
 MatchmakingPanel ◀─── LOBBY_STATE ───────────────┤
 MatchPresenter   ◀─── GAME_STATE, EVENTS, ───────┘
   ├─ MatchView        ERROR, REMOVED             │
   └─ MatchSeatsView                   game/ — pure Uno rules,
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
| Tests | `go test`: 87 cases covering rules, deck, bots, lobby, turn timeouts, seat takeover and message visibility |

## Run it

```bash
git clone https://github.com/rohan-more/Uno.git
cd Uno
docker compose up -d --build
```

The API is then on `localhost:7450` and the Nakama console on
http://localhost:7451 (admin / password). Open the project in Unity, load
`Assets/Scenes/Boot.unity` and press Play.

To skip the lobby and play three bots straight away, tick **Instant Match** on
the `Nakama` object in the Boot scene.

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
- [x] Card play over the network: turns, 8-second timer, events
- [x] Bots playing a real game, and taking over absent seats
- [x] Match scene for four players: face-down opponent hands, nameplates,
      turn-timer rings, card animations
- [x] Draw deck, with a play-or-keep choice for a playable drawn card
- [x] Choice countdowns that answer for you on timeout, and owed +2/+4
      cards taken automatically
- [ ] Results screen and rematch
- [ ] Stronger bot, deployment

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

**The bug that looked like a rules bug.** Matches kept freezing, usually right
after a Reverse. The rules were fine: 500 simulated four-bot games with about
2,800 Reverses all finished. The real cause was one line of ordering in the
client's event presenter. It marked itself busy *after* handing an event to the
UI, overwriting the UI's "done" signal, so every event waited out a 3-second
safety timeout. The client drifted seconds behind the server's turn clock, your
turn appeared on screen with almost no time left, two timeouts handed your seat
to a bot, and as the only human the match closed without telling anyone.
Reverse was just when the turn came back to you unexpectedly. The fix was the
ordering, plus a `REMOVED` message so a player who loses their seat is told why
and sent home instead of left staring at a frozen table.

**Two more from the lobby.** "Close a match with no players" killed every
lobby 200 ms after creation, before its creator could join. And storing the
session under one key meant four test windows all restored the first window's
token and logged in as the same account. Both were correct rules that were wrong
in sequence.

## License

See [LICENSE](LICENSE). Card artwork and avatars belong to their respective
owners and are used here for a non-commercial portfolio project.
