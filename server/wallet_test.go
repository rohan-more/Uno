package main

import (
	"context"
	"errors"
	"strconv"
	"testing"

	"github.com/heroiclabs/nakama-common/runtime"
)

// fakeWallet keeps balances and wallet records in memory, with the same
// version check Nakama's storage does.
type fakeWallet struct {
	coins    map[string]int64
	state    map[string]walletState
	versions map[string]int
	ledger   []string // reasons, in order
}

func newFakeWallet() *fakeWallet {
	return &fakeWallet{coins: map[string]int64{}, state: map[string]walletState{}, versions: map[string]int{}}
}

func (f *fakeWallet) Balance(ctx context.Context, userID string) (int64, error) {
	return f.coins[userID], nil
}

func (f *fakeWallet) Add(ctx context.Context, userID string, amount int64, reason string) (int64, error) {
	f.coins[userID] += amount
	f.ledger = append(f.ledger, reason)
	return f.coins[userID], nil
}

func (f *fakeWallet) ReadState(ctx context.Context, userID string) (walletState, string, error) {
	v, ok := f.versions[userID]
	if !ok {
		return walletState{}, "", nil
	}
	return f.state[userID], strconv.Itoa(v), nil
}

func (f *fakeWallet) WriteState(ctx context.Context, userID string, state walletState, version string) error {
	current, exists := f.versions[userID]
	if (version == "" && exists) || (version != "" && version != strconv.Itoa(current)) {
		return errAlreadyChanged
	}
	f.state[userID] = state
	f.versions[userID] = current + 1
	return nil
}

func TestStarterCoins_GrantedOnceEver(t *testing.T) {
	w, cfg, ctx := newFakeWallet(), DefaultEconomyConfig(), context.Background()

	for login := 0; login < 3; login++ {
		if _, err := grantStarterCoins(ctx, w, cfg, "u"); err != nil {
			t.Fatal(err)
		}
	}

	if w.coins["u"] != int64(cfg.StartingCoins) {
		t.Errorf("coins = %d, want %d after three logins", w.coins["u"], cfg.StartingCoins)
	}
}

func TestStarterCoins_RacingLoginsGrantOnce(t *testing.T) {
	w, cfg, ctx := newFakeWallet(), DefaultEconomyConfig(), context.Background()

	// Two logins read "not granted" at the same time; only one write can win.
	_, versionA, _ := w.ReadState(ctx, "u")
	_, versionB, _ := w.ReadState(ctx, "u")
	if err := w.WriteState(ctx, "u", walletState{StarterGranted: true}, versionA); err != nil {
		t.Fatal(err)
	}
	if err := w.WriteState(ctx, "u", walletState{StarterGranted: true}, versionB); !errors.Is(err, errAlreadyChanged) {
		t.Fatalf("second racing write should be refused, got %v", err)
	}

	// And grantStarterCoins treats the lost race as "someone else is granting".
	if granted, err := grantStarterCoins(ctx, w, cfg, "u"); err != nil || granted {
		t.Errorf("granted=%v err=%v, want no second grant", granted, err)
	}
}

func TestFreeCoins_NothingWithEnoughToPlay(t *testing.T) {
	w, cfg, ctx := newFakeWallet(), DefaultEconomyConfig(), context.Background()
	w.coins["u"] = int64(cfg.FreeCoinsBelow) // exactly one stake: can still play

	info, err := settleFreeCoins(ctx, w, cfg, "u", 1_000, false)
	if err != nil || info.FreeCoinsWaiting || info.FreeCoinsAdded != 0 {
		t.Fatalf("with enough to play nothing should happen: %+v, %v", info, err)
	}

	// Days later, still nothing: time with enough coins is never saved up.
	info, _ = settleFreeCoins(ctx, w, cfg, "u", 1_000+10*cfg.FreeCoinsEveryMillis(), false)
	if info.Coins != int64(cfg.FreeCoinsBelow) || len(w.ledger) != 0 {
		t.Errorf("coins = %d, ledger %v; want no free coins", info.Coins, w.ledger)
	}
}

func TestFreeCoins_TopUpAfterTheWait(t *testing.T) {
	w, cfg, ctx := newFakeWallet(), DefaultEconomyConfig(), context.Background()
	w.coins["u"] = 30
	start := int64(1_000_000_000)

	// Seen below the line: the wait starts.
	info, err := settleFreeCoins(ctx, w, cfg, "u", start, false)
	if err != nil || !info.FreeCoinsWaiting || info.FreeCoinsInMs != cfg.FreeCoinsEveryMillis() {
		t.Fatalf("wait should start: %+v, %v", info, err)
	}

	// An hour in: still waiting, nothing added.
	info, _ = settleFreeCoins(ctx, w, cfg, "u", start+3_600_000, false)
	if want := cfg.FreeCoinsEveryMillis() - 3_600_000; info.FreeCoinsInMs != want || info.Coins != 30 {
		t.Fatalf("an hour in: %+v, want %d ms left and 30 coins", info, want)
	}

	// Due: topped up to exactly freeCoinsUpTo, once.
	info, _ = settleFreeCoins(ctx, w, cfg, "u", start+cfg.FreeCoinsEveryMillis(), false)
	if info.Coins != int64(cfg.FreeCoinsUpTo) || info.FreeCoinsAdded != int64(cfg.FreeCoinsUpTo)-30 || info.FreeCoinsWaiting {
		t.Fatalf("top-up: %+v, want %d coins", info, cfg.FreeCoinsUpTo)
	}
	info, _ = settleFreeCoins(ctx, w, cfg, "u", start+cfg.FreeCoinsEveryMillis()+1, false)
	if info.FreeCoinsAdded != 0 || len(w.ledger) != 1 || w.ledger[0] != CoinsFree {
		t.Errorf("the top-up must happen once, ledger %v", w.ledger)
	}
}

func TestFreeCoins_LongAbsenceStillOnlyTopsUpToTheLine(t *testing.T) {
	w, cfg, ctx := newFakeWallet(), DefaultEconomyConfig(), context.Background()

	settleFreeCoins(ctx, w, cfg, "u", 1, false) // broke at 0, wait starts
	info, _ := settleFreeCoins(ctx, w, cfg, "u", 1+20*cfg.FreeCoinsEveryMillis(), false)
	if info.Coins != int64(cfg.FreeCoinsUpTo) {
		t.Errorf("after a long absence coins = %d, want %d", info.Coins, cfg.FreeCoinsUpTo)
	}
}

func TestFreeCoins_GettingBackAboveTheLineCancelsTheWait(t *testing.T) {
	w, cfg, ctx := newFakeWallet(), DefaultEconomyConfig(), context.Background()
	w.coins["u"] = 10
	start := int64(1_000_000)

	settleFreeCoins(ctx, w, cfg, "u", start, false) // wait starts
	w.coins["u"] = 200                              // e.g. won, or bought
	settleFreeCoins(ctx, w, cfg, "u", start+1, false)

	// Broke again much later: a fresh wait, not one that's already due.
	w.coins["u"] = 10
	info, _ := settleFreeCoins(ctx, w, cfg, "u", start+2*cfg.FreeCoinsEveryMillis(), false)
	if info.FreeCoinsAdded != 0 || info.FreeCoinsInMs != cfg.FreeCoinsEveryMillis() {
		t.Errorf("the old wait should have been cancelled: %+v", info)
	}
}

func TestFreeCoins_NothingChangesDuringAMatch(t *testing.T) {
	w, cfg, ctx := newFakeWallet(), DefaultEconomyConfig(), context.Background()
	w.coins["u"] = 10 // e.g. the stake is held

	info, _ := settleFreeCoins(ctx, w, cfg, "u", 1_000, true)
	if info.FreeCoinsWaiting || w.state["u"].FreeCoinsClockMs != 0 {
		t.Errorf("a match must not start the wait: %+v", info)
	}

	settleFreeCoins(ctx, w, cfg, "u", 1_000, false) // home: wait starts
	info, _ = settleFreeCoins(ctx, w, cfg, "u", 1_000+cfg.FreeCoinsEveryMillis(), true)
	if info.FreeCoinsAdded != 0 || info.Coins != 10 {
		t.Errorf("a due top-up must wait for the match to end: %+v", info)
	}
}

func TestFreeCoins_RacingReadsTopUpOnce(t *testing.T) {
	w, cfg, ctx := newFakeWallet(), DefaultEconomyConfig(), context.Background()
	settleFreeCoins(ctx, w, cfg, "u", 1, false) // broke at 0, wait starts

	// Another read stops the clock between this one's read and write.
	racing := &racingWallet{fakeWallet: w, beforeWrite: func() {
		state, version, _ := w.ReadState(ctx, "u")
		state.FreeCoinsClockMs = 0
		w.WriteState(ctx, "u", state, version)
		w.Add(ctx, "u", int64(cfg.FreeCoinsUpTo), CoinsFree)
	}}
	info, err := settleFreeCoins(ctx, racing, cfg, "u", 1+cfg.FreeCoinsEveryMillis(), false)
	if err != nil || info.FreeCoinsAdded != 0 || w.coins["u"] != int64(cfg.FreeCoinsUpTo) {
		t.Errorf("only one top-up should land: %+v, coins %d, %v", info, w.coins["u"], err)
	}
}

// racingWallet runs beforeWrite once, just before the first state write, to
// stand in for another request getting there first.
type racingWallet struct {
	*fakeWallet
	beforeWrite func()
}

func (r *racingWallet) WriteState(ctx context.Context, userID string, state walletState, version string) error {
	if r.beforeWrite != nil {
		race := r.beforeWrite
		r.beforeWrite = nil
		race()
	}
	return r.fakeWallet.WriteState(ctx, userID, state, version)
}

func TestDebugTools_OffUnlessTheServerSaysSo(t *testing.T) {
	if debugToolsOn(context.Background()) {
		t.Error("debug tools must be off by default")
	}

	on := context.WithValue(context.Background(), runtime.RUNTIME_CTX_ENV, map[string]string{debugToolsEnv: "true"})
	if !debugToolsOn(on) {
		t.Error("debug tools should be on when the server env says so")
	}

	if _, err := debugCaller(context.Background()); err == nil {
		t.Error("debug RPCs must refuse calls while debug tools are off")
	}
}

func TestDebugGrant_AmountIsBounded(t *testing.T) {
	w, ctx := newFakeWallet(), context.Background()

	for _, bad := range []int64{0, -maxDebugGrant - 1, maxDebugGrant + 1} {
		if err := debugGrantCoins(ctx, w, "u", bad); !errors.Is(err, errBadDebugAmount) {
			t.Errorf("amount %d: err = %v, want errBadDebugAmount", bad, err)
		}
	}
	if err := debugGrantCoins(ctx, w, "u", 250); err != nil || w.coins["u"] != 250 {
		t.Errorf("grant 250: coins=%d err=%v", w.coins["u"], err)
	}
	if w.ledger[len(w.ledger)-1] != CoinsDebug {
		t.Errorf("ledger reason = %q, want %q", w.ledger[len(w.ledger)-1], CoinsDebug)
	}
}

func TestDebugGrant_TakingAwayStopsAtZero(t *testing.T) {
	w, ctx := newFakeWallet(), context.Background()
	w.coins["u"] = 120

	if err := debugGrantCoins(ctx, w, "u", -500); err != nil || w.coins["u"] != 0 {
		t.Errorf("take 500 of 120: coins=%d err=%v, want 0", w.coins["u"], err)
	}
}

func TestDebugFreeCoinsNow_TopsUpOnlyWhenBelowTheLine(t *testing.T) {
	w, cfg, ctx := newFakeWallet(), DefaultEconomyConfig(), context.Background()
	now := int64(1_790_000_000_000) // a real unix-ms time

	w.coins["u"] = 300
	info, err := debugFreeCoinsNow(ctx, w, cfg, "u", now)
	if err != nil || info.FreeCoinsAdded != 0 || w.coins["u"] != 300 {
		t.Errorf("with enough coins nothing should happen: %+v, %v", info, err)
	}

	w.coins["u"] = 20
	info, err = debugFreeCoinsNow(ctx, w, cfg, "u", now)
	if err != nil || w.coins["u"] != int64(cfg.FreeCoinsUpTo) || info.FreeCoinsAdded != int64(cfg.FreeCoinsUpTo)-20 {
		t.Errorf("below the line it should top up at once: %+v, coins %d, %v", info, w.coins["u"], err)
	}
}
