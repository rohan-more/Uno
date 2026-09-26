package main

import (
	"encoding/json"
	"math/rand/v2"
	"testing"
)

// newLobby builds a match state without Nakama, for testing seat logic.
func newLobby() *MatchState {
	s := &MatchState{
		cfg:            DefaultMatchConfig(),
		rng:            rand.New(rand.NewPCG(1, 2)),
		phase:          phaseLobby,
		countdownTicks: -1,
	}
	for i := range s.seats {
		s.seats[i] = seat{kind: KindEmpty}
	}
	return s
}

func (s *MatchState) addHuman(t *testing.T, userID string) int {
	t.Helper()
	idx := s.freeSeat()
	if idx < 0 {
		t.Fatal("no free seat")
	}
	s.seats[idx] = seat{kind: KindHuman, userID: userID, name: userID, connected: true}
	return idx
}

func TestFreeSeat_PrefersEmptyThenBot(t *testing.T) {
	s := newLobby()
	if got := s.freeSeat(); got != 0 {
		t.Fatalf("first seat = %d, want 0", got)
	}

	s.addHuman(t, "a")          // seat 0
	s.seats[1] = s.newBotSeat() // bot at 1
	if got := s.freeSeat(); got != 2 {
		t.Errorf("free seat = %d, want 2 (an empty seat beats a bot seat)", got)
	}

	// Table full apart from bots: the lowest bot seat gets taken over.
	s.seats[2] = s.newBotSeat()
	s.seats[3] = s.newBotSeat()
	if got := s.freeSeat(); got != 1 {
		t.Errorf("free seat = %d, want 1 (lowest bot seat)", got)
	}

	// Four humans: nowhere to sit.
	for i := 1; i < seatCount; i++ {
		s.seats[i] = seat{kind: KindHuman, userID: "u", connected: true}
	}
	if got := s.freeSeat(); got != -1 {
		t.Errorf("free seat = %d, want -1 when full", got)
	}
}

func TestNewBotSeat_UniqueNamesAndAvatars(t *testing.T) {
	s := newLobby()
	names := map[string]bool{}
	avatars := map[int]bool{}

	for i := range s.seats {
		s.seats[i] = s.newBotSeat()
		if names[s.seats[i].name] {
			t.Errorf("duplicate bot name %q at seat %d", s.seats[i].name, i)
		}
		if avatars[s.seats[i].avatar] {
			t.Errorf("duplicate avatar %d at seat %d", s.seats[i].avatar, i)
		}
		names[s.seats[i].name] = true
		avatars[s.seats[i].avatar] = true
	}
}

func TestLabel_OpenUntilCutoff(t *testing.T) {
	parse := func(label string) (open int, phase string) {
		var l struct {
			Open  int    `json:"open"`
			Phase string `json:"phase"`
		}
		if err := json.Unmarshal([]byte(label), &l); err != nil {
			t.Fatalf("label %q: %v", label, err)
		}
		return l.Open, l.Phase
	}

	s := newLobby()
	if open, phase := parse(s.label()); open != 1 || phase != phaseLobby {
		t.Errorf("empty lobby: open %d phase %s, want 1 lobby", open, phase)
	}

	s.addHuman(t, "a")
	s.countdownTicks = s.cfg.LobbyCountdownMs / msPerTick
	if open, _ := parse(s.label()); open != 1 {
		t.Error("lobby with a free seat should be open")
	}

	// Inside the cutoff, nobody new may join even with seats free.
	s.countdownTicks = s.cfg.JoinCutoffMs / msPerTick
	if open, _ := parse(s.label()); open != 0 {
		t.Error("lobby should close within the join cutoff")
	}

	s.countdownTicks = s.cfg.LobbyCountdownMs / msPerTick
	s.phase = phasePlaying
	if open, _ := parse(s.label()); open != 0 {
		t.Error("a started match should never be open")
	}
}

func TestCountdownMsLeft(t *testing.T) {
	s := newLobby()
	if got := s.countdownMsLeft(); got != s.cfg.LobbyCountdownMs {
		t.Errorf("before anyone joins = %d, want the full countdown", got)
	}

	s.countdownTicks = 10 // ticks
	if got, want := s.countdownMsLeft(), 10*msPerTick; got != want {
		t.Errorf("countdownMsLeft = %d, want %d", got, want)
	}
}

func TestSanitizeConfig(t *testing.T) {
	def := DefaultMatchConfig()
	logger := testLogger{}

	bad := def
	bad.TurnMs = 0
	bad.LobbyCountdownMs = -5
	bad.BotJoinAtMsLeft = []int{99999, -1} // both outside the countdown
	bad.JoinCutoffMs = 999999              // longer than the countdown

	got := sanitize(bad, def, logger)

	if got.TurnMs != def.TurnMs || got.LobbyCountdownMs != def.LobbyCountdownMs {
		t.Errorf("bad durations were not replaced: %+v", got)
	}
	if len(got.BotJoinAtMsLeft) != len(def.BotJoinAtMsLeft) {
		t.Errorf("bot marks = %v, want the defaults", got.BotJoinAtMsLeft)
	}
	if got.JoinCutoffMs != def.JoinCutoffMs {
		t.Errorf("join cutoff = %d, want the default", got.JoinCutoffMs)
	}
}

func TestSanitizeConfig_SortsBotMarks(t *testing.T) {
	def := DefaultMatchConfig()
	in := def
	in.BotJoinAtMsLeft = []int{3000, 8000, 5000}

	got := sanitize(in, def, testLogger{}).BotJoinAtMsLeft

	for i := 1; i < len(got); i++ {
		if got[i] > got[i-1] {
			t.Fatalf("marks not in descending order: %v", got)
		}
	}
}
