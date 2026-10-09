package game

import "testing"

func TestBotAction_AlwaysLegal(t *testing.T) {
	cat := loadTestCatalog(t)

	// Whole games driven by bots: every move must be accepted, every game must
	// finish, and no card may be lost along the way.
	for seed := uint64(1); seed <= 100; seed++ {
		players := MinPlayers + int(seed)%(MaxPlayers-MinPlayers+1)
		s, err := NewGame(cat, []string{"a", "b", "c", "d"}[:players], seed)
		if err != nil {
			t.Fatal(err)
		}
		rng := seededRNG(seed + 500)

		for step := 0; !s.Over(); step++ {
			if step > 10000 {
				t.Fatalf("seed %d: bots never finished the game", seed)
			}

			seat := s.Current
			action := BotAction(s, seat, rng)
			if _, err := s.Apply(seat, action); err != nil {
				t.Fatalf("seed %d step %d: bot played %+v: %v", seed, step, action, err)
			}
			requireEveryCardOnce(t, allCards(s))
			if t.Failed() {
				t.Fatalf("seed %d step %d: cards lost", seed, step)
			}
		}
	}
}

func TestBotAction_StacksAPendingDraw(t *testing.T) {
	s := rig(t, table{
		top:   "RED_DRAW_TWO",
		hands: [][]string{{"BLUE_DRAW_TWO", "RED_5"}, {"GREEN_3", "GREEN_4"}},
	})
	s.PendingDraw = 2

	action := BotAction(s, 0, seededRNG(1))

	if action.Type != PlayCard {
		t.Fatalf("action = %+v, want the bot to stack", action)
	}
	if card := cardIn(t, s, 0, "BLUE_DRAW_TWO"); action.CardID != card.ID {
		t.Errorf("played card %d, want the Draw Two %d", action.CardID, card.ID)
	}
}

func TestBotAction_TakesTheCardsWhenItCannotStack(t *testing.T) {
	s := rig(t, table{
		top:   "RED_DRAW_TWO",
		hands: [][]string{{"BLUE_5", "GREEN_9"}, {"GREEN_3"}},
		deck:  []string{"YELLOW_1", "YELLOW_2"},
	})
	s.PendingDraw = 2

	if action := BotAction(s, 0, seededRNG(1)); action.Type != DrawCard {
		t.Errorf("action = %+v, want DrawCard", action)
	}
}

func TestBotAction_WildUsesTheColorItHoldsMost(t *testing.T) {
	s := rig(t, table{
		top:   "RED_5",
		hands: [][]string{{"WILD", "BLUE_1", "BLUE_7", "BLUE_9", "GREEN_2"}, {"RED_3"}},
	})

	action := BotAction(s, 0, seededRNG(1))

	if action.Type != PlayCard || action.Color != Blue {
		t.Errorf("action = %+v, want a play with BLUE (three blue cards in hand)", action)
	}
}

func TestBotAction_PassesAfterAnUnplayableDraw(t *testing.T) {
	s := rig(t, table{
		top:   "RED_5",
		hands: [][]string{{"BLUE_1"}, {"RED_3"}},
		deck:  []string{"RED_9"},
	})

	// Draw a playable card, then hand the bot a state where it holds it.
	if _, err := s.Apply(0, Action{Type: DrawCard}); err != nil {
		t.Fatal(err)
	}
	if s.DrawnCard == nil {
		t.Fatal("expected the drawn card to be playable")
	}

	// It is playable, so the bot plays it rather than passing.
	if action := BotAction(s, 0, seededRNG(1)); action.Type != PlayCard {
		t.Errorf("action = %+v, want the drawn card played", action)
	}

	// With nothing playable it passes instead.
	s.DrawnCard = &Card{ID: 999, DefID: "BLUE_1"}
	if action := BotAction(s, 0, seededRNG(1)); action.Type != Pass {
		t.Errorf("action = %+v, want Pass", action)
	}
}
