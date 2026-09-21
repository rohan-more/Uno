package game

import (
	"errors"
	"fmt"
	"math/rand/v2"
)

const (
	MinPlayers = 2
	MaxPlayers = 4
	HandSize   = 7

	// NoSeat marks "nobody" in seat fields such as Winner and UnoTarget.
	NoSeat = -1
)

// ErrPlayerCount is a "sentinel error": a fixed error value callers can test
// for with errors.Is(err, ErrPlayerCount), even after it has been wrapped with
// fmt.Errorf("...: %w", ErrPlayerCount).
var ErrPlayerCount = errors.New("player count out of range")

// Player is one seat at the table. Seats are indexes into GameState.Players;
// the rules never see Nakama user IDs except as this label.
type Player struct {
	ID   string
	Hand []Card

	// CalledUno is true once the player has called UNO for their current
	// one-card hand (or pre-called it on their turn with two cards). Drawing
	// any card clears it.
	CalledUno bool
}

// GameState is everything needed to continue a game, and nothing that can be
// worked out from something else (e.g. the top card's type comes from Discard).
type GameState struct {
	cat  *Catalog
	rng  *rand.Rand
	Seed uint64 // log this: the same seed + same actions replays the game exactly

	Players   []Player
	Deck      *Deck
	Discard   []Card // top = last element
	Current   int    // seat whose turn it is
	Direction int    // +1 or -1

	ActiveColor Color // the color to match; differs from the top card after a wild
	PendingDraw int   // cards owed by the next player who can't stack

	// DrawnCard is set when the current player drew a playable card this turn.
	// Their only legal moves are then to play that card or Pass.
	DrawnCard *Card

	// UnoTarget is the seat that went down to one card without calling UNO.
	// Any other player may catch them until the next Play/Draw/Pass.
	UnoTarget int

	Winner int // NoSeat until someone empties their hand
}

// NewGame shuffles, deals HandSize cards to each player and turns over a
// Number card to start the discard pile. Seat 0 plays first.
func NewGame(cat *Catalog, playerIDs []string, seed uint64) (*GameState, error) {
	if n := len(playerIDs); n < MinPlayers || n > MaxPlayers {
		return nil, fmt.Errorf("%w: got %d, want %d-%d", ErrPlayerCount, n, MinPlayers, MaxPlayers)
	}

	s := &GameState{
		cat:       cat,
		rng:       rand.New(rand.NewPCG(seed, 0)),
		Seed:      seed,
		Deck:      NewDeck(cat),
		Current:   0,
		Direction: 1,
		UnoTarget: NoSeat,
		Winner:    NoSeat,
	}
	s.Deck.Shuffle(s.rng)

	s.Players = make([]Player, len(playerIDs))
	for i, id := range playerIDs {
		s.Players[i] = Player{ID: id}
	}

	for seat := range s.Players {
		s.drawCards(seat, HandSize)
	}

	if err := s.pickStartCard(); err != nil {
		return nil, err
	}
	return s, nil
}

// pickStartCard draws until it finds a Number card, puts that on the discard
// pile, sets ActiveColor, and returns the skipped cards to the deck. Skipped
// cards go back only after the pick, so one can't be turned over twice.
func (s *GameState) pickStartCard() error {
	var skipped []Card
	for {
		card, ok := s.Deck.Draw()
		if !ok {
			return errors.New("pick start card: deck has no number card")
		}
		def, _ := s.cat.Def(card)
		if def.Type == Number {
			s.Discard = append(s.Discard, card)
			s.ActiveColor = def.Color
			break
		}
		skipped = append(skipped, card)
	}
	if len(skipped) > 0 {
		s.Deck.Refill(skipped, s.rng)
	}
	return nil
}

// Over reports whether someone has won.
func (s *GameState) Over() bool {
	return s.Winner != NoSeat
}

// TopCard returns the card on top of the discard pile.
func (s *GameState) TopCard() Card {
	return s.Discard[len(s.Discard)-1]
}

// TopDef returns the definition of the top card. The catalog was validated when
// it loaded and every Card came from it, so the lookup can't miss.
func (s *GameState) TopDef() CardDef {
	def, _ := s.cat.Def(s.TopCard())
	return def
}

// nextSeat returns the seat `step` places away from `from` in the current
// direction, wrapping around the table. step 1 = next player, step 2 = skip one.
func (s *GameState) nextSeat(from, step int) int {
	n := len(s.Players)
	// % keeps the sign of the left side in Go (-1 % 4 == -1), so add n back.
	return ((from+s.Direction*step)%n + n) % n
}

// drawCards moves up to n cards from the deck into a player's hand and returns
// the cards drawn. If the deck runs out, it refills it from the discard pile
// (everything except the top card). If both are exhausted it returns fewer
// than n cards; that's legal, not an error.
func (s *GameState) drawCards(seat, n int) []Card {
	var drawn []Card
	for i := 0; i < n; i++ {
		if s.Deck.Len() == 0 {
			s.refillFromDiscard()
		}
		card, ok := s.Deck.Draw()
		if !ok {
			break
		}
		drawn = append(drawn, card)
	}

	if len(drawn) > 0 {
		// Index into s.Players: `p := s.Players[seat]` would be a copy, and
		// changes to p.Hand wouldn't reach the real player.
		s.Players[seat].Hand = append(s.Players[seat].Hand, drawn...)
		s.Players[seat].CalledUno = false
	}
	return drawn
}

// refillFromDiscard moves every discard card except the top one back into the
// deck and reshuffles. Does nothing if the discard pile has one card or fewer.
func (s *GameState) refillFromDiscard() {
	if len(s.Discard) <= 1 {
		return
	}
	last := len(s.Discard) - 1
	s.Deck.Refill(s.Discard[:last], s.rng) // Refill copies the cards in
	s.Discard = []Card{s.Discard[last]}
}
