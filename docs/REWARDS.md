# Coins, stakes and game over: plan

Status: **phases 1-4 done, merged to `main`.** Only the "Later" list is left. Built on branch
`Rohan/GameOverRewards`, one phase at a time. Every number here is a server
setting, tunable from the Nakama console like the turn timer.

## Goals

- Every match is played for a small, fixed stake, and the better you place the
  more of the pot you take home.
- New and broke players can always get back to the table.
- Nobody can tell a bot from a person, including when a bot takes over a seat.
- No coin is ever lost or paid twice: not by a disconnect, a crash or a restart.

## Rules

### How a game ends

The first player to empty their hand wins, and the game ends at once. Everyone
else is ranked by **how many cards they have left**, fewest first. Equal counts
are split by the **penalty value** of those cards, lower first:

| Card | Penalty |
|---|---|
| Number | Its face value (0-9) |
| Skip, Reverse, Draw Two | 20 |
| Wild, Wild Draw Four | 50 |

Players equal on both **share the place** (shown as "=2"), which is rare.

The results screen shows only places and coins (and cards left), never points.
So the rule players can see is simply "fewer cards ranks higher"; the penalty
quietly makes a hand full of wilds the worse of two equal ones. An earlier
version ranked by points first, and a player holding one Wild could come last
behind someone holding three low cards, which looked like a bug.

### Coins

| Setting | Value |
|---|---|
| Starting coins, once per new account | 500 |
| Stake per seat, fixed when the lobby is created | 50 |
| Pot (4 seats) | 200 |
| Payout by place | 60% / 30% / 10% / 0% |
| Free coins | Below 50, topped up to 100 after 4 hours |
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
| **Match starts** | Take every seated human's stake and create the **escrow record**, in **one** transaction. Anyone who can no longer pay is swapped for a bot and told why. |
| A bot takes a seat | The record marks that seat forfeited. |
| **Game over** | Rank the seats, send the results with each place's coins, then pay the humans still in their seats and delete the record, in one transaction. A failed payout is retried every tick, and again when the match closes. |
| Match ends **without** a game over | Refund every seat a human still holds (away within the grace window, never replaced) and delete the record, in one transaction. Forfeited seats stay forfeited. |

### Escrow record

Stored by the server in collection `escrow`, key the match id, one per staked
match:

```json
{
  "matchId": "…",
  "stake": 50,
  "seats": [
    { "seat": 0, "userId": "…", "paid": 50, "forfeited": false }
  ],
  "createdAtMs": 1791982283716
}
```

There's no status field: the record **exists exactly while stakes are held**.
Nakama's `MultiUpdate` changes wallets and storage in a single transaction,
so the record is created with the stakes taken and deleted with the payout or
refund. A crash can't land between the two, so nothing can be charged or paid
twice, and nothing is lost.

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
| **Server restart or crash mid-match** | Matches exist only in the server's memory and are gone, but the escrow record is saved. On server start, every record left over is refunded. (A cleanup at login isn't needed: a match only vanishes without closing when the server itself goes.) |
| A wallet update fails at game over | The record stays; the payout is retried every tick and once more as the match closes. If the server dies first, the restart refunds the stakes instead. |
| Trying to start a second match while seated in one | Refused; the client rejoins the one in progress via `current_match`. |
| A bot wins | The bot's share goes to the house; humans still get their places' shares. |
| Stake setting changed while lobbies are open | Each lobby keeps the stake it was created with. |

## Free coins

Free coins get broke players back into the game; they never refill what you
lost. A version that topped everyone up to 500 made losing cost nothing but
waiting, so there was no reason to try to win.

- **Only when you can't play.** Once your balance is below **50** (one stake),
  a 4 hour wait starts. When it's over you're topped up to exactly **100**: two
  matches, no more.
- **Automatic.** Nothing to claim: the server adds them the next time the wallet
  is read (login, the home screen, a reconnect). No scheduled job; it compares
  the time the wait began with now.
- **Nothing is saved up.** Getting back to 50 or more cancels the wait, so a
  rich player who loses a few matches gets nothing, and a long absence still
  only tops up to 100.
- **Not during a match.** While you're seated in one, your stake is held and the
  balance looks low, so nothing changes until you're home.
- Anything above 100 is only ever won.

| Setting | Value |
|---|---|
| `freeCoinsBelow` | 50 |
| `freeCoinsUpTo` | 100 |
| `freeCoinsEveryMinutes` | 240 |

The home screen announces a top-up ("+70 free coins") as the balance counts up,
and shows how long until the next one while you're waiting.

## Debug tools

For testing from inside the game, behind an **F1** menu. Every debug RPC is
refused unless the server setting `debugTools` is on, which it is only locally.

| RPC | Does |
|---|---|
| `debug_grant_coins` | Adds an amount to your wallet (ledger kind `debug`); a negative amount takes coins away, to go broke |
| `debug_free_coins_now` | If you're below the line, tops up now instead of in 4 hours |
| `DEBUG_WIN` (phase 3) | Match op `{ seat }`: that seat wins now. See below |
| `DEBUG_ONE_CARD` (phase 3) | Match op `{ seat }`: that seat's hand becomes a single Wild, so it wins on its next turn. See below |

### Rigging a game over

Both match ops are refused unless the debug tools are on, and only during
play. They pick a seat by its **server seat number**, so any player can be made
to win, bot or human.

- **Instant win** (`DEBUG_WIN`) empties the seat's hand, as if it had just
  played its last card, then runs the **same game-end code** a real final play
  does. The winner shows 0 cards; everyone else keeps their real hand, so the
  ranking, ties, payout, escrow, ledger and results are exactly what a real
  game would produce. There are no separate debug rules to drift out of step.
- **Down to one card** (`DEBUG_ONE_CARD`) swaps the seat's hand for one Wild,
  which is always playable, and play carries on. On that seat's next turn the
  player (or bot) plays it for real. This covers what an instant win skips:
  the last card's play and animation, the game ending mid-turn, and a bot
  winning on its own.

Specific hands, such as two losers tied on points to check the pot split, are
tested in the Go unit tests, where every hand can be set exactly.

**F1 menu:** a dropdown of the players in table order ("You", then clockwise),
filled from the match state, each option holding that player's server seat;
then **Instant win** and **Down to one card** buttons. Both are disabled
outside a match.

The menu is meant to grow: later, items or anything else worth granting.

## Protocol changes

| Change | Detail |
|---|---|
| New client op `LEAVE_MATCH` | Leave now: a bot takes the seat at once and you forfeit. |
| Debug ops `DEBUG_WIN`, `DEBUG_ONE_CARD` | `{ seat }`; refused unless the debug tools are on. |
| Drops keep your seat record | A plain disconnect no longer clears `current_match`, so the client can find and rejoin the match. |
| One takeover rule | `missedTurnsForBot` becomes 3 and applies to disconnected players too; `disconnectBotMs` is removed. |
| `GAME_OVER` gains `results` | `[{ seat, place, cardsLeft, penalty, coins }]`, plus `stake` and `pot`. Bot seats carry the coins they would have won. |
| `REMOVED` reasons | Add `LEFT` and `NOT_ENOUGH_COINS`. |
| Error code | `NOT_ENOUGH_COINS` from `find_match` and joining. |
| Seat kind hidden | `kind` dropped from `LOBBY_STATE` and `GAME_STATE` seats; `SEAT_CONTROL` no longer broadcast. |
| New RPCs | `debug_grant_coins`, `debug_free_coins_now`; `get_wallet` tops up free coins. |
| Lobby label | Includes the stake, so tables can be grouped by stake later. |
| Balance | Read from the Nakama account wallet, which the client already loads. |

## Client

- **Home:** coin balance (counting up and down), Play disabled below the stake,
  "+N free coins" when topped up, and the wait while you're below the line.
- **Match:** a Leave button that warns you'll lose your stake.
- **Reconnecting:** after a dropped socket the client reconnects and rejoins
  the match it was in; at startup it asks `current_match` and goes straight
  back to a match in progress.
- **Debug menu (F1):** grant or take coins, free coins now, and (phase 3) pick
  any player to win instantly or to be down to one card.
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
3. **Stakes.** The new game end and its ranking, charging at match start,
   paying at game over, refunds, the escrow record and its cleanup. First the
   new game end with the two rigging tools (`DEBUG_WIN`, `DEBUG_ONE_CARD`) and
   their F1 controls, to reach a game over in seconds while building the rest.
4. **Results screen.** The Card Party-style leaderboard, then back home.
5. **Later.** Stake tiers, XP and levels, a "Next game" countdown for a rematch,
   bot thinking-time variation.

## Open questions

- **Free coins:** below 50, up to 100, after 4 hours is a starting point; watch
  whether players run dry.
- **Starting coins:** 500 is now the most anyone has without winning (10
  matches). 300 would make losses sting sooner.
