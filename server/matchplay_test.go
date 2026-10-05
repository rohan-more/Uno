package main

import (
	"context"
	"encoding/json"
	"testing"

	"github.com/rohan-more/Uno/server/game"
)

// playMatch runs the handler's loop until the match asks to close, or the tick
// budget runs out. Returns how many ticks it took.
func playMatch(t *testing.T, s *MatchState, d *fakeDispatcher, maxTicks int) int {
	t.Helper()
	for tick := 1; tick <= maxTicks; tick++ {
		s.tickPlaying(context.Background(), testLogger{}, nil, d)
		if s.closeRequest {
			return tick
		}
	}
	t.Fatalf("match did not finish within %d ticks", maxTicks)
	return 0
}

func TestMatchPlays_AllBotsFinishTheGame(t *testing.T) {
	s := dealtMatch(t)
	// Bots everywhere except seat 0, which is a human who never acts: events are
	// only broadcast to human seats, so someone has to be there to receive them.
	// Their seat is never taken over, so the game runs to the end.
	s.seats[0].presence = fakePresence{userID: "u0"}
	s.seats[2] = seat{kind: KindBot, name: "Bot2", connected: true}
	s.cfg.MissedTurnsForBot = 1 << 30
	s.cfg.DisconnectBotMs = 1 << 30
	d := &fakeDispatcher{}

	playMatch(t, s, d, 20000)

	if !s.game.Over() {
		t.Fatal("match closed before the game was over")
	}
	if len(s.game.Ranking) != seatCount {
		t.Errorf("ranking = %v, want all four seats", s.game.Ranking)
	}
	if got := d.events(EvGameOver); len(got) != 1 {
		t.Errorf("GAME_OVER events = %d, want 1", len(got))
	}
	if got := d.events(EvTurnChanged); len(got) < 4 {
		t.Errorf("only %d TURN_CHANGED events in a whole game", len(got))
	}
}

func TestTurnTimeout_DrawsAndMovesOn(t *testing.T) {
	s := dealtMatch(t)
	s.seats[0].presence = fakePresence{userID: "u0"}
	d := &fakeDispatcher{}
	ctx, logger := context.Background(), testLogger{}

	// Get past the pre-match pause so seat 0 is on the clock.
	for s.preMatchTicks > 0 {
		s.tickPlaying(ctx, logger, nil, d)
	}
	handBefore := len(s.game.Players[0].Hand)

	// Let the turn run out: the gap after the previous move runs first.
	for i := 0; i < (s.cfg.TurnGapMs+s.cfg.TurnMs)/msPerTick+2; i++ {
		s.tickPlaying(ctx, logger, nil, d)
	}

	if len(d.events(EvTurnTimedOut)) == 0 {
		t.Error("no TURN_TIMED_OUT event")
	}
	if len(s.game.Players[0].Hand) != handBefore+1 {
		t.Errorf("hand = %d, want %d: a timeout should draw exactly one card",
			len(s.game.Players[0].Hand), handBefore+1)
	}
	if s.game.Current == 0 {
		t.Error("the turn should have moved on")
	}
	if s.missedTurns[0] != 1 {
		t.Errorf("missedTurns = %d, want 1", s.missedTurns[0])
	}
}

func TestMissedTurns_HandSeatToBot(t *testing.T) {
	s := dealtMatch(t)
	s.seats[0].presence = fakePresence{userID: "u0"}
	// Seat 2 stays a connected human so there is somebody left to receive the
	// SEAT_CONTROL broadcast when seat 0 is taken over.
	s.seats[2].presence = fakePresence{userID: "u2"}
	d := &fakeDispatcher{}
	ctx, logger := context.Background(), testLogger{}

	for tick := 0; tick < 2000 && s.seats[0].kind == KindHuman; tick++ {
		s.tickPlaying(ctx, logger, nil, d)
	}

	if s.seats[0].kind != KindBot {
		t.Fatal("seat 0 should have been given to a bot after missing turns")
	}
	if got := d.events(EvSeatControl); len(got) != 1 || got[0].Seat != 0 || got[0].Kind != KindBot {
		t.Errorf("SEAT_CONTROL events = %+v", got)
	}
	if len(d.kicked) != 1 || d.kicked[0].GetUserId() != "u0" {
		t.Errorf("replaced player should be kicked, kicked = %+v", d.kicked)
	}
	assertRemoved(t, d, "u0", RemovedMissedTurns)
}

func TestMissedTurns_LastHumanIsToldBeforeClose(t *testing.T) {
	s := dealtMatch(t)
	s.seats[0].presence = fakePresence{userID: "u0"}
	d := &fakeDispatcher{}
	ctx, logger := context.Background(), testLogger{}

	for tick := 0; tick < 2000 && !s.closeRequest; tick++ {
		s.tickPlaying(ctx, logger, nil, d)
	}

	if !s.closeRequest {
		t.Fatal("match should close when its only human misses their turns")
	}
	assertRemoved(t, d, "u0", RemovedMissedTurns)
}

// assertRemoved checks exactly one REMOVED went to userID, with the reason.
func assertRemoved(t *testing.T, d *fakeDispatcher, userID, reason string) {
	t.Helper()
	got := d.removed()
	if len(got) != 1 {
		t.Fatalf("REMOVED messages = %d, want 1", len(got))
	}
	if len(got[0].to) != 1 || got[0].to[0].GetUserId() != userID {
		t.Errorf("REMOVED went to %+v, want only %s", got[0].to, userID)
	}
	var msg RemovedMsg
	if err := json.Unmarshal(got[0].data, &msg); err != nil || msg.Reason != reason {
		t.Errorf("REMOVED = %s, want reason %s", got[0].data, reason)
	}
}

func TestClientAction_IllegalCardIsRejected(t *testing.T) {
	s := dealtMatch(t)
	presence := fakePresence{userID: "u0"}
	s.seats[0].presence = presence
	d := &fakeDispatcher{}
	ctx, logger := context.Background(), testLogger{}

	for s.preMatchTicks > 0 {
		s.tickPlaying(ctx, logger, nil, d)
	}

	// A card id nobody holds.
	body, _ := json.Marshal(PlayCardReq{CardID: 9999})
	msg := fakeMatchData{fakePresence: presence, opCode: OpPlayCard, data: body}

	before := len(s.game.Players[0].Hand)
	s.handleAction(logger, d, 0, msg)

	errs := d.errors()
	if len(errs) != 1 || errs[0].Code != ErrCodeCardNotInHand {
		t.Fatalf("errors = %+v, want one CARD_NOT_IN_HAND", errs)
	}
	if len(s.game.Players[0].Hand) != before || s.game.Current != 0 {
		t.Error("a rejected action changed the game")
	}
}

func TestClientAction_LegalPlayAdvancesTheTurn(t *testing.T) {
	s := dealtMatch(t)
	presence := fakePresence{userID: "u0"}
	s.seats[0].presence = presence
	d := &fakeDispatcher{}
	ctx, logger := context.Background(), testLogger{}

	for s.preMatchTicks > 0 {
		s.tickPlaying(ctx, logger, nil, d)
	}

	playable := s.game.PlayableCards(0)
	if len(playable) == 0 {
		t.Skip("seat 0 was dealt nothing playable with this seed")
	}

	s.missedTurns[0] = 1 // pretend they timed out once already
	body, _ := json.Marshal(PlayCardReq{CardID: playable[0].ID, Color: string(game.Blue)})
	s.handleAction(logger, d, 0, fakeMatchData{fakePresence: presence, opCode: OpPlayCard, data: body})

	if len(d.errors()) != 0 {
		t.Fatalf("legal play was rejected: %+v", d.errors())
	}
	if len(d.events(EvCardPlayed)) != 1 {
		t.Error("no CARD_PLAYED event")
	}
	if s.game.Current == 0 {
		t.Error("the turn did not move on")
	}
	if s.missedTurns[0] != 0 {
		t.Error("acting in time should clear the missed turn counter")
	}
}

func TestLastHumanGone_ClosesInsteadOfHandingToABot(t *testing.T) {
	s := dealtMatch(t)
	// One human, the rest bots: nobody would be left watching.
	s.seats[0].presence = fakePresence{userID: "u0"}
	s.seats[2] = seat{kind: KindBot, name: "Bot2", connected: true}
	d := &fakeDispatcher{}
	ctx, logger := context.Background(), testLogger{}

	for tick := 0; tick < 3000 && !s.closeRequest; tick++ {
		s.tickPlaying(ctx, logger, nil, d)
	}

	if !s.closeRequest {
		t.Fatal("match should close once the only human stops playing")
	}
	if s.seats[0].kind != KindHuman {
		t.Error("the last seat should not be handed to a bot")
	}
	if len(d.events(EvSeatControl)) != 0 {
		t.Error("no SEAT_CONTROL should be sent when the match is simply closing")
	}
}

func TestTurnGap_DelaysTheNextMove(t *testing.T) {
	s := dealtMatch(t)
	for i := range s.seats { // all bots, so only the clock decides the pace
		s.seats[i] = seat{kind: KindBot, name: "Bot", connected: true}
	}
	d := &fakeDispatcher{}
	ctx, logger := context.Background(), testLogger{}

	for s.preMatchTicks > 0 {
		s.tickPlaying(ctx, logger, nil, d)
	}
	discardBefore := len(s.game.Discard)

	// One tick is not enough: the gap and the bot's thinking pause come first.
	s.tickPlaying(ctx, logger, nil, d)
	if len(s.game.Discard) != discardBefore {
		t.Error("a bot played immediately, with no pause")
	}

	for i := 0; i < (s.cfg.TurnGapMs+s.cfg.BotThinkMs)/msPerTick+2; i++ {
		s.tickPlaying(ctx, logger, nil, d)
	}
	if len(s.game.Discard) == discardBefore && len(s.game.Players[0].Hand) == game.HandSize {
		t.Error("the bot never moved after its pause")
	}
}
