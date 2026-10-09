# Coins, stakes and game over: plan

Status: **phase 1 built, being tested.** Built on branch `Rohan/GameOverRewards`, one
phase at a time. Every number here is a server setting, tunable from the Nakama
console like the turn timer.

## Goals

- Every match is played for a small, fixed stake, and the better you place the
  more of the pot you take home.
- New and broke players can always get back to the table.
- Nobody can tell a bot from a person, including when a bot takes over a seat.
- No coin is ever lost or paid twice: not by a disconnect, a crash or a restart.

## Rules

### How a game ends

The first player to empty their hand wins, and the game ends at once. Everyone
else is ranked by the **penalty value** of the cards still in their hand:

| Card | Penalty |
|---|---|
| Number | Its face value (0-9) |
| Skip, Reverse, Draw Two | 20 |
| Wild, Wild Draw Four | 50 |

Fewer points ranks higher, then fewer cards. Players equal on both **share the
place** (shown as "=2").

### Coins

| Setting | Value |
|---|---|
| Starting coins, once per new account | 500 |
| Stake per seat, fixed when the lobby is created | 50 |
| Pot (4 seats) | 200 |
| Payout by place | 60% / 30% / 10% / 0% |
| Free coins | 100, claimable every 4 hours |
| House cut | none for now |

- **Ties** pool the shares of the places they cover and split them evenly.
  Coins that don't divide evenly go to the house.
- **Bots pay in like players**, funded by the house.
- **Only humans are paid.** A bot seat's place is shown with the coins it
  *would* have won, but nothing is paid out for it; the house keeps that share.
  This includes a seat a bot took over from a player.
- **Quick matches** (`quick_match`, for testing) cost nothing and pay nothing.

### Losing your seat forfeits your stake

The server cannot tell a player who lost their connection from one who quit
because they were losing; killing the app looks exactly like a dropped network.
So the rule doesn't try:

> **If a bot ends up in your seat, for any reason, you forfeit your stake.**

A bot takes the seat after either:

- **3 missed turns in a row**, whatever the reason: away from the keyboard,
  disconnected, or the app closed. One rule replaces today's two (2 missed
  turns, or 15 s disconnected). A disconnected player's turns time out with
  the safe move anyway, so the grace window is measured in their own turns:
  a slow table gives you longer to come back than a fast one.
- pressing **Leave** in the match, which hands the seat over immediately.

Come back before your third missed turn and you carry on with no penalty. Once
a bot has the seat, its result belongs to the house, like any bot seat.

While you're away the others wait out your turns, up to 8 s each, so about
24 s in all before the bot takes over. That's the price of not punishing a
brief drop.

### Bots are disguised

Players never learn that a seat is a bot:

- Lobby bots have generated names and avatars (as today).
- A bot that takes over a seat **keeps the player's name and avatar**.
- The server stops sending what kind each seat is: no `kind: "bot"` in
  `LOBBY_STATE` or `GAME_STATE`, and no `SEAT_CONTROL` broadcast. The player who
  lost the seat still gets `REMOVED`, so their own client knows why.
- A takeover broadcasts `PLAYER_CONNECTION connected:true`, so a seat that was
  greyed out while its player was away looks back to normal once the bot plays it.
- Bots get the same turn ring as everyone else (already true).
- Later: vary the bot's thinking time a little, so it doesn't always move
  after exactly 1.2 s.

## How coins move

All coins live in **Nakama's built-in wallet**: per account, updated atomically,
only changeable on the server, with a ledger of every change. Each ledger entry
is tagged `{ matchId, kind }`, where `kind` is `grant`, `stake`, `payout`,
`refund`, `free` or `debug`.

| When | What happens |
|---|---|
| Account created | Grant the starting coins, once (the after-login hook already runs on creation). |
| Joining a lobby | Nothing is charged. `find_match` and the join check refuse anyone below the stake with `NOT_ENOUGH_COINS`. |
| Leaving the lobby | Free; a bot takes the seat (as today). |
| **Match starts** | Charge every seated human in **one** wallet update, and save an **escrow record**: match id, who paid, stake, status `held`. Anyone who can no longer pay is swapped for a bot and told why. |
| **Game over** | Rank the seats, pay the human seats in one wallet update, mark the escrow `settled`, send the results. |
| Match ends **without** a game over | Refund every seat a human still holds (away within the grace window, never replaced), mark the escrow `refunded`. Forfeited seats stay forfeited. |

The escrow status is checked before every step, so a retry can never charge or
pay twice.

### Escrow record

Stored by the server in its own collection, one per staked match:

```json
{
  "matchId": "…",
  "stake": 50,
  "status": "held",          // held → settled | refunded
  "seats": [
    { "seat": 0, "userId": "…", "paid": 50, "forfeited": false }
  ],
  "createdAt": "2026-10-09T12:00:00Z"
}
```

## Edge cases

| Case | Outcome |
|---|---|
| Leave during the lobby countdown | Free; a bot takes the seat. |
| Press Leave mid-game | Forfeit. The bot takes the seat at once, under your name. |
| Disconnect mid-game | Seat held; your turns time out with the safe move (draw, then pass). Back before your third missed turn: carry on, no penalty. Otherwise: forfeit. |
| Miss 3 turns in a row while connected | Forfeit, with `REMOVED` telling you why. |
| The server's network drops everyone at once | Nobody chose to leave, so the match closes and everyone is refunded. (The match closes after 10 s with no humans connected, which is before anyone could miss 3 turns; that stays, and becomes a refund.) |
| Only human in a bot game leaves | Forfeit; the match closes. |
| Disconnect after game over | Nothing lost; the payout landed at game over. Your balance shows it when you're back. |
| **Server restart or crash mid-match** | Matches exist only in the server's memory and are gone, but the escrow is saved. A cleanup on server start, and again on the player's next login, refunds every `held` escrow whose match no longer exists. |
| A wallet update fails at game over | The escrow stays `held` with the results saved; the same cleanup pays them later. |
| Trying to start a second match while seated in one | Refused; the client rejoins the one in progress via `current_match`. |
| A bot wins | The bot's share goes to the house; humans still get their places' shares. |
| Stake setting changed while lobbies are open | Each lobby keeps the stake it was created with. |

## Free coins

- `claim_free_coins` grants 100 if 4 hours have passed since your last claim
  (server clock), and returns your new balance and when you can claim next.
- The home screen shows a claim button, or a countdown until the next claim.

## Debug tools

For testing from inside the game, behind an **F1** menu. Every debug RPC is
refused unless the server setting `debugTools` is on, which it is only locally.

| RPC | Does |
|---|---|
| `debug_grant_coins` | Adds an amount to your wallet (ledger kind `debug`) |
| `debug_reset_free_coins` | Makes free coins claimable right now |

The menu is meant to grow: later, items or anything else worth granting.

## Protocol changes

| Change | Detail |
|---|---|
| New client op `LEAVE_MATCH` | Leave now: a bot takes the seat at once and you forfeit. |
| Drops keep your seat record | A plain disconnect no longer clears `current_match`, so the client can find and rejoin the match. |
| One takeover rule | `missedTurnsForBot` becomes 3 and applies to disconnected players too; `disconnectBotMs` is removed. |
| `GAME_OVER` gains `results` | `[{ seat, place, cardsLeft, penalty, coins }]`, plus `stake` and `pot`. Bot seats carry the coins they would have won. |
| `REMOVED` reasons | Add `LEFT` and `NOT_ENOUGH_COINS`. |
| Error code | `NOT_ENOUGH_COINS` from `find_match` and joining. |
| Seat kind hidden | `kind` dropped from `LOBBY_STATE` and `GAME_STATE` seats; `SEAT_CONTROL` no longer broadcast. |
| New RPCs | `claim_free_coins`, `debug_grant_coins`, `debug_reset_free_coins`. |
| Lobby label | Includes the stake, so tables can be grouped by stake later. |
| Balance | Read from the Nakama account wallet, which the client already loads. |

## Client

- **Home:** coin balance, Play disabled below the stake, free-coins button or
  countdown.
- **Match:** a Leave button that warns you'll lose your stake.
- **Reconnecting:** after a dropped socket the client reconnects and rejoins
  the match it was in; at startup it asks `current_match` and goes straight
  back to a match in progress.
- **Debug menu (F1):** grant coins, reset the free-coins timer.
- **Results** (last phase), laid out like Card Party's: place (with "=2" for
  ties), avatar, name, cards left, coins won, then back home.

## Phases

Each phase ships on its own, with server tests and `PROTOCOL.md` updated.

1. **Seats: leave, reconnect, disguise.** `LEAVE_MATCH`; the 3-missed-turns
   rule; reconnecting for real (see below); hiding seat kinds; a Leave button.

   Reconnecting doesn't work today, for three reasons this phase fixes:
   the server clears `current_match` on any drop; the client forgets the match
   when its socket closes and only offers "Restart to reconnect"; and nothing
   calls `current_match` at startup, so a restarted client never goes back.
2. **Wallet.** Starting coins, balance on the home screen, free coins every 4
   hours, the debug RPCs and the F1 menu.
3. **Stakes.** The new game end and penalty ranking, charging at match start,
   paying at game over, refunds, the escrow record and its cleanup.
4. **Results screen.** The Card Party-style leaderboard, then back home.
5. **Later.** Stake tiers, XP and levels, a "Next game" countdown for a rematch,
   bot thinking-time variation.

## Open questions

- **Free coins:** 100 every 4 hours is a starting point; watch whether players
  run dry.
