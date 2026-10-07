# Uno multiplayer: architecture

One page on how the pieces fit, and what every script is for. The message
contract itself lives in [server/PROTOCOL.md](server/PROTOCOL.md).

## Shape of the system

```
Unity client (C#)                    Nakama server (Go plugin)
──────────────────                   ─────────────────────────
NakamaConnection  ──── HTTP/RPC ───▶ rpc.go        find_match, quick_match,
     │                                             current_match
     │            ──── socket  ───▶ match.go      one authoritative match
     │                                  │          per game, 4 fixed seats
LobbyView / MatchmakingPanel ◀── LOBBY_STATE      │
MatchPresenter ──▶ MatchView ◀── GAME_STATE,    game/   pure Uno rules,
               ──▶ MatchSeatsView   EVENTS, ERROR,       no Nakama
                                    REMOVED
```

**The server is authoritative.** The client sends intents ("play card 57") and
renders what comes back. It never decides whether a move is legal, what a
player drew, or whose turn it is. That removes cheating and keeps all four
clients in agreement.

**The rules are a separate package.** `server/game` has no Nakama imports, so
the whole card game can be unit-tested with `go test` in milliseconds, and the
same code can drive bots or a simulator.

## Server (Go, `server/`)

| File | What it does |
|---|---|
| `main.go` | Plugin entry point. Registers the match handler, RPCs and the after-login hook that gives a new account its name and avatar index |
| `names/names.go` | Generates names: Adjective + Animal + 2 digits (`SpryCrane15`), max 14 chars. Used for players and bots |
| `game/card.go` | Card types, the `cards.json` catalog and its validation |
| `game/deck.go` | Draw pile: build 108 cards, seeded shuffle, draw, refill from the discard pile |
| `game/state.go` | `GameState`: seats, hands, discard, direction, pending draws, finishing places. Deals a new game |
| `game/apply.go` | The rules. `Apply(seat, action)` validates a move, applies it and returns the events it caused |
| `game/bot.go` | `BotAction`: the move a server-played seat makes. Placeholder logic: first legal card, most-held colour, otherwise draw, otherwise pass |
| `config.go` | Tunable timings. Defaults in code, overridden by a storage object you can edit in the Nakama console; bad values fall back and are logged |
| `messages.go` | Every wire type and opcode from PROTOCOL.md, in Go |
| `match.go` | The authoritative match. Lobby: seats, countdown, bots filling empty seats, label updates. Play: deal, turn timer, client actions, bot turns, seat takeover (with `REMOVED` to the player), game over and closing |
| `rpc.go` | `find_match` (list open lobbies, else create), `quick_match` (deal straight away against bots, for testing), `current_match` (which match you still hold a seat in), `reset_config` |
| `data/cards.json` | The 108-card deck, shared with the Unity sprite ids |

### Two ideas worth knowing

**Card instance ids.** Every physical card gets an id 0–107 at the start of a
match. Players refer to cards by that id, so two `RED_5`s are never confused.
The server only ever sends the ids of cards you are allowed to see.

**Seeded randomness.** Shuffles come from a generator created from the match
seed. Log the seed and the actions, and a whole game can be replayed exactly,
which makes bug reports reproducible.

## Client (C#, `Assets/Scripts/`)

| File | What it does |
|---|---|
| `Network/NakamaConnection.cs` | Owns the client, session and socket. Device login, saved session, account name and avatar, join/leave match, send actions, raises `OnMatchState` |
| `Network/UnoOpCodes.cs` | The opcode numbers from `PROTOCOL.md`, so both sides agree |
| `Network/LobbyMessages.cs`, `Network/MatchMessages.cs` | The JSON shapes from `PROTOCOL.md`, filled by `JsonUtility` |
| `Network/MatchSceneLoader.cs` | Lives with `NakamaConnection`. Opens MatchScene when the first `GAME_STATE` arrives, and goes back to Boot |
| `Network/MatchState.cs` | What this client believes the table looks like: your hand, seat counts, top card, whose turn. Built from a snapshot, mirrors the server's rules only to highlight cards |
| `Network/MatchPresenter.cs` | Drives a networked match. Applies each server event to `MatchState`, then raises it for the views one at a time, waiting for `StepComplete()` so animations never overlap. Sends your plays, draws and passes. Resyncs on a gap or an error |
| `UI/LobbyView.cs` | Home screen: shows name and avatar, Play and Exit buttons |
| `UI/MatchmakingPanel.cs` | The 2×2 searching panel. Empty seats cycle avatars; a seat stops when the server says it's taken. Your seat is bottom-left and never scrolls |
| `UI/ScreenFlow.cs` | Fades between the lobby and matchmaking panels via their CanvasGroups |
| `UI/MatchView.cs` | Draws the match from `MatchPresenter`: hands, discard pile, draw deck, cards flying between them. Turns clicks into moves (asking for a wild's colour first), blocks double sends, shows your-turn cues and the "removed" screen |
| `UI/MatchSeatsView.cs` | Seat nameplates: avatar, name, card count, the draining turn-timer ring, finishing place. Panel 0 is you, then the players after you in turn order |
| `UI/HandView.cs` | One hand. Your cards face up and clickable, or an opponent's as a count of card backs. Laid out by a `HandLayout` |
| `Utilities/CurvedHandLayout.cs` | Your hand: a scrollable arc, playable cards lifted |
| `Utilities/OpponentFanLayout.cs` | An opponent's hand: a small fan of the newest few backs |
| `UI/DrawPileView.cs` | The face-down deck, thinning as it runs low. Clicking it draws |
| `UI/DiscardPileView.cs`, `UI/CardProxyView.cs` | The top discard, and the single card that flies between seats, deck and pile |
| `Data/CardDatabase.cs` | Card definitions and sprites by id (`RED_5`), matching `server/data/cards.json` |
| `Data/AvatarLibrary.cs` | ScriptableObject mapping the server's avatar index to a sprite |

**Offline leftovers, safe to delete:** `UI/TestGameController.cs`,
`Data/Rules/*`, `Data/DeckModel.cs`, `Data/HumanDecisionMaker.cs`. They ran the
single-player prototype; MatchScene keeps `TestGameController` disabled and
nothing networked uses them.

### Two ideas worth knowing on the client

**State first, then the event.** The presenter updates `MatchState` before it
raises an event, so a view never works anything out for itself: it redraws
from `State`. Animations that need the "before" picture (a hand growing card by
card) start from the final state and hide what hasn't landed yet.

**One step at a time.** Server batches can hold several moves. Every event a
view handles must end in exactly one `StepComplete()`, immediately or when its
animation lands; otherwise the presenter waits out a 3 s timeout and the client
falls behind the server's turn clock.

## How one match flows

1. **Login.** The client authenticates by device id. On a brand new account a
   server hook assigns a name and avatar index, storing the name as the Nakama
   username, which is unique server-wide.
2. **Find a match.** `find_match` lists open lobbies; if none has a free seat it
   creates one. The client joins by id over the socket.
3. **Lobby.** A new lobby waits up to 10 seconds for its creator to actually
   join, then closes if nobody did. A 12-second countdown starts with the first
   player. Bots take
   empty seats at 8, 5 and 3 seconds left. A human who arrives later takes an
   empty seat, or the lowest bot seat if the table is full. At 1 second left the
   lobby closes to new players. `LOBBY_STATE` after every change drives the
   panel.
4. **Deal.** The server deals 7 cards each and turns over a Number card, then
   sends every player their own `GAME_STATE`: their hand, and only card counts
   for everyone else.
5. **Play.** A client sends `PLAY_CARD`, `DRAW_CARD` or `PASS`. The server runs
   it through `game.Apply` and broadcasts the resulting `EVENTS` to everyone,
   with hidden cards stripped per player. An illegal move gets an `ERROR` back
   and changes nothing.
6. **Absent players.** Each turn has 8 seconds; running out makes the safe move.
   Two missed turns, 15 seconds disconnected, or leaving outright hands the seat
   to a bot for the rest of the match. The player gets `REMOVED` first, and their
   client explains why and goes home. If they were the last human, the match
   closes instead.
7. **End.** Emptying your hand gives you a finishing place and the others play
   on. When one player is left, `GAME_OVER` carries the ranking and the match
   closes shortly after.

## Why matchmaking is an RPC, not Nakama's matchmaker

Nakama's matchmaker pools players and matches them all at once. That fights the
experience we wanted: a lobby you sit in, seats filling one at a time, bots
appearing on a countdown, and a late player taking a bot's seat. Listing open
matches and creating one when there are none gives us that, and the matchmaker
can still sit in front of it later if matches ever need to be skill-based.
