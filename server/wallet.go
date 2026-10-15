package main

import (
	"context"
	"database/sql"
	"encoding/json"
	"errors"
	"strings"
	"time"

	"github.com/heroiclabs/nakama-common/runtime"
)

// Coins live in each account's Nakama wallet, which only the server can change
// and which keeps a ledger of every change. Alongside it, a small server-owned
// storage record remembers one-off grants and the free-coins wait.
const (
	walletCurrency   = "coins"
	walletCollection = "wallet"
	walletStateKey   = "state"

	// debugToolsEnv turns the debug RPCs on. Set in local.yml only, so a real
	// deployment can't leave them on by accident.
	debugToolsEnv = "UNO_DEBUG_TOOLS"

	maxDebugGrant = 100000
)

// Why coins moved, written to the wallet ledger with every change.
const (
	CoinsStarting = "starting"
	CoinsFree     = "free"
	CoinsDebug    = "debug"
)

// errAlreadyChanged means another request updated the wallet state first.
var errAlreadyChanged = errors.New("wallet state changed by another request")

// walletState is the per-player record next to the wallet.
type walletState struct {
	StarterGranted bool `json:"starterGranted"`
	// FreeCoinsClockMs is when the wait for free coins began (unix ms): the
	// first time the balance was seen below freeCoinsBelow. 0 while it isn't
	// running, so time spent with enough coins is never saved up.
	FreeCoinsClockMs int64 `json:"freeCoinsClockMs"`
}

// walletStore is what the coin rules need from Nakama, so tests can use a fake.
type walletStore interface {
	Balance(ctx context.Context, userID string) (int64, error)
	// Add changes the balance by amount (may be negative) and returns the new one.
	Add(ctx context.Context, userID string, amount int64, reason string) (int64, error)
	// ReadState returns the record and its version ("" if there is none yet).
	ReadState(ctx context.Context, userID string) (walletState, string, error)
	// WriteState only succeeds if the record is still at version; otherwise
	// it returns errAlreadyChanged.
	WriteState(ctx context.Context, userID string, state walletState, version string) error
}

// WalletInfo is what the wallet RPCs return.
type WalletInfo struct {
	Coins int64 `json:"coins"`
	// FreeCoinsAdded is what this very call topped up, so the client can say so.
	FreeCoinsAdded int64 `json:"freeCoinsAdded"`
	// FreeCoinsWaiting is true while the balance is below freeCoinsBelow and a
	// top-up is on its way; FreeCoinsInMs is how long until it's due.
	FreeCoinsWaiting bool  `json:"freeCoinsWaiting"`
	FreeCoinsInMs    int64 `json:"freeCoinsInMs"`
	FreeCoinsBelow   int   `json:"freeCoinsBelow"`
	FreeCoinsUpTo    int   `json:"freeCoinsUpTo"`
	// Stake is what a match costs to enter today; below it, Play is off.
	Stake int `json:"stake"`
}

// grantStarterCoins gives an account its starting coins, once ever. It runs on
// every login, so accounts made before coins existed get them too. The flag is
// written first, against its version, so two logins at once can't both grant.
func grantStarterCoins(ctx context.Context, store walletStore, cfg EconomyConfig, userID string) (bool, error) {
	state, version, err := store.ReadState(ctx, userID)
	if err != nil {
		return false, err
	}
	if state.StarterGranted {
		return false, nil
	}

	state.StarterGranted = true
	if err := store.WriteState(ctx, userID, state, version); err != nil {
		if errors.Is(err, errAlreadyChanged) {
			return false, nil // the other login is granting them
		}
		return false, err
	}
	if _, err := store.Add(ctx, userID, int64(cfg.StartingCoins), CoinsStarting); err != nil {
		return false, err
	}
	return true, nil
}

// settleFreeCoins reads the wallet and applies the free-coins rule on the way:
//
//   - With freeCoinsBelow or more, nothing happens, and any wait is cancelled.
//   - Below it, a wait starts; once it has lasted freeCoinsEveryMinutes, the
//     balance is topped up to exactly freeCoinsUpTo.
//   - While inMatch nothing changes: a held stake makes the balance look low.
//
// Free coins are a way back into the game when you can't afford one, never a
// refill of what you lost: anything above freeCoinsUpTo is only won.
func settleFreeCoins(ctx context.Context, store walletStore, cfg EconomyConfig, userID string, nowMs int64, inMatch bool) (WalletInfo, error) {
	coins, err := store.Balance(ctx, userID)
	if err != nil {
		return WalletInfo{}, err
	}
	state, version, err := store.ReadState(ctx, userID)
	if err != nil {
		return WalletInfo{}, err
	}

	info := WalletInfo{Coins: coins, FreeCoinsBelow: cfg.FreeCoinsBelow, FreeCoinsUpTo: cfg.FreeCoinsUpTo, Stake: cfg.Stake}
	below := coins < int64(cfg.FreeCoinsBelow)
	dueAt := state.FreeCoinsClockMs + cfg.FreeCoinsEveryMillis()

	switch {
	case inMatch:
		if below && state.FreeCoinsClockMs != 0 {
			info.FreeCoinsWaiting = true
			info.FreeCoinsInMs = max(0, dueAt-nowMs)
		}
		return info, nil

	case !below:
		if state.FreeCoinsClockMs != 0 {
			state.FreeCoinsClockMs = 0
			if err := store.WriteState(ctx, userID, state, version); err != nil && !errors.Is(err, errAlreadyChanged) {
				return WalletInfo{}, err
			}
		}
		return info, nil

	case state.FreeCoinsClockMs == 0:
		// Just dropped below: start the wait. Losing a race means another
		// request started it a moment ago, which is just as good.
		state.FreeCoinsClockMs = nowMs
		if err := store.WriteState(ctx, userID, state, version); err != nil && !errors.Is(err, errAlreadyChanged) {
			return WalletInfo{}, err
		}
		info.FreeCoinsWaiting = true
		info.FreeCoinsInMs = cfg.FreeCoinsEveryMillis()
		return info, nil

	case nowMs < dueAt:
		info.FreeCoinsWaiting = true
		info.FreeCoinsInMs = dueAt - nowMs
		return info, nil
	}

	// Due. Stop the clock first, against its version, so two reads at once
	// can't both top up; the loser just reports what the winner added.
	state.FreeCoinsClockMs = 0
	if err := store.WriteState(ctx, userID, state, version); err != nil {
		if !errors.Is(err, errAlreadyChanged) {
			return WalletInfo{}, err
		}
		if info.Coins, err = store.Balance(ctx, userID); err != nil {
			return WalletInfo{}, err
		}
		return info, nil
	}

	topUp := int64(cfg.FreeCoinsUpTo) - coins
	if info.Coins, err = store.Add(ctx, userID, topUp, CoinsFree); err != nil {
		return WalletInfo{}, err
	}
	info.FreeCoinsAdded = topUp
	return info, nil
}

// debugToolsOn reports whether the debug RPCs are allowed on this server.
func debugToolsOn(ctx context.Context) bool {
	env, _ := ctx.Value(runtime.RUNTIME_CTX_ENV).(map[string]string)
	return env[debugToolsEnv] == "true"
}

// debugGrantCoins adds coins for testing, or takes them away with a negative
// amount (never below 0), e.g. to go broke and try free coins. The amount is
// capped so a typo can't wreck an account.
func debugGrantCoins(ctx context.Context, store walletStore, userID string, amount int64) error {
	if amount == 0 || amount < -maxDebugGrant || amount > maxDebugGrant {
		return errBadDebugAmount
	}
	if amount < 0 {
		coins, err := store.Balance(ctx, userID)
		if err != nil {
			return err
		}
		amount = max(amount, -coins)
		if amount == 0 {
			return nil // already at 0
		}
	}
	_, err := store.Add(ctx, userID, amount, CoinsDebug)
	return err
}

var errBadDebugAmount = errors.New("amount must be between -100000 and 100000, and not 0")

// debugFreeCoinsNow makes a waiting top-up due at once and applies it. With
// enough coins there is nothing to top up, and nothing happens.
func debugFreeCoinsNow(ctx context.Context, store walletStore, cfg EconomyConfig, userID string, nowMs int64) (WalletInfo, error) {
	coins, err := store.Balance(ctx, userID)
	if err != nil {
		return WalletInfo{}, err
	}
	if coins < int64(cfg.FreeCoinsBelow) {
		state, version, err := store.ReadState(ctx, userID)
		if err != nil {
			return WalletInfo{}, err
		}
		state.FreeCoinsClockMs = max(1, nowMs-cfg.FreeCoinsEveryMillis()) // 0 would mean "not waiting"
		if err := store.WriteState(ctx, userID, state, version); err != nil {
			return WalletInfo{}, err
		}
	}
	return settleFreeCoins(ctx, store, cfg, userID, nowMs, false)
}

// ---------- Nakama-backed store ----------

type nakamaWallet struct {
	nk runtime.NakamaModule
}

func (w nakamaWallet) Balance(ctx context.Context, userID string) (int64, error) {
	account, err := w.nk.AccountGetId(ctx, userID)
	if err != nil {
		return 0, err
	}
	var wallet map[string]int64
	if raw := account.GetWallet(); raw != "" {
		if err := json.Unmarshal([]byte(raw), &wallet); err != nil {
			return 0, err
		}
	}
	return wallet[walletCurrency], nil
}

func (w nakamaWallet) Add(ctx context.Context, userID string, amount int64, reason string) (int64, error) {
	updated, _, err := w.nk.WalletUpdate(ctx, userID,
		map[string]int64{walletCurrency: amount},
		map[string]interface{}{"reason": reason},
		true) // write the ledger entry
	if err != nil {
		return 0, err
	}
	return updated[walletCurrency], nil
}

func (w nakamaWallet) ReadState(ctx context.Context, userID string) (walletState, string, error) {
	objects, err := w.nk.StorageRead(ctx, []*runtime.StorageRead{{
		Collection: walletCollection,
		Key:        walletStateKey,
		UserID:     userID,
	}})
	if err != nil {
		return walletState{}, "", err
	}
	if len(objects) == 0 {
		return walletState{}, "", nil
	}
	var state walletState
	if err := json.Unmarshal([]byte(objects[0].Value), &state); err != nil {
		return walletState{}, "", err
	}
	return state, objects[0].Version, nil
}

func (w nakamaWallet) WriteState(ctx context.Context, userID string, state walletState, version string) error {
	value, err := json.Marshal(state)
	if err != nil {
		return err
	}
	if version == "" {
		version = "*" // only if there's still no record
	}
	_, err = w.nk.StorageWrite(ctx, []*runtime.StorageWrite{{
		Collection:      walletCollection,
		Key:             walletStateKey,
		UserID:          userID,
		Value:           string(value),
		Version:         version,
		PermissionRead:  1, // the player may look
		PermissionWrite: 0, // only the server changes it
	}})
	if err != nil && isVersionConflict(err) {
		return errAlreadyChanged
	}
	return err
}

// isVersionConflict spots Nakama's "version check failed" storage error.
func isVersionConflict(err error) bool {
	var rerr *runtime.Error
	if errors.As(err, &rerr) && rerr.Code == 6 { // ALREADY_EXISTS / version mismatch
		return true
	}
	return err != nil && containsAny(err.Error(), "version check failed", "Storage write rejected")
}

func nowMs() int64 { return time.Now().UnixMilli() }

func containsAny(s string, parts ...string) bool {
	for _, p := range parts {
		if strings.Contains(s, p) {
			return true
		}
	}
	return false
}

// ---------- RPCs ----------

// rpcGetWallet returns the balance, topping it up first if free coins are due.
func rpcGetWallet(ctx context.Context, logger runtime.Logger, db *sql.DB, nk runtime.NakamaModule, payload string) (string, error) {
	userID, err := callerID(ctx)
	if err != nil {
		return "", err
	}
	return walletReply(ctx, logger, nk, userID)
}

func rpcDebugGrantCoins(ctx context.Context, logger runtime.Logger, db *sql.DB, nk runtime.NakamaModule, payload string) (string, error) {
	userID, err := debugCaller(ctx)
	if err != nil {
		return "", err
	}
	var req struct {
		Amount int64 `json:"amount"`
	}
	if err := json.Unmarshal([]byte(payload), &req); err != nil {
		return "", runtime.NewError("expected {\"amount\": n}", 3)
	}
	if err := debugGrantCoins(ctx, nakamaWallet{nk}, userID, req.Amount); err != nil {
		if errors.Is(err, errBadDebugAmount) {
			return "", runtime.NewError(err.Error(), 3)
		}
		logger.WithField("error", err.Error()).Error("debug grant coins")
		return "", runtime.NewError("could not grant coins", 13)
	}
	logger.WithField("user_id", userID).WithField("amount", req.Amount).Warn("debug: granted coins")
	return walletReply(ctx, logger, nk, userID)
}

func rpcDebugFreeCoinsNow(ctx context.Context, logger runtime.Logger, db *sql.DB, nk runtime.NakamaModule, payload string) (string, error) {
	userID, err := debugCaller(ctx)
	if err != nil {
		return "", err
	}
	cfg := LoadEconomyConfig(ctx, logger, nk)
	info, err := debugFreeCoinsNow(ctx, nakamaWallet{nk}, cfg, userID, nowMs())
	if err != nil {
		logger.WithField("error", err.Error()).Error("debug free coins now")
		return "", runtime.NewError("could not top up", 13)
	}
	logFreeCoins(logger, userID, info)
	return marshalInfo(info)
}

// walletReply settles free coins and returns the wallet, as every wallet RPC replies.
func walletReply(ctx context.Context, logger runtime.Logger, nk runtime.NakamaModule, userID string) (string, error) {
	cfg := LoadEconomyConfig(ctx, logger, nk)
	inMatch := matchInProgress(ctx, logger, nk, userID)
	info, err := settleFreeCoins(ctx, nakamaWallet{nk}, cfg, userID, nowMs(), inMatch)
	if err != nil {
		logger.WithField("error", err.Error()).Error("read wallet")
		return "", runtime.NewError("could not read the wallet", 13)
	}
	logFreeCoins(logger, userID, info)
	return marshalInfo(info)
}

func logFreeCoins(logger runtime.Logger, userID string, info WalletInfo) {
	if info.FreeCoinsAdded > 0 {
		logger.WithField("user_id", userID).WithField("added", info.FreeCoinsAdded).Info("free coins topped up")
	}
}

func callerID(ctx context.Context) (string, error) {
	userID, ok := ctx.Value(runtime.RUNTIME_CTX_USER_ID).(string)
	if !ok || userID == "" {
		return "", runtime.NewError("must be logged in", 16)
	}
	return userID, nil
}

func debugCaller(ctx context.Context) (string, error) {
	if !debugToolsOn(ctx) {
		return "", runtime.NewError("debug tools are off on this server", 7)
	}
	return callerID(ctx)
}

func marshalInfo(info WalletInfo) (string, error) {
	reply, err := json.Marshal(info)
	if err != nil {
		return "", runtime.NewError("could not encode the wallet", 13)
	}
	return string(reply), nil
}
