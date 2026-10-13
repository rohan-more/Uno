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
	w, cfg, ctx := newFakeWallet(), DefaultMatchConfig(), context.Background()

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
	w, cfg, ctx := newFakeWallet(), DefaultMatchConfig(), context.Background()

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

func TestFreeCoins_CooldownBetweenClaims(t *testing.T) {
	w, cfg, ctx := newFakeWallet(), DefaultMatchConfig(), context.Background()
	start := int64(1_000_000_000)

	info, err := claimFreeCoins(ctx, w, cfg, "u", start)
	if err != nil || !info.Claimed || info.Coins != int64(cfg.FreeCoins) {
		t.Fatalf("first claim: %+v, %v", info, err)
	}
	if info.FreeCoinsInMs != int64(cfg.FreeCoinsEveryMs) {
		t.Errorf("after claiming, next in %d ms, want %d", info.FreeCoinsInMs, cfg.FreeCoinsEveryMs)
	}

	// A minute later: refused, coins unchanged, and it says how long to wait.
	info, err = claimFreeCoins(ctx, w, cfg, "u", start+60_000)
	if err != nil || info.Claimed || info.Coins != int64(cfg.FreeCoins) {
		t.Fatalf("early claim should be refused: %+v, %v", info, err)
	}
	if want := int64(cfg.FreeCoinsEveryMs) - 60_000; info.FreeCoinsInMs != want {
		t.Errorf("early claim: next in %d ms, want %d", info.FreeCoinsInMs, want)
	}

	// Once the cooldown is over it works again.
	info, err = claimFreeCoins(ctx, w, cfg, "u", start+int64(cfg.FreeCoinsEveryMs))
	if err != nil || !info.Claimed || info.Coins != 2*int64(cfg.FreeCoins) {
		t.Fatalf("claim after the cooldown: %+v, %v", info, err)
	}
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

	for _, bad := range []int64{0, -5, maxDebugGrant + 1} {
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

func TestDebugResetFreeCoins_MakesThemClaimable(t *testing.T) {
	w, cfg, ctx := newFakeWallet(), DefaultMatchConfig(), context.Background()
	now := int64(5_000_000)

	if _, err := claimFreeCoins(ctx, w, cfg, "u", now); err != nil {
		t.Fatal(err)
	}
	if err := debugResetFreeCoins(ctx, w, "u"); err != nil {
		t.Fatal(err)
	}
	info, err := claimFreeCoins(ctx, w, cfg, "u", now+1)
	if err != nil || !info.Claimed {
		t.Errorf("after a reset, free coins should be claimable at once: %+v, %v", info, err)
	}
}
