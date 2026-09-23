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

`find_match` returns a lobby that still has room, or creates one.

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

**op 100 `LOBBY_STATE`** — broadcast on every lobby change:

```json
{
  "countdownMsLeft": 8000,
  "seats": [
    { "seat": 0, "kind": "human", "userId": "u-…", "name": "BraveStormFalcon42", "connected": true },
    { "seat": 1, "kind": "bot",   "name": "QuietRiverOtter18" },
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
    { "seat": 1, "kind": "bot",   "name": "QuietRiverOtter18",  "cardCount": 7, "place": 0, "connected": true },
    { "seat": 2, "kind": "human", "name": "SwiftEmberTiger07",  "cardCount": 7, "place": 0, "connected": true },
    { "seat": 3, "kind": "human", "name": "CalmDuskHeron55",    "cardCount": 7, "place": 0, "connected": true }
  ],
  "hand": [ { "id": 3, "defId": "RED_1" } ],
  "topCard": { "id": 14, "defId": "RED_7" },
  "activeColor": "RED",
  "direction": 1,
  "currentSeat": 0,
  "pendingDraw": 0,
  "drawnCardId": null,
  "deckCount": 79,
  "turnMsLeft": 8000,
  "startsInMs": 3000,
  "ranking": [],
  "seq": 0
}
```

- `hand` is the receiver's own hand only; `you` is their seat.
- `drawnCardId` is set only for the current player, after they drew a playable
  card.
- `place` is 0 while playing, else the finishing position.
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

There is no start action: the countdown starts the match. The color for a wild
is chosen **before** sending, so a play is always one message.

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
| `TURN_CHANGED` | `seat, turnMs, pendingDraw` | `turnMs` is 0 for a bot seat (no countdown ring) |
| `TURN_TIMED_OUT` | `seat` | Precedes the automatic draw's events |
| `SEAT_CONTROL` | `seat, kind` | Only ever `human` → `bot`; permanent |
| `PLAYER_CONNECTION` | `seat, connected` | Grey out the nameplate |
| `PLAYER_FINISHED` | `seat, place` | Play continues among the rest |
| `GAME_OVER` | `ranking` | Seats in finishing order |

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
| `SEAT_TAKEN_BY_BOT` | Rejoined after being replaced |
| `BAD_MESSAGE` | Unknown opcode or unreadable JSON |

A well-behaved client never triggers these: it enables only playable cards, on
its own turn. Treat an error as a client bug, log it, and send `REQUEST_STATE`.

## 7. Timers, timeouts and bots

- **Turn timer: 8 s.** On timeout the server makes the safe move and never plays
  a card: take a pending +2/+4, or draw one and pass.

  ```json
  { "seq": 14, "events": [
    { "type": "TURN_TIMED_OUT", "seat": 1 },
    { "type": "CARDS_DRAWN", "seat": 1, "count": 1 },
    { "type": "TURN_CHANGED", "seat": 2, "turnMs": 8000, "pendingDraw": 0 }
  ] }
  ```

- **A bot takes the seat** on whichever comes first:
  - **2 missed turns in a row** (acting in time resets the counter), or
  - **15 s disconnected**, so a drop right after one's turn is noticed before the
    turn comes round again.
- **Takeover is permanent.** The player never gets the seat back. Reconnecting
  or relaunching lands them on the home screen; they do not spectate. Rejoining
  is refused with `SEAT_TAKEN_BY_BOT`.
- **Reconnect inside the window:** the client rejoins the same match id (see
  `current_match`) and receives a fresh `GAME_STATE`. Others get
  `PLAYER_CONNECTION connected:true`.
- **Bot turns** have no timer; the bot acts after `botThinkMs` (default 0).
- **Nobody left:** when no human has been connected for ~10 s, the match closes.
- **Phase 2 bot:** placeholder logic (first legal card, most-held color,
  otherwise draw and pass) behind one `BotAction` function. The heuristic bot
  replaces it later without protocol changes.

## 8. Configuration

Tunables live in a Nakama **storage object** so they can be edited in the
developer console at `localhost:7351` with no rebuild:

```
collection: config
key:        match
```

```json
{
  "lobbyCountdownMs": 12000,
  "botJoinAtMsLeft": [8000, 5000, 3000],
  "preMatchMs": 3000,
  "turnMs": 8000,
  "missedTurnsForBot": 2,
  "disconnectBotMs": 15000,
  "botThinkMs": 0,
  "noHumansCloseMs": 10000,
  "postGameCloseMs": 30000
}
```

- Defaults live in Go, so an empty database works.
- Read when a match is **created**: edits apply to new matches, never to one in
  progress.
- Missing or invalid values fall back to the default and are logged.
- An RPC restores the defaults.
- Rules constants (7-card hands, 4 seats, 108 cards) stay in code.

## 9. Staying in sync

- Every `EVENTS` message carries `seq`, one higher than the last; snapshots
  carry the current `seq`.
- Client rules: `seq == last+1` → animate; `seq <= last` → ignore (duplicate);
  `seq > last+1` → send `REQUEST_STATE` and redraw from the snapshot.
- **RPC `current_match {}` → `{ "matchId": "…" }` or empty.** Lets a relaunched
  client find the match it still holds a human seat in. Empty means home screen.

## 10. End of the match

```json
{ "seq": 57, "events": [
  { "type": "CARD_PLAYED", "seat": 2, "card": { "id": 5, "defId": "RED_3" }, "color": "RED" },
  { "type": "PLAYER_FINISHED", "seat": 2, "place": 2 },
  { "type": "PLAYER_FINISHED", "seat": 1, "place": 3 },
  { "type": "GAME_OVER", "ranking": [0, 2, 1, 3] }
] }
```

- A player who empties their hand takes the next place and the others play on;
  their last card's effect still applies.
- The last player left takes last place and the match ends.
- The match stays open for `postGameCloseMs` (30 s) for the results screen, then
  the server closes it. No rematch in v1.

## 11. Not in v1

Private rooms and room codes, matchmaking by skill, rematch, spectating,
reconnecting after a bot takeover, chat and emotes, stats and leaderboards.
