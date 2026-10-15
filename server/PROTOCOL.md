# Uno client/server protocol v1

The contract between the Unity client and the Nakama Go match handler. Both
sides build against this document; change it here first.

## 1. Transport

- Nakama realtime socket, one server-authoritative match per game.
- Every message is a **match data message**: an integer opcode plus a JSON body
  (UTF-8, camelCase keys).
- Opcodes 1–49 are client→server, 100+ are server→client.
- The server validates everything against the rules in `server/game`. Clients
  never decide the outcome of a move.

### Shared shapes

| Shape | JSON |
|---|---|
| Card | `{ "id": 57, "defId": "RED_5" }` |
| Color | `"RED" \| "YELLOW" \| "GREEN" \| "BLUE"` (a card's own color may be `"WILD"`) |
| Seat | `0..3`; seats never change during a match |

`id` is the card instance id (0–107) and is what the client sends back.
`defId` maps to the sprite in Unity's `CardDatabase` and to `data/cards.json`.

**Hidden information rule:** ids are assigned in catalog order, so an id
identifies a card. The server therefore never sends the id of a card the
receiver may not see. Opponent hands are card counts only.

## 2. Identity

- Device authentication only (`AuthenticateDevice`, `create: true`). No email,
  no social login. Google/Apple can be linked to the same account later.
- On first login a server hook generates a name:
  **Adjective + Noun + Animal + two digits**, e.g. `BraveStormFalcon42`, at most
  20 characters. It is stored as **both** the Nakama username (globally unique,
  so a clash just means regenerating) and the display name.
- Bots draw from the same generator and never reuse a name already at the table,
  so bots are indistinguishable from players by name alone.
- **Local testing:** each game window must authenticate with a different device
  id, passed as `-uno-device win1` on the command line. Otherwise all windows
  are the same account and fight over one seat.

## 3. Matchmaking and the lobby

A match always has **4 seats**. Empty seats are filled with bots.

```
RPC find_match {}  ->  { "matchId": "…" }   then socket.JoinMatchAsync(matchId)
```

`find_match` returns a lobby that still has room, or creates one. A new lobby
survives `emptyLobbyGraceMs` without any player, so the client that created it
has time to join.

**Countdown** (all values configurable, see §8):

| Time | What happens |
|---|---|
| First player joins | 12 s countdown starts |
| 8 s / 5 s / 3 s left | If seats are still empty, one bot joins at each mark |
| Any time | A joining human takes an **empty** seat; only if there is none does it take the lowest-numbered bot seat |
| All 4 seats filled | Countdown ends immediately |
| 0 s | The match starts with whoever is seated |

- A player who leaves before the start frees their seat.
- If the last human leaves before the start, the match closes.
- **Nobody joins after the match has started.**

**op 100 `LOBBY_STATE`** — broadcast on every lobby change. `avatar` indexes the
client's AvatarLibrary. **Bots are disguised:** a bot's seat says `"human"`,
has a made-up `userId` and is always `connected`, so clients see only
`"human"` or `"empty"`:

```json
{
  "countdownMsLeft": 8000,
  "seats": [
    { "seat": 0, "kind": "human", "userId": "u-…", "name": "BraveStormFalcon42", "avatar": 7, "connected": true },
    { "seat": 1, "kind": "human", "userId": "3f2c…", "name": "QuietRiverOtter18", "avatar": 22, "connected": true },
    { "seat": 2, "kind": "empty" },
    { "seat": 3, "kind": "empty" }
  ]
}
```

## 4. Starting the match

1. The server deals: 7 cards per seat, then a Number card to start the discard
   pile (specials are reshuffled back, see the rules engine).
2. Each seat receives its own `GAME_STATE` with `startsInMs` (3 s).
3. The client holds on the full lobby screen, then shows the table with cards
   already dealt. There is no dealing animation and no per-card event.
4. After `preMatchMs` the server sends `TURN_CHANGED` for seat 0 and the turn
   timer starts.

**op 101 `GAME_STATE`** — personalized snapshot. Sent at match start, on
reconnect, and in reply to `REQUEST_STATE`:

```json
{
  "you": 2,
  "seats": [
    { "seat": 0, "kind": "human", "name": "BraveStormFalcon42", "cardCount": 7, "place": 0, "connected": true },
    { "seat": 1, "kind": "human", "name": "QuietRiverOtter18",  "cardCount": 7, "place": 0, "connected": true },
    { "seat": 2, "kind": "human", "name": "SwiftEmberTiger07",  "cardCount": 7, "place": 0, "connected": true },
    { "seat": 3, "kind": "human", "name": "CalmDuskHeron55",    "cardCount": 7, "place": 0, "connected": true }
  ],
  "hand": [ { "id": 3, "defId": "RED_1" } ],
  "topCard": { "id": 14, "defId": "RED_7" },
  "activeColor": "RED",
  "direction": 1,
  "currentSeat": 0,
  "pendingDraw": 0,
  "drawnCardId": -1,
  "deckCount": 79,
  "turnMsLeft": 8000,
  "startsInMs": 3000,
  "ranking": [],
  "seq": 0
}
```

- `hand` is the receiver's own hand only; `you` is their seat.
- `drawnCardId` is the card the current player drew and may still play, or -1.
  It is only ever sent to that player.
- `place` is 0 while playing, else the place at the end (see section 10).
- `results` appears once the game is over, as in `GAME_OVER`, so a client that
  rejoins then can still show them.
- `startsInMs` is only present before the first turn.
- Timers are always **time remaining in milliseconds**, never a clock time, so
  client clock skew does not matter.

## 5. Client → server

| Op | Name | Body | Valid when |
|---|---|---|---|
| 2 | `PLAY_CARD` | `{ "cardId": 57, "color": "BLUE" }` | Your turn. `color` required for `WILD` and `WILD_DRAW_FOUR`, ignored otherwise |
| 3 | `DRAW_CARD` | `{}` | Your turn, not yet drawn. Also how a pending +2/+4 is taken |
| 4 | `PASS` | `{}` | Your turn, after drawing a playable card |
| 5 | `REQUEST_STATE` | `{}` | Any time; the server replies with `GAME_STATE` |
| 6 | `EXTEND_TURN` | `{}` | Your turn, holding a drawn **wild** you are about to play, once per turn. Restarts your clock so choosing its color gets a full turn |
| 7 | `LEAVE_MATCH` | `{}` | During play: quit now. A bot takes the seat at once and you get `REMOVED` with `LEFT`. In the lobby, just leave the match |
| 90 | `DEBUG_WIN` | `{ "seat": 2 }` | **Debug only.** During play: that seat (any, bot or human) wins now, as if it had played its last card |
| 91 | `DEBUG_ONE_CARD` | `{ "seat": 2 }` | **Debug only.** During play: that seat's hand becomes one Wild; everyone gets a fresh `GAME_STATE` |

The debug ops answer `DEBUG_TOOLS_OFF` unless the server runs with
`UNO_DEBUG_TOOLS=true` (see section 11).

There is no start action: the countdown starts the match. The color for a wild
is chosen **before** sending, so a play is always one message.

**Choosing in time.** Clients run their own countdown on every choice popup
(play or keep a drawn card; pick a wild's color), set a little under the
server's remaining time so the client answers first. Out of time on play-or-keep
means keep (`PASS`); out of time on a color means a random color. Picking the
color for a drawn wild is a second choice in the same turn, so the client sends
`EXTEND_TURN` first and gets a fresh clock for it.

## 6. Server → client

**op 102 `EVENTS`** — everything one action caused, in order:

```json
{ "seq": 43, "events": [
  { "type": "CARD_PLAYED", "seat": 0, "card": { "id": 7, "defId": "RED_DRAW_TWO" }, "color": "RED" },
  { "type": "TURN_CHANGED", "seat": 1, "turnMs": 8000, "pendingDraw": 2 }
] }
```

| Event | Fields | Notes |
|---|---|---|
| `CARD_PLAYED` | `seat, card, color` | `color` is the color to match after the play |
| `CARDS_DRAWN` | `seat, count, cards` | **`cards` only goes to the drawer**; others get `count` |
| `PLAYER_SKIPPED` | `seat` | Sent with a Skip, so the client can animate it |
| `DIRECTION_CHANGED` | `direction` | `1` or `-1` |
| `TURN_CHANGED` | `seat, turnMs, pendingDraw` | `turnMs` is the full turn length, for bots too (they act after `botThinkMs`) |
| `TURN_EXTENDED` | `seat, turnMs` | `EXTEND_TURN` was accepted: that seat's clock restarts at `turnMs` |
| `TURN_TIMED_OUT` | `seat` | Precedes the automatic draw's events |
| `PLAYER_CONNECTION` | `seat, connected` | Grey out the nameplate |
| `GAME_OVER` | `seat, ranking, results` | `seat` won. See section 10 |

**op 103 `ERROR`** — to the offending client only; nothing changed:

```json
{ "code": "ILLEGAL_CARD", "message": "card can't be played on the current pile" }
```

| Code | Cause |
|---|---|
| `NOT_YOUR_TURN` | Acted out of turn, or just after timing out |
| `CARD_NOT_IN_HAND` | Client hand out of date |
| `ILLEGAL_CARD` | No match, or it does not answer a pending +2/+4 |
| `COLOR_REQUIRED` | Wild played without a valid color |
| `MUST_PLAY_DRAWN_CARD` | After drawing, tried to play another card |
| `ALREADY_DREW` | Two draws in one turn |
| `CANNOT_PASS` | Passed without having drawn a playable card |
| `GAME_NOT_STARTED` / `GAME_OVER` | Action outside the playing phase |
| `BAD_MESSAGE` | Unknown opcode or unreadable JSON |
| `CANNOT_EXTEND` | `EXTEND_TURN` twice in a turn, or without a drawn wild |
| `DEBUG_TOOLS_OFF` | A debug op on a server without the debug tools |

A well-behaved client never triggers these: it enables only playable cards, on
its own turn. Treat an error as a client bug, log it, and send `REQUEST_STATE`.

**op 104 `REMOVED`** — to one player, the moment they lose their seat, just
before they are kicked (or the match closes, if they were the last human):

```json
{ "reason": "MISSED_TURNS" }
```

| Reason | Cause |
|---|---|
| `MISSED_TURNS` | Ran out of time on 3 turns in a row, connected or not |
| `LEFT` | Sent `LEAVE_MATCH` |
| `NOT_ENOUGH_COINS` | Couldn't cover the stake when the match started; a bot took the seat. Arrives on the matchmaking screen |

Nothing else follows. The client should say why and go back to the home screen.

## 7. Timers, timeouts and bots

- **Owing cards with nothing to stack** (a pending +2/+4 and no matching draw
  card in hand): the server takes them for the player at once, after the
  `turnGapMs` pause. There is no choice to wait for, and it is not a missed turn.
- **Turn timer: 8 s.** On timeout the server makes the safe move and never plays
  a card: take a pending +2/+4, or draw one and pass.

  ```json
  { "seq": 14, "events": [
    { "type": "TURN_TIMED_OUT", "seat": 1 },
    { "type": "CARDS_DRAWN", "seat": 1, "count": 1 },
    { "type": "TURN_CHANGED", "seat": 2, "turnMs": 8000, "pendingDraw": 0 }
  ] }
  ```

- **A bot takes the seat** after either:
  - **3 missed turns in a row**, for any reason (acting in time resets the
    counter). A closed socket is not a reason by itself: the server can't tell
    a dropped connection from a quit, so a disconnected player's seat is held
    and their turns time out like anyone else's.
  - **`LEAVE_MATCH`** (op 7), which hands the seat over at once.
  - Quick matches (`quick_match`, for testing) skip the missed-turns rule: an
    idle player keeps timing out but keeps their seat.
- **Bots are disguised.** A bot that takes over keeps the player's name and
  avatar, and nobody is told. If the seat was shown as away, everyone gets
  `PLAYER_CONNECTION connected:true`.
- **The player is told first** with `REMOVED` (op 104), whichever way the seat
  is lost.
- **The last human leaving ends the match.** There is no point handing the final
  seat to a bot and playing to an empty room, so the match closes instead.
- **Takeover is permanent.** The player never gets the seat back. Reconnecting
  or relaunching lands them on the home screen; they do not spectate, and
  joining the match again is refused.
- **Reconnect before the takeover:** a dropped player keeps their
  `current_match` record, so the client (reconnecting by itself, or relaunched)
  rejoins the same match id and receives a fresh `GAME_STATE`. Others get
  `PLAYER_CONNECTION connected:true`.
- **Bot turns** have no timer; the bot acts after `botThinkMs` (default 1200 ms).
- **Every move is followed by `turnGapMs`** before the next seat may act, so a
  run of bot turns does not resolve in a single frame and clients have time to
  animate.
- **Nobody left:** when no human has been connected for ~10 s, the match closes.
- **Phase 2 bot:** placeholder logic (first legal card, most-held color,
  otherwise draw and pass) behind one `BotAction` function. The heuristic bot
  replaces it later without protocol changes.

## 8. Configuration

Tunables live in Nakama **storage objects**, one per area, so they can be edited
in the developer console with no rebuild (`localhost:7351` by default; this
repo's Docker setup maps it to `localhost:7451`).

Values use units that read well and say so in their names: seconds (decimals
allowed, e.g. `1.2`) and minutes. The server converts them on load.

**`config` / `match`**: lobby and turn timings. Read when a match is
**created**, so edits apply to new matches, never to one in progress.

```json
{
  "lobbyCountdownSeconds": 12,
  "botJoinAtSecondsLeft": [8, 5, 3],
  "joinCutoffSeconds": 1,
  "preMatchSeconds": 3,
  "turnSeconds": 8,
  "botThinkSeconds": 1.2,
  "turnGapSeconds": 0.5,
  "missedTurnsForBot": 3,
  "emptyLobbyGraceSeconds": 10,
  "noHumansCloseSeconds": 10,
  "postGameCloseSeconds": 30
}
```

**`config` / `economy`**: coins. Read on every wallet request, so edits apply
at once.

```json
{
  "startingCoins": 500,
  "stake": 50,
  "payoutPercents": [60, 30, 10, 0],
  "freeCoinsBelow": 50,
  "freeCoinsUpTo": 100,
  "freeCoinsEveryMinutes": 240
}
```

`stake` is read when a lobby is created, and that lobby keeps it. `stake: 0`
makes every match free. `payoutPercents` is one share per place, up to four,
adding up to 100 or less; the rest stays with the house.

- Defaults live in Go, so an empty database works.
- Missing fields keep their default; invalid values fall back to the default and
  are logged.
- The `reset_config` RPC writes both objects with every default filled in.
- Rules constants (7-card hands, 4 seats, 108 cards) stay in code.
- Message fields such as `turnMs` in `TURN_CHANGED` are protocol, not config,
  and stay in milliseconds.

## 9. Staying in sync

- Every `EVENTS` message carries `seq`, one higher than the last; snapshots
  carry the current `seq`.
- Client rules: `seq == last+1` → animate; `seq <= last` → ignore (duplicate);
  `seq > last+1` → send `REQUEST_STATE` and redraw from the snapshot.
- **RPC `current_match {}` → `{ "matchId": "…", "phase": "playing" }` or empty.**
  Lets a relaunched or reconnected client find the match it still holds a human
  seat in. Rejoin only while `phase` is `playing`; empty means home screen.

## 10. End of the match

```json
{ "seq": 57, "events": [
  { "type": "CARD_PLAYED", "seat": 2, "card": { "id": 5, "defId": "RED_3" }, "color": "RED" },
  { "type": "GAME_OVER", "seat": 2, "ranking": [2, 0, 3, 1], "results": [
    { "seat": 2, "place": 1, "cardsLeft": 0, "penalty": 0,  "cards": [] },
    { "seat": 0, "place": 2, "cardsLeft": 2, "penalty": 7,  "cards": [ ... ] },
    { "seat": 3, "place": 2, "cardsLeft": 2, "penalty": 7,  "cards": [ ... ] },
    { "seat": 1, "place": 4, "cardsLeft": 1, "penalty": 50, "cards": [ ... ] }
  ] }
] }
```

- The first player to empty their hand wins, and the game ends at once. Their
  last card's effect (a Draw Two, say) does not happen.
- Everyone else is ranked by **cards left**, fewest first. Equal counts are
  split by the **penalty** of those cards: a number card its face value,
  Skip / Reverse / Draw Two 20, Wild and Wild Draw Four 50, lower first.
  Players equal on both share the place (shown as "=2"); the next place is
  skipped, as above.
- Players see places and coins (and may see cards left). The penalty is never
  shown: it's only the tie-break, sent so the client can check it.
- `results` is in place order, with each seat's cards left and the `coins` its
  place won. `GAME_OVER` also carries the `stake` and the `pot`. A bot seat's
  coins are only for show: the house keeps them. See section 11.
- The match stays open for `postGameCloseMs` (30 s) for the results screen, then
  the server closes it. No rematch in v1.

## 11. Coins

Coins live in each account's Nakama wallet (currency `coins`). Only the server
changes it, and every change is in the wallet ledger with a `reason`:
`starting`, `free`, `debug` (later `stake`, `payout`, `refund`).

- **Starting coins:** every account gets `startingCoins` once, at login. Accounts
  made before coins existed get them at their next login.
- **Free coins** rescue players who can't afford a match, and are added
  automatically. Once the balance is below `freeCoinsBelow`, a wait of
  `freeCoinsEveryMinutes` starts; when it's over, the next wallet read tops the
  balance up to exactly `freeCoinsUpTo`. Getting back to `freeCoinsBelow` or more
  cancels the wait, so time with enough coins is never saved up. Nothing changes
  while the player is seated in a match being played.

All wallet RPCs reply with the same shape:

```json
{ "coins": 100, "freeCoinsAdded": 70, "freeCoinsWaiting": false,
  "freeCoinsInMs": 0, "freeCoinsBelow": 50, "freeCoinsUpTo": 100 }
```

- `freeCoinsAdded`: coins this very call topped up (0 almost always), so the
  client can announce them.
- `freeCoinsWaiting`: below the line, with a top-up on its way.
  `freeCoinsInMs` is how long until it's due, so the client never compares
  clocks with the server. When it reaches 0, read the wallet again.

| RPC | Body | Does |
|---|---|---|
| `get_wallet` | `{}` | Balance, after topping up free coins if they're due |
| `debug_grant_coins` | `{ "amount": 500 }` | Adds coins, or takes them with a negative amount (not below 0). -100000 to 100000. **Debug only** |
| `debug_free_coins_now` | `{}` | If below the line, tops up now. **Debug only** |

The wallet reply also carries `stake`, what a match costs today, so the client
can turn Play off below it.

Debug RPCs answer `PERMISSION_DENIED` unless the server runs with
`UNO_DEBUG_TOOLS=true` in its runtime env, which only `local.yml` sets.

### Stakes

Every match from `find_match` is played for `stake` coins a seat. Quick matches
are free.

- **Entering.** `find_match` fails with `NOT_ENOUGH_COINS` below the stake, and
  joining a staked lobby is refused with that reason. Nothing is charged in the
  lobby, so leaving it is free.
- **The start.** Each human's stake is taken in one transaction. Anyone who
  can no longer cover it gets `REMOVED` with `NOT_ENOUGH_COINS` and a bot takes
  the seat. Bots pay in too, from the house, so the pot is always 4 stakes.
- **Game over.** The pot is paid by place (`payoutPercents`). Seats sharing a
  place pool the shares of the places they cover and split them evenly; what
  doesn't divide stays with the house. Only humans still in their seat are
  paid. A seat a bot took over forfeited its stake, and its winnings stay with
  the house.
- **Ending early.** A match that closes without a game over (everyone
  dropped, the server shutting down) refunds every human still in their seat.
- **Ledger.** Every movement is in the wallet ledger with `reason` `stake`,
  `payout` or `refund`, and the `matchId`.

The held stakes live in a server-owned record, collection `escrow`, key the
match id. It's created in the same transaction that takes the stakes, records
forfeits, and is deleted in the same transaction that pays out or refunds. So
it exists exactly while coins are held. On startup the server refunds every
record left over: matches live only in memory, so those matches died with the
last server.

## 12. Not in v1

Private rooms and room codes, matchmaking by skill, rematch, spectating,
reconnecting after a bot takeover, chat and emotes, stats and leaderboards.
