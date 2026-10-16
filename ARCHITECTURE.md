# Uno multiplayer: architecture

One page on how the pieces fit, and what every script is for. The message
contract itself lives in [server/PROTOCOL.md](server/PROTOCOL.md).

## Shape of the system

```
Unity client (C#)                    Nakama server (Go plugin)
──────────────────                   ─────────────────────────
NakamaConnection  ──── HTTP/RPC ───▶ rpc.go        find_match, quick_match,
     │                                             current_match
     │                              ───▶ wallet.go     get_wallet, debug RPCs
     │            ──── socket  ───▶ match.go      one authoritative match
     │                                  │          per game, 4 fixed seats
LobbyView / MatchmakingPanel ◀── LOBBY_STATE      │  stakes.go: escrow
MatchPresenter ──▶ MatchView ◀── GAME_STATE,    game/   pure Uno rules,
               ──▶ MatchSeatsView   EVENTS, ERROR,       no Nakama
               ──▶ ResultsView      REMOVED
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
| `main.go` | Plugin entry point. Registers the match handler, RPCs and the after-login hook that gives a new account its name, avatar index and starting coins. Refunds stakes left from a previous run |
| `names/names.go` | Generates names: Adjective + Animal + 2 digits (`SpryCrane15`), max 14 chars. Used for players and bots |
| `game/card.go` | Card types, the `cards.json` catalog and its validation |
| `game/deck.go` | Draw pile: build 108 cards, seeded shuffle, draw, refill from the discard pile |
| `game/state.go` | `GameState`: seats, hands, discard, direction, pending draws, the results. Deals a new game |
| `game/apply.go` | The rules. `Apply(seat, action)` validates a move, applies it and returns the events it caused |
| `game/end.go` | How a game ends: first empty hand wins, the rest ranked by cards left with a hidden penalty tie-break. Also the debug rigs `DebugWin` and `DebugOneCard` |
| `game/bot.go` | `BotAction`: the move a server-played seat makes. Placeholder logic: first legal card, most-held colour, otherwise draw, otherwise pass |
| `config.go` | Tunables in two storage objects you can edit in the Nakama console: `config/match` (timings, in seconds) and `config/economy` (coins). Defaults in code; bad values fall back and are logged |
| `wallet.go` | Coins in the Nakama wallet: starting coins once per account, the free-coins rescue applied on every wallet read, and the debug grants |
| `stakes.go` | Stakes: taking them at the start, the escrow record, payouts by place, forfeits, refunds, and recovery after a restart |
| `messages.go` | Every wire type and opcode from PROTOCOL.md, in Go |
| `match.go` | The authoritative match. Lobby: seats, countdown, bots filling empty seats, label updates. Play: stakes, deal, turn timer, client actions, bot turns, disguised seat takeover (with `REMOVED` to the player), leaving, rejoining, game over and payout, closing |
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
| `Services/Services.cs` | Where scripts find the shared services by interface: `Connection`, `Wallet`, `Assets`, `Popups` |
| `Startup/StartupLoader.cs` | The loading screen in the Startup scene: art, sign-in and coins, with a progress bar and retry, then the Lobby scene |
| `Network/NakamaConnection.cs` | `IConnectionService`. Owns the client, session and socket: device login, saved session, account name and avatar, join/leave match, reconnecting and rejoining after a drop, send actions, raises `OnMatchState` |
| `Network/Wallet.cs` | `IWalletService`. The coins as the server reports them, the free-coins wait, and the debug grants |
| `Data/GameAssets.cs` | `IAssetService`. Loads the card and avatar libraries through Addressables |
| `Network/UnoOpCodes.cs` | The opcode numbers from `PROTOCOL.md`, so both sides agree |
| `Network/LobbyMessages.cs`, `Network/MatchMessages.cs` | The JSON shapes from `PROTOCOL.md`, filled by `JsonUtility` |
| `Network/MatchSceneLoader.cs` | Lives with `NakamaConnection`. Opens MatchScene when the first `GAME_STATE` arrives, and goes back to the Lobby scene |
| `Network/MatchState.cs` | What this client believes the table looks like: your hand, seat counts, top card, whose turn. Built from a snapshot, mirrors the server's rules only to highlight cards |
| `Network/MatchPresenter.cs` | Drives a networked match. Applies each server event to `MatchState`, then raises it for the views one at a time, waiting for `StepComplete()` so animations never overlap. Sends your plays, draws and passes. Resyncs on a gap or an error |
| `UI/LobbyView.cs` | Home screen: name and avatar, the coin balance counting up and down, free coins, Play (off below the stake, or REJOIN MATCH) and Exit |
| `UI/MatchmakingPanel.cs` | The 2×2 searching panel. Empty seats cycle avatars; a seat stops when the server says it's taken. Your seat is bottom-left and never scrolls |
| `UI/ScreenFlow.cs` | Fades between the lobby and matchmaking panels via their CanvasGroups |
| `UI/MatchView.cs` | Draws the match from `MatchPresenter`: hands, discard pile, draw deck, cards flying between them. Turns clicks into moves (asking for a wild's colour first), blocks double sends, shows your-turn cues and the "removed" screen |
| `UI/MatchSeatsView.cs` | Seat nameplates: avatar, name, card count, the draining turn-timer ring, place at the end. Panel 0 is you, then the players after you in turn order |
| `UI/ResultsView.cs`, `UI/RankView.cs` | The results screen: one row per player with place, avatar ring by place, name, cards left and coins counting up; then home |
| `UI/PopupManager.cs` and the popups | `IPopupService`: one popup at a time, with a shared countdown (play or keep a drawn card, pick a colour, confirm leaving) |
| `Debug/DebugMenu.cs` | F1 testing tools, kept alive across scenes |
| `Utilities/CoinFormat.cs`, `Utilities/NumberCounter.cs` | `12.5K`-style coin amounts, and text that counts to a new number |
| `UI/HandView.cs` | One hand. Your cards face up and clickable, or an opponent's as a count of card backs. Laid out by a `HandLayout` |
| `Utilities/CurvedHandLayout.cs` | Your hand: a scrollable arc, playable cards lifted |
| `Utilities/OpponentFanLayout.cs` | An opponent's hand: a small fan of the newest few backs |
| `UI/DrawPileView.cs` | The face-down deck, thinning as it runs low. Clicking it draws |
| `UI/DiscardPileView.cs`, `UI/CardProxyView.cs` | The top discard, and the single card that flies between seats, deck and pile |
| `Data/CardDatabase.cs` | Card definitions and sprites by id (`RED_5`), matching `server/data/cards.json` |
| `Data/AvatarLibrary.cs` | ScriptableObject mapping the server's avatar index to a sprite |

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

1. **Login.** The loading screen loads the art, authenticates by device id and
   reads the wallet. On a brand new account a server hook assigns a name and
   avatar index, storing the name as the Nakama username, which is unique
   server-wide, and grants the starting coins.
2. **Find a match.** `find_match` refuses anyone below the stake, then lists
   open lobbies at today's stake; if none has a free seat it creates one. The
   client joins by id over the socket.
3. **Lobby.** A new lobby waits up to 10 seconds for its creator to actually
   join, then closes if nobody did. A 12-second countdown starts with the first
   player. Bots take
   empty seats at 8, 5 and 3 seconds left. A human who arrives later takes an
   empty seat, or the lowest bot seat if the table is full. At 1 second left the
   lobby closes to new players. `LOBBY_STATE` after every change drives the
   panel.
4. **Deal.** The server takes every human's stake (anyone short is replaced by
   a bot), deals 7 cards each and turns over a Number card, then sends every
   player their own `GAME_STATE`: their hand, and only card counts for everyone
   else.
5. **Play.** A client sends `PLAY_CARD`, `DRAW_CARD` or `PASS`. The server runs
   it through `game.Apply` and broadcasts the resulting `EVENTS` to everyone,
   with hidden cards stripped per player. An illegal move gets an `ERROR` back
   and changes nothing.
6. **Absent players.** Each turn has 8 seconds; running out makes the safe move.
   A dropped player keeps their seat and can rejoin. Three missed turns in a
   row, or pressing Leave, hands the seat to a bot under their name, and their
   stake is forfeit. The player gets `REMOVED` first, and their client explains
   why and goes home. If they were the last human, the match closes instead.
7. **End.** The first empty hand wins at once. Everyone else is ranked by cards
   left, and the pot is paid out by place. `GAME_OVER` carries each seat's
   place, cards left and coins for the results screen, and the match closes
   shortly after. A match that ends without a game over refunds the stakes.

## Why matchmaking is an RPC, not Nakama's matchmaker

Nakama's matchmaker pools players and matches them all at once. That fights the
experience we wanted: a lobby you sit in, seats filling one at a time, bots
appearing on a countdown, and a late player taking a bot's seat. Listing open
matches and creating one when there are none gives us that, and the matchmaker
can still sit in front of it later if matches ever need to be skill-based.
