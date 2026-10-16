package game

import (
	"errors"
	"fmt"
	"sort"
)

// How a game ends: the first player to empty their hand wins, and the game is
// over at once. Everyone else is ranked by how many cards they have left,
// fewest first. Equal counts are split by the penalty value of those cards
// (lower first), which players never see; equal on both share the place.
// Players only see places and coins, so the visible rule is "fewer cards ranks
// higher", and the penalty quietly makes a hand full of wilds the worse one.

// Result is one seat's line on the results screen.
type Result struct {
	Seat      int
	Place     int // 1 = winner; tied seats share a place, e.g. 1, 2, 2, 4
	CardsLeft int
	Penalty   int    // sum of Penalty() over the cards left; the hidden tie-break
	Hand      []Card // the cards left, for showing on the results screen
}

// Penalty is what this card counts against you if it's still in your hand at
// the end: a number card its face value, Skip / Reverse / Draw Two 20, wilds 50.
func (d CardDef) Penalty() int {
	switch d.Type {
	case Number:
		return d.Number
	case Skip, Reverse, DrawTwo:
		return 20
	case WildCard, WildDrawFour:
		return 50
	}
	return 0
}

// ErrDebug is returned when a debug change can't be made.
var ErrDebug = errors.New("debug change not possible")

// end finishes the game with winner first and everyone else ranked by their
// hands, and returns the GAME_OVER event. Every way a game can end (a real last
// card, or DebugWin) comes through here.
func (s *GameState) end(winner int) Event {
	s.DrawnCard = nil
	s.PendingDraw = 0

	results := make([]Result, len(s.Players))
	for seat, p := range s.Players {
		penalty := 0
		for _, c := range p.Hand {
			def, _ := s.cat.Def(c)
			penalty += def.Penalty()
		}
		results[seat] = Result{Seat: seat, CardsLeft: len(p.Hand), Penalty: penalty, Hand: append([]Card(nil), p.Hand...)}
	}

	// Winner first, then fewer cards, then fewer points, then seat (only so
	// the order is stable; seat never decides a place).
	sort.SliceStable(results, func(i, j int) bool {
		a, b := results[i], results[j]
		if (a.Seat == winner) != (b.Seat == winner) {
			return a.Seat == winner
		}
		if a.CardsLeft != b.CardsLeft {
			return a.CardsLeft < b.CardsLeft
		}
		if a.Penalty != b.Penalty {
			return a.Penalty < b.Penalty
		}
		return a.Seat < b.Seat
	})

	s.Ranking = make([]int, len(results))
	for i := range results {
		r := &results[i]
		r.Place = i + 1
		if i > 1 { // the winner never shares; losers equal on both share
			prev := results[i-1]
			if prev.Penalty == r.Penalty && prev.CardsLeft == r.CardsLeft {
				r.Place = prev.Place
			}
		}
		s.Ranking[i] = r.Seat
		s.Players[r.Seat].Place = r.Place
	}
	s.Results = results

	return Event{Type: EvGameOver, Seat: winner, Ranking: append([]int(nil), s.Ranking...), Results: append([]Result(nil), results...)}
}

// DebugWin ends the game now with seat as the winner, as if it had just played
// its last card: its hand goes under the discard pile, then the normal game
// end ranks everyone else by the cards they really hold. For testing only.
func (s *GameState) DebugWin(seat int) ([]Event, error) {
	if s.Over() {
		return nil, ErrGameOver
	}
	if seat < 0 || seat >= len(s.Players) {
		return nil, fmt.Errorf("%w: %d", ErrBadSeat, seat)
	}

	// Under the top card, so the pile still shows what was last played.
	hand := s.Players[seat].Hand
	s.Players[seat].Hand = nil
	s.Discard = append(append([]Card(nil), hand...), s.Discard...)
	return []Event{s.end(seat)}, nil
}

// DebugOneCard swaps seat's hand for a single Wild, which can be played on
// anything, so it wins on its next turn (unless it owes cards from a Draw Two
// or Wild Draw Four first). The old hand is shuffled into the deck; if every
// Wild is in someone's hand, that player gets one of the old cards in its
// place, so their card count doesn't change. For testing only.
func (s *GameState) DebugOneCard(seat int) error {
	if s.Over() {
		return ErrGameOver
	}
	if seat < 0 || seat >= len(s.Players) {
		return fmt.Errorf("%w: %d", ErrBadSeat, seat)
	}

	old := s.Players[seat].Hand
	isWild := func(c Card) bool {
		def, _ := s.cat.Def(c)
		return def.Type == WildCard
	}

	var wild Card
	found := false
	take := func(cards []Card, i int) []Card {
		wild, found = cards[i], true
		return append(cards[:i:i], cards[i+1:]...)
	}

	// Already holding one, then the deck, then the discard pile below the top.
	for i, c := range old {
		if !found && isWild(c) {
			old = take(old, i)
			break
		}
	}
	for i, c := range s.Deck.cards {
		if !found && isWild(c) {
			s.Deck.cards = take(s.Deck.cards, i)
			break
		}
	}
	for i := 0; !found && i < len(s.Discard)-1; i++ {
		if isWild(s.Discard[i]) {
			s.Discard = take(s.Discard, i)
		}
	}
	// Last resort: swap with another player.
	for other := range s.Players {
		if found || len(old) == 0 {
			break
		}
		if other == seat {
			continue
		}
		for i, c := range s.Players[other].Hand {
			if isWild(c) {
				hand := append([]Card(nil), s.Players[other].Hand...)
				hand[i] = old[0] // they get one of the old cards back for it
				old = old[1:]
				s.Players[other].Hand = hand
				wild, found = c, true
				break
			}
		}
	}
	if !found {
		return fmt.Errorf("%w: no Wild card to give", ErrDebug)
	}

	s.Players[seat].Hand = []Card{wild}
	s.Deck.Refill(old, s.rng)
	if seat == s.Current {
		s.DrawnCard = nil // a drawn card it was holding has gone
	}
	return nil
}
