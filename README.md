# Uno Multiplayer

A four-player online Uno game: **Unity** client, **Nakama** game server with an
authoritative match handler written in **Go**.

It started as a single-player prototype. The rules now live on the server, which
deals the cards, decides what is legal and tells each client only what that
player is allowed to see.

> **Status: playable, in progress.** A full four-player match runs end to end
> over the network: a loading screen, login, matchmaking, lobbies with bots,
> dealing, turns with an 8-second timer, bots quietly taking over absent players,
> reconnecting, matches played for coin stakes, and a results screen. Rematch
> and deployment are next; see the [roadmap](#roadmap).

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
- **Coins can't be lost or paid twice.** Every match is played for a stake, paid
  out 60/30/10 by place. The stakes are held in an escrow record that's created
  in the same database transaction that takes them, and deleted in the same one
  that pays out or refunds, so a crash can't land in between. Records left after
  a restart are refunded on startup.
- **Bots are disguised.** Nobody can tell a bot from a person, including when a
  bot quietly takes over a player who left, under their name and avatar.
- **Free coins rescue, they don't refill.** Below one stake, a 4-hour wait tops
  you up to 100, automatically. Anything above that is only ever won, so losing
  costs something.
- **The client plays events, it doesn't simulate.** A presenter applies each
  server event to the client's view of the table, then hands it to the UI one
  at a time and waits for its animation, so a burst of bot moves never
  overlaps on screen.

## Architecture

```
Unity client (C#)                      Nakama server (Go plugin)
─────────────────                      ─────────────────────────
NakamaConnection  ──── RPC ──────────▶ find_match / quick_match /
       │                                current_match / get_wallet
       │          ──── socket ───────▶ match handler: 4 seats, lobby
       │                                countdown, bots, turn timer,
       │                                seat takeover, stakes
 LobbyView                                        │
 MatchmakingPanel ◀─── LOBBY_STATE ───────────────┤
 MatchPresenter   ◀─── GAME_STATE, EVENTS, ───────┘
   ├─ MatchView        ERROR, REMOVED             │
   ├─ MatchSeatsView                   game/ — pure Uno rules,
   └─ ResultsView                      no Nakama imports
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
| Tests | `go test`: 126 cases covering rules, ranking, deck, bots, lobby, turn timeouts, seat takeover, message visibility, wallet, free coins and stakes |

## Run it

```bash
git clone https://github.com/rohan-more/Uno.git
cd Uno
docker compose up -d --build
```

The API is then on `localhost:7450` and the Nakama console on
http://localhost:7451 (admin / password). Run the `reset_config` RPC once from
the console's API Explorer, so the tunables appear under Storage → `config`.
Open the project in Unity, load `Assets/Scenes/Startup.unity` and press Play.
(Pressing Play in another scene goes through Startup first.)

To skip the lobby and play three bots straight away, tick **Instant Match** on
the `Nakama` object in the Startup scene. Those quick matches are free; real
matches cost a stake.

**F1** opens testing tools while the server runs with `UNO_DEBUG_TOOLS=true`
(set in `server/local.yml`): drop the connection, grant or take coins, top up
free coins, and make any player win now or hold a single Wild.

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
- [x] Leaving, reconnecting and rejoining a match in progress; disguised bots
- [x] Coins: starting coins, a free-coins rescue, stakes, payouts by place,
      escrow and refunds
- [x] Results screen
- [ ] Rematch, stake tiers
- [ ] Stronger bot, deployment

## Engineering notes

**Tests that earn their place.** The rules are checked by 300 random full games
that verify, after every single move, that all 108 cards still exist exactly
once, and that every game ends with an empty-handed winner and every seat ranked. To confirm the tests
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
