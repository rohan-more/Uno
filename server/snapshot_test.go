package main

import (
	"os"
	"testing"

	"github.com/rohan-more/Uno/server/game"
)

// dealtMatch builds a started match with four seats: two humans, two bots.
func dealtMatch(t *testing.T) *MatchState {
	t.Helper()

	data, err := os.ReadFile("data/cards.json")
	if err != nil {
		t.Fatalf("read cards.json: %v", err)
	}
	cat, err := game.LoadCatalog(data)
	if err != nil {
		t.Fatalf("LoadCatalog: %v", err)
	}

	s := newLobby()
	s.seats[0] = seat{kind: KindHuman, userID: "u0", name: "Human0", avatar: 1, connected: true}
	s.seats[1] = seat{kind: KindBot, name: "Bot1", avatar: 2, connected: true}
	s.seats[2] = seat{kind: KindHuman, userID: "u2", name: "Human2", avatar: 3, connected: true}
	s.seats[3] = seat{kind: KindBot, name: "Bot3", avatar: 4, connected: true}

	g, err := game.NewGame(cat, []string{"u0", "bot:Bot1", "u2", "bot:Bot3"}, 42)
	if err != nil {
		t.Fatalf("NewGame: %v", err)
	}

	s.game = g
	s.phase = phasePlaying
	s.preMatchTicks = s.cfg.PreMatchMs / msPerTick
	return s
}

func TestBuildGameState_ShowsOnlyYourHand(t *testing.T) {
	s := dealtMatch(t)

	for _, seat := range []int{0, 2} {
		msg := s.buildGameState(seat)

		if msg.You != seat {
			t.Errorf("seat %d: You = %d", seat, msg.You)
		}
		if len(msg.Hand) != game.HandSize {
			t.Errorf("seat %d: hand has %d cards, want %d", seat, len(msg.Hand), game.HandSize)
		}

		// The hand in the message must be this seat's actual hand.
		for i, card := range msg.Hand {
			if card.ID != s.game.Players[seat].Hand[i].ID {
				t.Errorf("seat %d: card %d is not from this player's hand", seat, i)
			}
		}

		// Everyone else is a count and nothing more.
		if len(msg.Seats) != seatCount {
			t.Fatalf("seat %d: %d seats in snapshot, want %d", seat, len(msg.Seats), seatCount)
		}
		for i, gs := range msg.Seats {
			if gs.CardCount != game.HandSize {
				t.Errorf("seat %d: seat %d count = %d, want %d", seat, i, gs.CardCount, game.HandSize)
			}
			if gs.Name != s.seats[i].name || gs.Kind != s.seats[i].kind {
				t.Errorf("seat %d: seat %d = %+v, does not match the table", seat, i, gs)
			}
		}
	}
}

func TestBuildGameState_TableFacts(t *testing.T) {
	s := dealtMatch(t)
	msg := s.buildGameState(0)

	if want := 108 - game.HandSize*seatCount - 1; msg.DeckCount != want {
		t.Errorf("deck count = %d, want %d", msg.DeckCount, want)
	}
	if msg.TopCard.DefID == "" || msg.ActiveColor == "" {
		t.Errorf("top card %+v / color %q missing", msg.TopCard, msg.ActiveColor)
	}
	if msg.CurrentSeat != 0 || msg.Direction != 1 || msg.PendingDraw != 0 {
		t.Errorf("start state wrong: current %d, direction %d, pending %d",
			msg.CurrentSeat, msg.Direction, msg.PendingDraw)
	}
	if msg.StartsInMs != s.cfg.PreMatchMs {
		t.Errorf("startsInMs = %d, want %d", msg.StartsInMs, s.cfg.PreMatchMs)
	}
	if msg.TurnMsLeft != 0 {
		t.Errorf("turnMsLeft = %d before the first turn, want 0", msg.TurnMsLeft)
	}
	if msg.DrawnCardID != nil {
		t.Errorf("drawnCardId = %v at the start, want nil", *msg.DrawnCardID)
	}
}

func TestHideCards_OnlyTheDrawerSeesCards(t *testing.T) {
	events := []EventMsg{
		{Type: EvCardsDrawn, Seat: 1, Count: 2, Cards: []CardMsg{{ID: 5, DefID: "RED_5"}, {ID: 6, DefID: "RED_6"}}},
		{Type: EvTurnChanged, Seat: 2},
	}

	forDrawer := hideCards(events, 1)
	if len(forDrawer[0].Cards) != 2 {
		t.Errorf("the drawer should see their 2 cards, got %d", len(forDrawer[0].Cards))
	}

	forOthers := hideCards(events, 0)
	if forOthers[0].Cards != nil {
		t.Errorf("other players must not see drawn cards, got %+v", forOthers[0].Cards)
	}
	if forOthers[0].Count != 2 {
		t.Errorf("other players still need the count, got %d", forOthers[0].Count)
	}

	// The original batch must not be modified, or the next player would be
	// handed a stripped copy.
	if len(events[0].Cards) != 2 {
		t.Error("hideCards changed the original events")
	}
}
