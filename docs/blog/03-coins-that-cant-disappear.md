# Uno in Unity/Nakama: coins that can't disappear

*Escrow, leavers, and what happens when the server dies*

My online Uno is four players on a Unity client and a Nakama server written in
Go, and every match is played for coins: each seat stakes 50, and the pot is
paid out by place. Once there's money on the table, every way a match can go
wrong becomes a question about money. A player quits because they're losing.
Someone's Wi-Fi drops. The server restarts with four stakes in the middle of a
game. The goal I wrote down before any code: no coin is ever lost or paid
twice.

## Leaving and dropping look the same

The server can't tell a player who lost their connection from one who closed the
app because they were losing. So the rule doesn't try:

> If a bot ends up in your seat, for any reason, you lose your stake.

A bot takes the seat after three missed turns in a row, whatever the reason, or
at once if you press Leave. Drop out and come back before that and you carry on
as if nothing happened: the server keeps your seat, and the client reconnects
and rejoins by itself. Even after a full restart, the home screen offers to
rejoin the match you're still in.

The bot that takes over plays under your name and avatar, and the other players
aren't told. The server never reveals which seats are bots at all, lobby bots
included, so nobody can tell a bot from a person.

## One record, one transaction

When a match starts, every human's stake is taken from their wallet. The
dangerous moment is between taking the coins and writing down that you took
them: crash there and the coins are simply gone.

Nakama has a call, `MultiUpdate`, that changes wallets and stored records in a
single database transaction. So each staked match gets one escrow record, and
it only ever changes alongside the coins:

- created in the same transaction that takes the stakes,
- updated when a player forfeits,
- deleted in the same transaction that pays out, or refunds.

That means the record exists exactly while coins are held. There's no status
field to keep in sync and no step that can half-happen: either the coins moved
and the record changed, or neither did.

## When things go wrong

- **Everyone drops** and the match closes early: everyone still in their seat
  gets their stake back. Forfeits stay forfeited.
- **The payout fails** at game over: it's retried every tick, and once more as
  the match closes, so it never turns into a refund by mistake.
- **The server dies mid-match**: matches live only in memory, but escrow records
  are in the database. On startup the server refunds every record it finds,
  because any match they belonged to died with the old server.

These paths are hard to reach by playing, so they're tested against a fake bank
that behaves like Nakama: all or nothing, with version checks on every record.
The server now has 126 tests, from the shuffle up to a payout that fails once and
is then retried.

## What's next

A rematch button, more than one stake level, a bot that doesn't just play the
first legal card, and a deployment so friends can play. Code and notes:
[rohan-more/Uno](https://github.com/rohan-more/Uno).
