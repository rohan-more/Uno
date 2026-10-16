package game

import (
	"errors"
	"testing"
)

func TestPenalty(t *testing.T) {
	cat := loadTestCatalog(t)
	tests := map[string]int{
		"RED_0": 0, "BLUE_7": 7, "GREEN_9": 9,
		"RED_SKIP": 20, "YELLOW_REVERSE": 20, "BLUE_DRAW_TWO": 20,
		"WILD": 50, "WILD_DRAW_FOUR": 50,
	}
	for defID, want := range tests {
		def, ok := cat.Def(Card{DefID: defID})
		if !ok {
			t.Fatalf("unknown card %s", defID)
		}
		if got := def.Penalty(); got != want {
			t.Errorf("%s penalty = %d, want %d", defID, got, want)
		}
	}
}

// places returns each seat's place, by seat.
func places(s *GameState) []int {
	out := make([]int, len(s.Players))
	for seat, p := range s.Players {
		out[seat] = p.Place
	}
	return out
}

func equalInts(a, b []int) bool {
	if len(a) != len(b) {
		return false
	}
	for i := range a {
		if a[i] != b[i] {
			return false
		}
	}
	return true
}

func TestEnd_LosersRankedByCardsLeft(t *testing.T) {
	s := rig(t, table{top: "RED_5", hands: [][]string{
		{"RED_1"},                      // plays it and wins
		{"BLUE_1", "GREEN_1", "RED_0"}, // 3 cards, only 2 points: still last
		{"BLUE_9", "GREEN_9"},          // 2 cards
		{"WILD"},                       // 1 card, 50 points: still second
	}})

	play(t, s, 0, "RED_1", "")

	if got, want := s.Ranking, []int{0, 3, 2, 1}; !equalInts(got, want) {
		t.Errorf("ranking = %v, want %v (fewest cards first, whatever they're worth)", got, want)
	}
	r := s.Results
	if r[1].Penalty != 50 || r[1].CardsLeft != 1 || r[3].Penalty != 2 || len(r[3].Hand) != 3 {
		t.Errorf("results = %+v", r)
	}
}

func TestEnd_EqualCardsSplitByPenalty(t *testing.T) {
	s := rig(t, table{top: "RED_5", hands: [][]string{
		{"RED_1"},
		{"WILD", "BLUE_2"},    // 2 cards, 52
		{"BLUE_3", "GREEN_4"}, // 2 cards, 7
		{"RED_SKIP", "RED_9"}, // 2 cards, 29
	}})

	play(t, s, 0, "RED_1", "")

	if got, want := places(s), []int{1, 4, 2, 3}; !equalInts(got, want) {
		t.Errorf("places = %v, want %v (the hidden penalty breaks the tie)", got, want)
	}
}

func TestEnd_TiesShareAPlace(t *testing.T) {
	t.Run("equal cards and points share third", func(t *testing.T) {
		s := rig(t, table{top: "RED_5", hands: [][]string{
			{"RED_1"},
			{"BLUE_4", "GREEN_3"}, // 2 cards, 7
			{"BLUE_3", "GREEN_4"}, // 2 cards, 7
			{"WILD"},              // 1 card
		}})
		play(t, s, 0, "RED_1", "")
		if got, want := places(s), []int{1, 3, 3, 2}; !equalInts(got, want) {
			t.Errorf("places = %v, want %v", got, want)
		}
	})
	t.Run("equal cards and points share second", func(t *testing.T) {
		s := rig(t, table{top: "RED_5", hands: [][]string{
			{"RED_1"},
			{"BLUE_3", "GREEN_4"}, // 2 cards
			{"BLUE_7"},            // 1 card, 7
			{"GREEN_7"},           // 1 card, 7
		}})
		play(t, s, 0, "RED_1", "")
		if got, want := places(s), []int{1, 4, 2, 2}; !equalInts(got, want) {
			t.Errorf("places = %v, want %v", got, want)
		}
	})
	t.Run("the winner never shares", func(t *testing.T) {
		s := rig(t, table{top: "RED_5", hands: [][]string{
			{"RED_1"},
			{}, {"BLUE_2"}, {"GREEN_2"},
		}})
		// Seat 1 holds nothing, like the winner, but didn't go out first.
		play(t, s, 0, "RED_1", "")
		if got, want := places(s), []int{1, 2, 3, 3}; !equalInts(got, want) {
			t.Errorf("places = %v, want %v", got, want)
		}
	})
}

func TestDebugWin_SameEndAsARealWin(t *testing.T) {
	s := rig(t, table{top: "RED_5", hands: [][]string{
		{"RED_1", "RED_2"},
		{"WILD", "BLUE_9", "RED_SKIP"}, // the rigged winner, holding a lot
		{"BLUE_3"},                     // 1 card, 3 points
		{"GREEN_8"},                    // 1 card, 8 points
	}})
	top := s.TopCard()

	ev, err := s.DebugWin(1)
	if err != nil {
		t.Fatal(err)
	}

	if !s.Over() || len(ev) != 1 || ev[0].Type != EvGameOver || ev[0].Seat != 1 {
		t.Fatalf("events = %+v", ev)
	}
	// Seats 2 and 3 hold one card each (2 has fewer points), seat 0 holds two.
	if got, want := s.Ranking, []int{1, 2, 3, 0}; !equalInts(got, want) {
		t.Errorf("ranking = %v, want %v (winner, then by the real hands)", got, want)
	}
	if w := s.Results[0]; w.CardsLeft != 0 || w.Penalty != 0 {
		t.Errorf("the winner should show an empty hand: %+v", w)
	}
	if s.TopCard() != top || len(s.Discard) != 4 {
		t.Errorf("the winner's cards should go under the top card: %v", s.Discard)
	}
	if _, err := s.DebugWin(2); !errors.Is(err, ErrGameOver) {
		t.Errorf("a second win should be refused, got %v", err)
	}
}

func TestDebugOneCard_LeavesASingleWild(t *testing.T) {
	for seat := 0; seat < MaxPlayers; seat++ {
		s := newTestGame(t, MaxPlayers, uint64(7+seat))
		counts := make([]int, MaxPlayers)
		for i, p := range s.Players {
			counts[i] = len(p.Hand)
		}

		if err := s.DebugOneCard(seat); err != nil {
			t.Fatal(err)
		}

		hand := s.Players[seat].Hand
		def, _ := s.cat.Def(hand[0])
		if len(hand) != 1 || def.Type != WildCard {
			t.Errorf("seat %d hand = %v, want one WILD", seat, hand)
		}
		for i, p := range s.Players {
			if i != seat && len(p.Hand) != counts[i] {
				t.Errorf("seat %d's hand changed size: %d -> %d", i, counts[i], len(p.Hand))
			}
		}
		requireEveryCardOnce(t, allCards(s))
	}
}

func TestDebugOneCard_SwapsWhenEveryWildIsHeld(t *testing.T) {
	s := rig(t, table{top: "RED_5", hands: [][]string{
		{"RED_1", "RED_2"},
		{"WILD", "BLUE_9"},
	}})

	if err := s.DebugOneCard(0); err != nil {
		t.Fatal(err)
	}
	if len(s.Players[0].Hand) != 1 || s.Players[0].Hand[0].DefID != "WILD" {
		t.Errorf("seat 0 hand = %v, want the WILD", s.Players[0].Hand)
	}
	if len(s.Players[1].Hand) != 2 || s.Players[1].Hand[0].DefID == "WILD" {
		t.Errorf("seat 1 should get an old card for its WILD: %v", s.Players[1].Hand)
	}
	if s.Deck.Len() != 1 {
		t.Errorf("deck = %d, want seat 0's other old card", s.Deck.Len())
	}
}
