# Match intro: plan

Status: **planned, not started.** Recorded 2026-11-11.

## Why

When a match opens, the cards are simply there and the stake is never shown.
Players don't see what they're playing for, or the cards being dealt. Two
short animations at the start fix that:

1. **The pot.** Each seat's stake (e.g. 50 coins) flies in as coins from every
   player's seat to the centre of the table, where a pot counter counts up to
   the total (e.g. 200).
2. **The deal.** Cards fly from the draw pile to every player, one at a time
   round the table, 7 each: face down to opponents, flipping face up as they
   land in your hand. Then the starting card turns over onto the discard pile.

**The order must be flexible:** pot then deal, deal then pot, or one alone. It's
a setting, not code.

## Shape

- **`MatchIntro`** in MatchScene runs an ordered list of **intro steps** set in
  the Inspector, one after another, then lets play begin. Reordering the list
  reorders the intro.
- Each step is a component with its own animation, like the special-card
  panels (`TableEventPanel`):
  - `PotIntroStep`: coins from each seat to the centre, then the pot counter
    counts up (`NumberCounter`, `CoinFormat`). The pot stays on show for the
    match, e.g. as a small "Pot 200" by the piles.
  - `DealIntroStep`: cards from the draw pile, round-robin in turn order, at a
    readable pace. Hands and card counts appear as cards land.
- The steps read the snapshot that started the match: seats, `stake`, `pot`, and
  your hand (from `GAME_STATE`). No new server data is needed.

## Things to get right

- **Timing is the server's.** The intro has to fit inside the server's
  pre-match pause (`preMatchSeconds`, 3 s today), before the first turn's clock
  starts. Today that pause is spent on the matchmaking screen
  (`MatchSceneLoader` waits `startsInMs` before loading MatchScene). Instead:
  load MatchScene as soon as the deal arrives, play the intro during the pause,
  and raise `preMatchSeconds` to cover it (about 4-5 s for both steps). The
  intro must never run past the first turn.
- **Hide what hasn't happened yet.** The snapshot already contains every hand,
  so `MatchView`, the seats and the piles must hold back their cards and counts
  until the deal shows them landing.
- **Skip it when it doesn't apply.**
  - Rejoining a match in progress: no intro, straight to the table.
  - Free matches (`stake` 0): no pot step.
- **Sound:** coin clinks as coins land (`Sfx.Coins`), card sounds as cards land
  (`Sfx.CardDrawn`).
- **Everyone sees the same intro,** but from their own seat: coins and cards fly
  from each player's position on *their* screen.

## Testing

Quick matches deal straight away, so they're a fast way to see the intro.
Staked matches (Instant Match off) are needed for the pot step.
