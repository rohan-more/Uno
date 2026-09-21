package game

import (
	"errors"
	"testing"
)

// newTestGame starts a real game with n players and a fixed seed.
func newTestGame(t *testing.T, n int, seed uint64) *GameState {
	t.Helper()
	ids := []string{"p0", "p1", "p2", "p3", "p4"}[:n]
	s, err := NewGame(loadTestCatalog(t), ids, seed)
	if err != nil {
		t.Fatalf("NewGame: %v", err)
	}
	return s
}

// allCards collects every card in the game: hands, deck and discard pile.
func allCards(s *GameState) []Card {
	var all []Card
	for _, p := range s.Players {
		all = append(all, p.Hand...)
	}
	all = append(all, s.Deck.cards...)
	all = append(all, s.Discard...)
	return all
}

func TestNewGame(t *testing.T) {
	for n := MinPlayers; n <= MaxPlayers; n++ {
		s := newTestGame(t, n, 1)

		for seat, p := range s.Players {
			if len(p.Hand) != HandSize {
				t.Errorf("%d players: seat %d has %d cards, want %d", n, seat, len(p.Hand), HandSize)
			}
		}
		if want := totalCardCount - HandSize*n - 1; s.Deck.Len() != want {
			t.Errorf("%d players: deck has %d cards, want %d", n, s.Deck.Len(), want)
		}
		if len(s.Discard) != 1 {
			t.Fatalf("%d players: discard has %d cards, want 1", n, len(s.Discard))
		}
		if top := s.TopDef(); top.Type != Number || s.ActiveColor != top.Color {
			t.Errorf("%d players: top %s, active color %s; want a Number card and its color", n, top.ID, s.ActiveColor)
		}
		if s.Current != 0 || s.Direction != 1 || s.PendingDraw != 0 || s.Over() || s.UnoTarget != NoSeat {
			t.Errorf("%d players: bad start state: current %d, direction %d, pending %d, winner %d, uno target %d",
				n, s.Current, s.Direction, s.PendingDraw, s.Winner, s.UnoTarget)
		}
		requireEveryCardOnce(t, allCards(s))
	}
}

func TestNewGame_BadPlayerCount(t *testing.T) {
	cat := loadTestCatalog(t)
	for _, ids := range [][]string{{"solo"}, {"a", "b", "c", "d", "e"}} {
		_, err := NewGame(cat, ids, 1)
		if !errors.Is(err, ErrPlayerCount) {
			t.Errorf("%d players: err = %v, want ErrPlayerCount", len(ids), err)
		}
	}
}

func TestNewGame_SameSeedSameDeal(t *testing.T) {
	a, b := newTestGame(t, 4, 99), newTestGame(t, 4, 99)
	for seat := range a.Players {
		for i := range a.Players[seat].Hand {
			if a.Players[seat].Hand[i] != b.Players[seat].Hand[i] {
				t.Fatalf("seat %d card %d differs", seat, i)
			}
		}
	}
	if a.TopCard() != b.TopCard() || !sameOrder(a.Deck, b.Deck) {
		t.Error("same seed gave a different start card or deck")
	}
}

func TestPickStartCard_SkipsSpecialCards(t *testing.T) {
	cat := loadTestCatalog(t)
	s := &GameState{cat: cat, rng: seededRNG(1)}
	// Top of the deck is the END: WILD comes off first, then SKIP, then RED_4.
	s.Deck = &Deck{cards: []Card{
		{ID: 0, DefID: "BLUE_7"},
		{ID: 1, DefID: "RED_4"},
		{ID: 2, DefID: "RED_SKIP"},
		{ID: 3, DefID: "WILD"},
	}}

	if err := s.pickStartCard(); err != nil {
		t.Fatal(err)
	}
	if got := s.TopCard(); got.ID != 1 {
		t.Errorf("start card = %+v, want RED_4 (ID 1)", got)
	}
	if s.ActiveColor != Red {
		t.Errorf("ActiveColor = %s, want RED", s.ActiveColor)
	}
	// The skipped WILD and SKIP are back in the deck with BLUE_7.
	inDeck := map[int]bool{}
	for _, c := range s.Deck.cards {
		inDeck[c.ID] = true
	}
	if len(s.Deck.cards) != 3 || !inDeck[0] || !inDeck[2] || !inDeck[3] {
		t.Errorf("deck = %+v, want IDs 0, 2 and 3", s.Deck.cards)
	}
}

func TestPickStartCard_NoNumberCard(t *testing.T) {
	s := &GameState{cat: loadTestCatalog(t), rng: seededRNG(1)}
	s.Deck = &Deck{cards: []Card{{ID: 0, DefID: "WILD"}}}
	if err := s.pickStartCard(); err == nil {
		t.Error("expected an error when the deck has no number card")
	}
}

func TestNextSeat(t *testing.T) {
	tests := []struct {
		players, direction, from, step, want int
	}{
		{4, 1, 0, 1, 1},
		{4, 1, 3, 1, 0},  // wraps forward
		{4, -1, 0, 1, 3}, // wraps backward: the negative % case
		{4, 1, 3, 2, 1},  // skip
		{4, -1, 1, 2, 3},
		{3, -1, 0, 2, 1},
		{2, 1, 0, 2, 0}, // two players: skip comes back to you
		{2, -1, 1, 1, 0},
	}
	for _, tt := range tests {
		s := &GameState{Players: make([]Player, tt.players), Direction: tt.direction}
		if got := s.nextSeat(tt.from, tt.step); got != tt.want {
			t.Errorf("%d players, dir %d: nextSeat(%d, %d) = %d, want %d",
				tt.players, tt.direction, tt.from, tt.step, got, tt.want)
		}
	}
}

func TestDrawCards_RefillsFromDiscard(t *testing.T) {
	s := newTestGame(t, 2, 5)
	top := s.TopCard()

	// Move the whole deck under the top card of the discard pile.
	var rest []Card
	for s.Deck.Len() > 0 {
		c, _ := s.Deck.Draw()
		rest = append(rest, c)
	}
	s.Discard = append(rest, top)

	drawn := s.drawCards(0, 3)

	if len(drawn) != 3 || len(s.Players[0].Hand) != HandSize+3 {
		t.Errorf("drew %d, hand %d; want 3 and %d", len(drawn), len(s.Players[0].Hand), HandSize+3)
	}
	if len(s.Discard) != 1 || s.TopCard() != top {
		t.Errorf("discard = %+v, want only the old top card", s.Discard)
	}
	requireEveryCardOnce(t, allCards(s))
}

func TestDrawCards_NothingLeft(t *testing.T) {
	s := newTestGame(t, 2, 5)
	// Put the entire deck into seat 1's hand. Discard only has its top card.
	s.Players[1].Hand = append(s.Players[1].Hand, s.Deck.cards...)
	s.Deck.cards = nil

	drawn := s.drawCards(0, 2)

	if len(drawn) != 0 || len(s.Players[0].Hand) != HandSize {
		t.Errorf("drew %d cards with nothing left, want 0", len(drawn))
	}
	requireEveryCardOnce(t, allCards(s))
}

func TestDrawCards_ClearsCalledUno(t *testing.T) {
	s := newTestGame(t, 2, 5)
	s.Players[0].CalledUno = true
	s.drawCards(0, 1)
	if s.Players[0].CalledUno {
		t.Error("drawing should clear CalledUno")
	}
}
