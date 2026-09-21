package game

import (
	"errors"
	"testing"
)

// table describes a hand-built game position for a rules test.
type table struct {
	top   string     // DefID of the discard pile's top card
	color Color      // active color; defaults to the top card's color
	hands [][]string // one DefID list per seat
	deck  []string   // draw pile; the LAST entry is drawn first
}

// rig builds a GameState from a table. Card IDs are unique but don't cover the
// full 108, so use allCards/requireEveryCardOnce only with NewGame games.
func rig(t *testing.T, tb table) *GameState {
	t.Helper()
	cat := loadTestCatalog(t)
	nextID := 0
	mk := func(defID string) Card {
		if _, ok := cat.Def(Card{DefID: defID}); !ok {
			t.Fatalf("rig: unknown card %q", defID)
		}
		nextID++
		return Card{ID: nextID, DefID: defID}
	}

	s := &GameState{
		cat:       cat,
		rng:       seededRNG(1),
		Deck:      &Deck{},
		Direction: 1,
	}
	for _, hand := range tb.hands {
		p := Player{}
		for _, id := range hand {
			p.Hand = append(p.Hand, mk(id))
		}
		s.Players = append(s.Players, p)
	}
	for _, id := range tb.deck {
		s.Deck.cards = append(s.Deck.cards, mk(id))
	}
	s.Discard = []Card{mk(tb.top)}
	s.ActiveColor = tb.color
	if s.ActiveColor == "" {
		s.ActiveColor = s.TopDef().Color
	}
	return s
}

// cardIn returns the first card in seat's hand with this DefID.
func cardIn(t *testing.T, s *GameState, seat int, defID string) Card {
	t.Helper()
	for _, c := range s.Players[seat].Hand {
		if c.DefID == defID {
			return c
		}
	}
	t.Fatalf("seat %d has no %s", seat, defID)
	return Card{}
}

// play is shorthand for applying PlayCard and failing the test on error.
func play(t *testing.T, s *GameState, seat int, defID string, color Color) []Event {
	t.Helper()
	ev, err := s.Apply(seat, Action{Type: PlayCard, CardID: cardIn(t, s, seat, defID).ID, Color: color})
	if err != nil {
		t.Fatalf("seat %d play %s: %v", seat, defID, err)
	}
	return ev
}

func do(t *testing.T, s *GameState, seat int, typ ActionType) []Event {
	t.Helper()
	ev, err := s.Apply(seat, Action{Type: typ})
	if err != nil {
		t.Fatalf("seat %d %s: %v", seat, typ, err)
	}
	return ev
}

func wantErr(t *testing.T, err, target error) {
	t.Helper()
	if !errors.Is(err, target) {
		t.Fatalf("err = %v, want %v", err, target)
	}
}

func eventTypes(evs []Event) []EventType {
	var out []EventType
	for _, e := range evs {
		out = append(out, e.Type)
	}
	return out
}

func TestCanPlay(t *testing.T) {
	tests := []struct {
		name    string
		top     string
		color   Color
		pending int
		card    string
		want    bool
	}{
		{"same color", "RED_5", "", 0, "RED_9", true},
		{"same number", "RED_5", "", 0, "BLUE_5", true},
		{"nothing matches", "RED_5", "", 0, "BLUE_7", false},
		{"symbol match", "RED_SKIP", "", 0, "BLUE_SKIP", true},
		{"number doesn't match symbol", "RED_SKIP", "", 0, "BLUE_5", false},
		{"wild always", "RED_5", "", 0, "WILD", true},
		{"wild draw four always", "RED_5", "", 0, "WILD_DRAW_FOUR", true},
		{"chosen color after wild", "WILD", Green, 0, "GREEN_2", true},
		{"old color after wild", "WILD", Green, 0, "RED_2", false},
		{"stack draw two", "RED_DRAW_TWO", "", 2, "BLUE_DRAW_TWO", true},
		{"no color match while owing", "RED_DRAW_TWO", "", 2, "RED_5", false},
		{"no wild while owing draw two", "RED_DRAW_TWO", "", 2, "WILD_DRAW_FOUR", false},
		{"stack wild draw four", "WILD_DRAW_FOUR", Blue, 4, "WILD_DRAW_FOUR", true},
		{"no draw two on draw four", "WILD_DRAW_FOUR", Blue, 4, "BLUE_DRAW_TWO", false},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			s := rig(t, table{top: tt.top, color: tt.color, hands: [][]string{{}, {}}})
			s.PendingDraw = tt.pending
			def, _ := s.cat.Def(Card{DefID: tt.card})
			if got := s.CanPlay(def); got != tt.want {
				t.Errorf("CanPlay(%s) on %s = %v, want %v", tt.card, tt.top, got, tt.want)
			}
		})
	}
}

func TestPlay_NumberCard(t *testing.T) {
	s := rig(t, table{top: "RED_5", hands: [][]string{{"RED_9", "BLUE_1", "GREEN_2"}, {"YELLOW_3"}}})

	ev := play(t, s, 0, "RED_9", "")

	if s.TopCard().DefID != "RED_9" || s.ActiveColor != Red {
		t.Errorf("top %s color %s, want RED_9 RED", s.TopCard().DefID, s.ActiveColor)
	}
	if len(s.Players[0].Hand) != 2 || s.Current != 1 {
		t.Errorf("hand %d current %d, want 2 and 1", len(s.Players[0].Hand), s.Current)
	}
	if got := eventTypes(ev); len(got) != 2 || got[0] != EvCardPlayed || got[1] != EvTurnChanged {
		t.Errorf("events = %v", got)
	}
}

func TestPlay_Rejected(t *testing.T) {
	s := rig(t, table{
		top:   "RED_5",
		hands: [][]string{{"BLUE_7", "WILD", "RED_1"}, {"RED_2"}},
	})

	_, err := s.Apply(0, Action{Type: PlayCard, CardID: cardIn(t, s, 0, "BLUE_7").ID})
	wantErr(t, err, ErrIllegalCard)

	_, err = s.Apply(1, Action{Type: PlayCard, CardID: cardIn(t, s, 1, "RED_2").ID})
	wantErr(t, err, ErrNotYourTurn)

	_, err = s.Apply(0, Action{Type: PlayCard, CardID: 999})
	wantErr(t, err, ErrCardNotInHand)

	wild := cardIn(t, s, 0, "WILD").ID
	_, err = s.Apply(0, Action{Type: PlayCard, CardID: wild})
	wantErr(t, err, ErrColorRequired)
	_, err = s.Apply(0, Action{Type: PlayCard, CardID: wild, Color: Wild})
	wantErr(t, err, ErrColorRequired)

	_, err = s.Apply(0, Action{Type: "DANCE"})
	wantErr(t, err, ErrUnknownAction)
	_, err = s.Apply(7, Action{Type: DrawCard})
	wantErr(t, err, ErrBadSeat)

	// None of the rejected actions changed anything.
	if len(s.Players[0].Hand) != 3 || s.TopCard().DefID != "RED_5" || s.Current != 0 {
		t.Error("a rejected action changed the state")
	}
}

func TestPlay_WildSetsColor(t *testing.T) {
	s := rig(t, table{top: "RED_5", hands: [][]string{{"WILD", "RED_1"}, {"RED_2"}}})
	play(t, s, 0, "WILD", Blue)
	if s.ActiveColor != Blue || s.Current != 1 {
		t.Errorf("color %s current %d, want BLUE 1", s.ActiveColor, s.Current)
	}
}

func TestPlay_Skip(t *testing.T) {
	s := rig(t, table{top: "RED_5", hands: [][]string{{"RED_SKIP", "RED_1"}, {"RED_2"}, {"RED_3"}}})
	play(t, s, 0, "RED_SKIP", "")
	if s.Current != 2 {
		t.Errorf("current = %d, want 2", s.Current)
	}
}

func TestPlay_Reverse(t *testing.T) {
	s := rig(t, table{top: "RED_5", hands: [][]string{{"RED_REVERSE", "RED_1"}, {"RED_2"}, {"RED_3"}}})
	ev := play(t, s, 0, "RED_REVERSE", "")
	if s.Direction != -1 || s.Current != 2 {
		t.Errorf("direction %d current %d, want -1 and 2", s.Direction, s.Current)
	}
	if got := eventTypes(ev); len(got) != 3 || got[1] != EvDirectionChanged {
		t.Errorf("events = %v", got)
	}
}

func TestPlay_ReverseWithTwoPlayersActsAsSkip(t *testing.T) {
	s := rig(t, table{top: "RED_5", hands: [][]string{{"RED_REVERSE", "RED_1"}, {"RED_2"}}})
	play(t, s, 0, "RED_REVERSE", "")
	if s.Current != 0 {
		t.Errorf("current = %d, want 0 (same player again)", s.Current)
	}
}

func TestDrawTwo_StackThenTake(t *testing.T) {
	s := rig(t, table{
		top:   "RED_5",
		hands: [][]string{{"RED_DRAW_TWO", "RED_1"}, {"BLUE_DRAW_TWO", "RED_2"}, {"GREEN_3", "GREEN_4"}},
		deck:  []string{"YELLOW_1", "YELLOW_2", "YELLOW_3", "YELLOW_4", "YELLOW_5"},
	})

	play(t, s, 0, "RED_DRAW_TWO", "")
	if s.PendingDraw != 2 || s.Current != 1 {
		t.Fatalf("pending %d current %d, want 2 and 1", s.PendingDraw, s.Current)
	}

	// Seat 1 can't dodge with a color match, but can stack.
	_, err := s.Apply(1, Action{Type: PlayCard, CardID: cardIn(t, s, 1, "RED_2").ID})
	wantErr(t, err, ErrIllegalCard)
	play(t, s, 1, "BLUE_DRAW_TWO", "")
	if s.PendingDraw != 4 || s.Current != 2 {
		t.Fatalf("pending %d current %d, want 4 and 2", s.PendingDraw, s.Current)
	}

	// Seat 2 can't stack, takes all 4 and loses the turn.
	ev := do(t, s, 2, DrawCard)
	if len(s.Players[2].Hand) != 6 || s.PendingDraw != 0 || s.Current != 0 {
		t.Errorf("hand %d pending %d current %d, want 6, 0, 0", len(s.Players[2].Hand), s.PendingDraw, s.Current)
	}
	if len(ev) != 2 || len(ev[0].Cards) != 4 {
		t.Errorf("events = %+v", ev)
	}
}

func TestWildDrawFour_Stack(t *testing.T) {
	s := rig(t, table{
		top:   "RED_5",
		hands: [][]string{{"WILD_DRAW_FOUR", "RED_1"}, {"WILD_DRAW_FOUR", "RED_DRAW_TWO", "RED_2"}},
	})
	play(t, s, 0, "WILD_DRAW_FOUR", Green)

	_, err := s.Apply(1, Action{Type: PlayCard, CardID: cardIn(t, s, 1, "RED_DRAW_TWO").ID})
	wantErr(t, err, ErrIllegalCard)
	play(t, s, 1, "WILD_DRAW_FOUR", Yellow)

	if s.PendingDraw != 8 || s.ActiveColor != Yellow || s.Current != 0 {
		t.Errorf("pending %d color %s current %d, want 8 YELLOW 0", s.PendingDraw, s.ActiveColor, s.Current)
	}
}

func TestDraw_UnplayableEndsTurn(t *testing.T) {
	s := rig(t, table{top: "RED_5", hands: [][]string{{"BLUE_1"}, {"RED_2"}}, deck: []string{"GREEN_7"}})
	do(t, s, 0, DrawCard)
	if len(s.Players[0].Hand) != 2 || s.Current != 1 || s.DrawnCard != nil {
		t.Errorf("hand %d current %d drawn %v", len(s.Players[0].Hand), s.Current, s.DrawnCard)
	}
}

func TestDraw_PlayableThenPlay(t *testing.T) {
	s := rig(t, table{
		top:   "RED_5",
		hands: [][]string{{"BLUE_1", "RED_1"}, {"RED_2"}},
		deck:  []string{"RED_7"},
	})
	do(t, s, 0, DrawCard)
	if s.Current != 0 || s.DrawnCard == nil || s.DrawnCard.DefID != "RED_7" {
		t.Fatalf("current %d drawn %v, want seat 0 holding RED_7", s.Current, s.DrawnCard)
	}

	_, err := s.Apply(0, Action{Type: DrawCard})
	wantErr(t, err, ErrAlreadyDrew)
	// RED_1 is playable too, but only the drawn card is allowed now.
	_, err = s.Apply(0, Action{Type: PlayCard, CardID: cardIn(t, s, 0, "RED_1").ID})
	wantErr(t, err, ErrMustPlayDrawnCard)

	play(t, s, 0, "RED_7", "")
	if s.Current != 1 || s.DrawnCard != nil {
		t.Errorf("current %d drawn %v, want 1 and nil", s.Current, s.DrawnCard)
	}
}

func TestDraw_PlayableThenPass(t *testing.T) {
	s := rig(t, table{top: "RED_5", hands: [][]string{{"BLUE_1"}, {"RED_2"}}, deck: []string{"RED_7"}})

	_, err := s.Apply(0, Action{Type: Pass})
	wantErr(t, err, ErrCannotPass)

	do(t, s, 0, DrawCard)
	do(t, s, 0, Pass)
	if s.Current != 1 || s.DrawnCard != nil || len(s.Players[0].Hand) != 2 {
		t.Errorf("current %d drawn %v hand %d", s.Current, s.DrawnCard, len(s.Players[0].Hand))
	}
}

func TestFinish_GameContinues(t *testing.T) {
	s := rig(t, table{top: "RED_5", hands: [][]string{{"RED_1"}, {"RED_2", "RED_3"}, {"RED_4", "RED_6"}}})

	ev := play(t, s, 0, "RED_1", "")

	if s.Over() || s.Players[0].Place != 1 || len(s.Ranking) != 1 || s.Current != 1 {
		t.Fatalf("over %v place %d ranking %v current %d; want game on, seat 0 first, seat 1 next",
			s.Over(), s.Players[0].Place, s.Ranking, s.Current)
	}
	if got := eventTypes(ev); len(got) != 3 || got[1] != EvPlayerFinished || ev[1].Place != 1 {
		t.Errorf("events = %+v", ev)
	}

	// Seat 0 is now skipped: 1 -> 2 -> 1.
	play(t, s, 1, "RED_2", "")
	if s.Current != 2 {
		t.Errorf("current = %d, want 2", s.Current)
	}
	play(t, s, 2, "RED_4", "")
	if s.Current != 1 {
		t.Errorf("current = %d, want 1 (seat 0 has finished)", s.Current)
	}
	_, err := s.Apply(0, Action{Type: DrawCard})
	wantErr(t, err, ErrNotYourTurn)
}

func TestFinish_LastTwoEndsGame(t *testing.T) {
	s := rig(t, table{top: "RED_5", hands: [][]string{{"RED_1"}, {"RED_2", "RED_3"}}})

	ev := play(t, s, 0, "RED_1", "")

	if !s.Over() {
		t.Fatal("game should be over with one player left")
	}
	if len(s.Ranking) != 2 || s.Ranking[0] != 0 || s.Ranking[1] != 1 || s.Players[1].Place != 2 {
		t.Errorf("ranking %v, places %d/%d; want [0 1]", s.Ranking, s.Players[0].Place, s.Players[1].Place)
	}
	want := []EventType{EvCardPlayed, EvPlayerFinished, EvPlayerFinished, EvGameOver}
	got := eventTypes(ev)
	if len(got) != len(want) {
		t.Fatalf("events = %v, want %v", got, want)
	}
	for i := range want {
		if got[i] != want[i] {
			t.Fatalf("events = %v, want %v", got, want)
		}
	}
	if r := ev[3].Ranking; len(r) != 2 || r[0] != 0 || r[1] != 1 {
		t.Errorf("GAME_OVER ranking = %v, want [0 1]", r)
	}
	_, err := s.Apply(1, Action{Type: DrawCard})
	wantErr(t, err, ErrGameOver)
}

func TestFinish_LastCardEffectStillApplies(t *testing.T) {
	t.Run("draw two", func(t *testing.T) {
		s := rig(t, table{
			top:   "RED_5",
			hands: [][]string{{"RED_DRAW_TWO"}, {"BLUE_1", "BLUE_2"}, {"GREEN_3", "GREEN_4"}},
			deck:  []string{"YELLOW_1", "YELLOW_2"},
		})
		play(t, s, 0, "RED_DRAW_TWO", "")
		if s.PendingDraw != 2 || s.Current != 1 {
			t.Fatalf("pending %d current %d, want 2 and 1", s.PendingDraw, s.Current)
		}
		do(t, s, 1, DrawCard)
		if len(s.Players[1].Hand) != 4 || s.Current != 2 {
			t.Errorf("hand %d current %d, want 4 and 2", len(s.Players[1].Hand), s.Current)
		}
	})
	t.Run("skip", func(t *testing.T) {
		s := rig(t, table{top: "RED_5", hands: [][]string{{"RED_SKIP"}, {"RED_1", "RED_2"}, {"RED_3", "RED_4"}}})
		play(t, s, 0, "RED_SKIP", "")
		if s.Current != 2 {
			t.Errorf("current = %d, want 2 (seat 1 skipped)", s.Current)
		}
	})
	t.Run("reverse by the finisher", func(t *testing.T) {
		// Three players; seat 1 finishes on a Reverse, leaving two. Play just
		// continues in the new direction: 1 -> 0.
		s := rig(t, table{
			top:   "RED_5",
			hands: [][]string{{"RED_1", "RED_2"}, {"RED_REVERSE"}, {"RED_3", "RED_4"}},
		})
		s.Current = 1
		play(t, s, 1, "RED_REVERSE", "")
		if s.Direction != -1 || s.Current != 0 {
			t.Errorf("direction %d current %d, want -1 and 0", s.Direction, s.Current)
		}
	})
	t.Run("wild draw four", func(t *testing.T) {
		s := rig(t, table{top: "RED_5", hands: [][]string{{"WILD_DRAW_FOUR"}, {"BLUE_1", "BLUE_2"}, {"GREEN_3", "GREEN_4"}}})
		play(t, s, 0, "WILD_DRAW_FOUR", Green)
		if s.PendingDraw != 4 || s.ActiveColor != Green || s.Current != 1 {
			t.Errorf("pending %d color %s current %d, want 4 GREEN 1", s.PendingDraw, s.ActiveColor, s.Current)
		}
	})
}

func TestReverse_TwoLeftOfFourActsAsSkip(t *testing.T) {
	s := rig(t, table{
		top:   "RED_5",
		hands: [][]string{{}, {"RED_REVERSE", "RED_1"}, {}, {"RED_2", "RED_3"}},
	})
	s.finish(0)
	s.finish(2)
	s.Current = 1

	play(t, s, 1, "RED_REVERSE", "")

	if s.Current != 1 {
		t.Errorf("current = %d, want 1 (two players left, Reverse acts as Skip)", s.Current)
	}
}

// TestRandomGames plays many full games with random legal moves and checks
// that no card is ever lost or duplicated, every game finishes, and everyone
// ends up with a place.
func TestRandomGames(t *testing.T) {
	cat := loadTestCatalog(t)
	colors := []Color{Red, Yellow, Green, Blue}

	for seed := uint64(1); seed <= 300; seed++ {
		players := MinPlayers + int(seed)%(MaxPlayers-MinPlayers+1)
		s, err := NewGame(cat, []string{"a", "b", "c", "d"}[:players], seed)
		if err != nil {
			t.Fatal(err)
		}
		rng := seededRNG(seed + 1000)

		for step := 0; !s.Over(); step++ {
			if step > 10000 {
				t.Fatalf("seed %d: game didn't finish in 10000 actions", seed)
			}
			seat := s.Current
			if !s.InGame(seat) {
				t.Fatalf("seed %d step %d: finished seat %d has the turn", seed, step, seat)
			}

			var a Action
			if playable := s.PlayableCards(seat); len(playable) > 0 {
				c := playable[rng.IntN(len(playable))]
				a = Action{Type: PlayCard, CardID: c.ID, Color: colors[rng.IntN(4)]}
			} else if s.DrawnCard != nil {
				a = Action{Type: Pass}
			} else {
				a = Action{Type: DrawCard}
			}
			if _, err := s.Apply(seat, a); err != nil {
				t.Fatalf("seed %d step %d: seat %d %+v: %v", seed, step, seat, a, err)
			}
			requireEveryCardOnce(t, allCards(s))
			if t.Failed() {
				t.Fatalf("seed %d step %d: cards lost or duplicated", seed, step)
			}
		}

		seen := map[int]bool{}
		for i, seat := range s.Ranking {
			if seen[seat] || s.Players[seat].Place != i+1 {
				t.Fatalf("seed %d: bad ranking %v", seed, s.Ranking)
			}
			seen[seat] = true
		}
	}
}
