# Uno in Unity/Nakama: playing for coins

*Stakes, payouts, and making losing cost something*

My online Uno runs four players on a Unity client and a Nakama server written in
Go. The server owns the game: it deals, checks every move and decides the
winner. Winning didn't mean anything, though. Every match ended the same way,
with a list of names. So I added coins.

## A stake and a pot

Every match from matchmaking now costs 50 coins a seat. Bots pay in too, from
the house, so the pot is always 200. When the game ends it's paid out by place:
60%, 30%, 10%, nothing for last. Only humans are paid; whatever a bot wins stays
with the house. Players who share a place pool the shares of the places they
cover and split them. Every number lives in a config object I can edit in the
Nakama console without a rebuild, and each lobby keeps the stake it was created
with, so changing it never touches a game in progress.

New accounts start with 500 coins, and the home screen counts your balance up
and down when it changes.

## Losing has to cost something

Free coins took three tries.

The first version gave anyone 100 coins every four hours. Then I wanted it
automatic and capped, and suggested topping everyone up to 500. Talking it
through showed the problem: lose a few games, drop to 400, and the free coins
quietly put you back. Losing would only cost time, so there'd be no reason to
try to win.

The version that shipped is a rescue, not a refill. Free coins only start when
you can't afford a match, below 50. Four hours later the server tops you up to
exactly 100, enough for two matches, and it happens by itself the next time
your wallet is read. The timer only runs while you're broke, so a rich player
never saves up free time for later. Anything above 100 is only ever won.

## A ranking you can see

The first player to empty their hand wins, and the game ends there. Everyone
else needs a place, because places are worth coins.

Uno's traditional scoring counts the cards left in your hand: number cards at
face value, action cards 20, wilds 50. I ranked by those points first, until I
pictured the results screen. It only shows places and coins, never points, so
a player holding one Wild (50 points) would come last behind someone holding
three low cards. On screen that looks like a bug.

So the visible rule is now just "fewer cards ranks higher", and the points only
break ties between equal card counts, where a hand of wilds is the worse one.
Equal on both and you share the place, shown as "=2".

## Rigging the ending

Testing payouts by playing whole games was slow, so the debug menu (F1, only on
a local server) can now make any player win on the spot, or leave them holding a
single Wild so they win for real on their next turn. Both go through the same
game-end code as a real win, so the ranking and payouts they show are the
genuine ones, not a debug shortcut that drifts out of step.

## What's next

The results screen is in: four rows with a gold, silver, bronze or slate ring
around each avatar, and the coins counting up. Next up: a rematch button and
more than one stake level. Code and notes:
[rohan-more/Uno](https://github.com/rohan-more/Uno).
