package main

import (
	"context"
	"errors"
	"os"
	"strconv"
	"testing"

	"github.com/heroiclabs/nakama-common/runtime"

	"github.com/rohan-more/Uno/server/game"
)

// fakeBank keeps balances and escrow records in memory, all-or-nothing like
// Nakama's MultiUpdate, with version checks on the records.
type fakeBank struct {
	coins    map[string]int64
	escrows  map[string]heldEscrow
	versions int
	ledger   []string // "user reason amount", in order
	failNext error    // returned once by the next write, then cleared
}

func newFakeBank(coins map[string]int64) *fakeBank {
	return &fakeBank{coins: coins, escrows: map[string]heldEscrow{}}
}

func (b *fakeBank) Balances(ctx context.Context, userIDs []string) (map[string]int64, error) {
	out := map[string]int64{}
	for _, id := range userIDs {
		out[id] = b.coins[id]
	}
	return out, nil
}

func (b *fakeBank) fail() error {
	err := b.failNext
	b.failNext = nil
	return err
}

func (b *fakeBank) nextVersion() string {
	b.versions++
	return strconv.Itoa(b.versions)
}

func (b *fakeBank) Hold(ctx context.Context, e escrow, take map[string]int64) (string, error) {
	if err := b.fail(); err != nil {
		return "", err
	}
	if _, exists := b.escrows[e.MatchID]; exists {
		return "", errors.New("escrow exists")
	}
	for id, amount := range take {
		if b.coins[id] < amount {
			return "", errShort
		}
	}
	for id, amount := range take {
		b.coins[id] -= amount
		b.ledger = append(b.ledger, id+" "+CoinsStake+" "+strconv.FormatInt(-amount, 10))
	}
	v := b.nextVersion()
	b.escrows[e.MatchID] = heldEscrow{Escrow: e, Version: v}
	return v, nil
}

func (b *fakeBank) Update(ctx context.Context, e escrow, version string) (string, error) {
	if err := b.fail(); err != nil {
		return "", err
	}
	if h, ok := b.escrows[e.MatchID]; !ok || h.Version != version {
		return "", errAlreadyChanged
	}
	v := b.nextVersion()
	b.escrows[e.MatchID] = heldEscrow{Escrow: e, Version: v}
	return v, nil
}

func (b *fakeBank) Release(ctx context.Context, matchID, version string, pay map[string]int64, reason string) error {
	if err := b.fail(); err != nil {
		return err
	}
	if h, ok := b.escrows[matchID]; !ok || h.Version != version {
		return errAlreadyChanged
	}
	delete(b.escrows, matchID)
	for id, amount := range pay {
		if amount != 0 {
			b.coins[id] += amount
			b.ledger = append(b.ledger, id+" "+reason+" "+strconv.FormatInt(amount, 10))
		}
	}
	return nil
}

func (b *fakeBank) Held(ctx context.Context) ([]heldEscrow, error) {
	var out []heldEscrow
	for _, h := range b.escrows {
		out = append(out, h)
	}
	return out, nil
}

// ---------- payout maths ----------

func results(places ...int) []game.Result {
	out := make([]game.Result, len(places))
	for i, p := range places {
		out[i] = game.Result{Seat: i, Place: p}
	}
	return out
}

func TestPayoutShares(t *testing.T) {
	percents := []int{60, 30, 10, 0}
	tests := []struct {
		name   string
		pot    int64
		places []int
		want   []int64 // by seat
	}{
		{"no ties", 200, []int{1, 2, 3, 4}, []int64{120, 60, 20, 0}},
		{"two share second", 200, []int{1, 2, 2, 4}, []int64{120, 40, 40, 0}},
		{"two share third", 200, []int{1, 2, 3, 3}, []int64{120, 60, 10, 10}},
		{"three share second", 200, []int{1, 2, 2, 2}, []int64{120, 26, 26, 26}}, // 80/3, 2 left to the house
		{"free match", 0, []int{1, 2, 3, 4}, []int64{0, 0, 0, 0}},
	}
	for _, tt := range tests {
		got := payoutShares(tt.pot, percents, results(tt.places...))
		for seat, want := range tt.want {
			if got[seat] != want {
				t.Errorf("%s: seat %d gets %d, want %d (all: %v)", tt.name, seat, got[seat], want, got)
			}
		}
	}
}

func TestValidPayouts(t *testing.T) {
	for _, ok := range [][]int{{60, 30, 10, 0}, {100}, {50, 50}, {50, 30}} {
		if !validPayouts(ok) {
			t.Errorf("%v should be accepted", ok)
		}
	}
	for _, bad := range [][]int{nil, {60, 50}, {-10, 110}, {25, 25, 25, 25, 0}} {
		if validPayouts(bad) {
			t.Errorf("%v should be refused", bad)
		}
	}
}

// ---------- in a match ----------

// stakedLobby is a lobby about to start: humans u0 and u2, empty seats 1 and 3.
func stakedLobby(t *testing.T, bank *fakeBank) *MatchState {
	t.Helper()
	if catalog == nil { // startMatch deals from the global catalog main.go loads
		data, err := os.ReadFile("data/cards.json")
		if err != nil {
			t.Fatal(err)
		}
		if catalog, err = game.LoadCatalog(data); err != nil {
			t.Fatal(err)
		}
	}
	s := newLobby()
	s.matchID = "m1"
	s.bank = bank
	s.stake = 50
	s.payoutPercents = []int{60, 30, 10, 0}
	s.seats[0] = seat{kind: KindHuman, userID: "u0", name: "Human0", connected: true, presence: fakePresence{userID: "u0"}}
	s.seats[2] = seat{kind: KindHuman, userID: "u2", name: "Human2", connected: true, presence: fakePresence{userID: "u2"}}
	return s
}

func TestStakes_TakenWhenTheMatchStarts(t *testing.T) {
	bank := newFakeBank(map[string]int64{"u0": 500, "u2": 80})
	s := stakedLobby(t, bank)

	s.startMatch(context.Background(), testLogger{}, nil, &fakeDispatcher{})

	if s.phase != phasePlaying || s.escrow == nil || len(s.escrow.Seats) != 2 {
		t.Fatalf("phase %s escrow %+v; want playing with two stakes held", s.phase, s.escrow)
	}
	if bank.coins["u0"] != 450 || bank.coins["u2"] != 30 || len(bank.escrows) != 1 {
		t.Errorf("coins %v escrows %d", bank.coins, len(bank.escrows))
	}
}

func TestStakes_ShortPlayerIsReplacedAtTheStart(t *testing.T) {
	bank := newFakeBank(map[string]int64{"u0": 500, "u2": 20})
	s := stakedLobby(t, bank)
	d := &fakeDispatcher{}

	s.startMatch(context.Background(), testLogger{}, nil, d)

	if s.seats[2].kind != KindBot || s.escrow == nil || len(s.escrow.Seats) != 1 {
		t.Fatalf("seat 2 %s, escrow %+v; want a bot and only u0 staked", s.seats[2].kind, s.escrow)
	}
	assertRemoved(t, d, "u2", RemovedNotEnoughCoins)
	if bank.coins["u2"] != 20 {
		t.Errorf("u2 should keep their coins, has %d", bank.coins["u2"])
	}
}

func TestStakes_NobodyCanPayClosesTheMatch(t *testing.T) {
	bank := newFakeBank(map[string]int64{"u0": 10, "u2": 10})
	s := stakedLobby(t, bank)

	s.startMatch(context.Background(), testLogger{}, nil, &fakeDispatcher{})

	if !s.closeRequest || s.game != nil || len(bank.escrows) != 0 {
		t.Errorf("close %v game %v escrows %d; want the match closed undealt", s.closeRequest, s.game != nil, len(bank.escrows))
	}
}

func TestStakes_FreeMatchTakesNothing(t *testing.T) {
	bank := newFakeBank(map[string]int64{"u0": 500, "u2": 500})
	s := stakedLobby(t, bank)
	s.stake = 0 // what a quick match has

	s.startMatch(context.Background(), testLogger{}, nil, &fakeDispatcher{})

	if s.escrow != nil || bank.coins["u0"] != 500 || len(bank.ledger) != 0 {
		t.Errorf("a free match must not touch the wallets: %v", bank.ledger)
	}
}

// startedStakedMatch is a staked match in play, past the pre-match pause.
func startedStakedMatch(t *testing.T, bank *fakeBank) *MatchState {
	t.Helper()
	s := stakedLobby(t, bank)
	s.startMatch(context.Background(), testLogger{}, nil, &fakeDispatcher{})
	if s.escrow == nil {
		t.Fatal("stakes were not held")
	}
	s.preMatchTicks = 0
	return s
}

func TestStakes_PaidOutByPlaceAtGameOver(t *testing.T) {
	bank := newFakeBank(map[string]int64{"u0": 500, "u2": 500})
	s := startedStakedMatch(t, bank)
	d := &fakeDispatcher{}

	sendDebug(s, debugCtx(), d, OpDebugWin, 0) // u0 wins

	over := d.events(EvGameOver) // one copy for each of the two humans
	if len(over) != 2 || over[0].Pot != 200 || over[0].Stake != 50 || over[0].Results[0].Coins != 120 {
		t.Fatalf("GAME_OVER = %+v, want pot 200 and 120 coins to the winner", over)
	}
	if !s.settleDue || !s.payOut(context.Background(), testLogger{}) {
		t.Fatal("the payout should be due and go through")
	}

	u2Place := s.game.Players[2].Place
	want := map[int]int64{1: 120, 2: 60, 3: 20, 4: 0}
	if bank.coins["u0"] != 450+120 || bank.coins["u2"] != 450+want[u2Place] {
		t.Errorf("coins %v, u2 placed %d", bank.coins, u2Place)
	}
	if s.escrow != nil || len(bank.escrows) != 0 {
		t.Error("the escrow should be released")
	}
}

func TestStakes_ForfeitedSeatIsNotPaid(t *testing.T) {
	bank := newFakeBank(map[string]int64{"u0": 500, "u2": 500})
	s := startedStakedMatch(t, bank)
	d := &fakeDispatcher{}

	s.giveSeatToBot(context.Background(), testLogger{}, nil, d, 2, RemovedLeft)
	if !bank.escrows["m1"].Escrow.Seats[1].Forfeited {
		t.Fatal("the forfeit should be stored in the escrow")
	}

	sendDebug(s, debugCtx(), d, OpDebugWin, 2) // the bot in u2's old seat wins
	s.payOut(context.Background(), testLogger{})

	if bank.coins["u2"] != 450 {
		t.Errorf("u2 forfeited, so gets nothing: has %d", bank.coins["u2"])
	}
}

func TestStakes_RefundedWhenTheMatchEndsEarly(t *testing.T) {
	bank := newFakeBank(map[string]int64{"u0": 500, "u2": 500})
	s := startedStakedMatch(t, bank)
	s.giveSeatToBot(context.Background(), testLogger{}, nil, &fakeDispatcher{}, 2, RemovedMissedTurns)

	s.releaseStakes(context.Background(), testLogger{}) // e.g. every human dropped

	if bank.coins["u0"] != 500 || bank.coins["u2"] != 450 || len(bank.escrows) != 0 {
		t.Errorf("coins %v; want u0 refunded and u2's forfeit kept", bank.coins)
	}
}

func TestStakes_FailedPayoutIsRetried(t *testing.T) {
	bank := newFakeBank(map[string]int64{"u0": 500, "u2": 500})
	s := startedStakedMatch(t, bank)
	sendDebug(s, debugCtx(), &fakeDispatcher{}, OpDebugWin, 0)

	bank.failNext = errors.New("database down")
	if s.payOut(context.Background(), testLogger{}) {
		t.Fatal("a failed payout must report it")
	}
	s.releaseStakes(context.Background(), testLogger{}) // the match closes: pays, never refunds

	if bank.coins["u0"] != 570 {
		t.Errorf("u0 has %d, want the payout (570)", bank.coins["u0"])
	}
}

func TestRecoverStakes_RefundsLostMatchesOnly(t *testing.T) {
	bank := newFakeBank(map[string]int64{"a": 0, "b": 0, "c": 0})
	bank.escrows["lost"] = heldEscrow{Version: "1", Escrow: escrow{MatchID: "lost", Stake: 50, Seats: []escrowSeat{
		{Seat: 0, UserID: "a", Paid: 50},
		{Seat: 1, UserID: "b", Paid: 50, Forfeited: true},
	}}}
	bank.escrows["live"] = heldEscrow{Version: "2", Escrow: escrow{MatchID: "live", Stake: 50, Seats: []escrowSeat{
		{Seat: 0, UserID: "c", Paid: 50},
	}}}

	recoverStakes(context.Background(), testLogger{}, bank, func(id string) bool { return id == "live" })

	if bank.coins["a"] != 50 || bank.coins["b"] != 0 || bank.coins["c"] != 0 {
		t.Errorf("coins %v; want only a refunded", bank.coins)
	}
	if _, ok := bank.escrows["live"]; !ok || len(bank.escrows) != 1 {
		t.Errorf("a live match's stakes must stay held: %v", bank.escrows)
	}
}

func TestJoinAttempt_RefusedBelowTheStake(t *testing.T) {
	bank := newFakeBank(map[string]int64{"poor": 49, "rich": 50})
	s := newLobby()
	s.bank, s.stake = bank, 50
	m := &UnoMatch{}

	_, ok, reason := m.MatchJoinAttempt(context.Background(), testLogger{}, nil, nil, nil, 0, s, fakePresence{userID: "poor"}, nil)
	if ok || reason != ErrCodeNotEnoughCoins {
		t.Errorf("poor: ok=%v reason=%q", ok, reason)
	}
	if _, ok, _ := m.MatchJoinAttempt(context.Background(), testLogger{}, nil, nil, nil, 0, s, fakePresence{userID: "rich"}, nil); !ok {
		t.Error("rich should be let in")
	}
}

var _ runtime.Presence = fakePresence{}
