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
// storage record remembers one-off grants and the free-coins clock.
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
	StarterGranted  bool  `json:"starterGranted"`
	LastFreeClaimMs int64 `json:"lastFreeClaimMs"` // unix ms; 0 means never
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
	Coins         int64 `json:"coins"`
	FreeCoins     int   `json:"freeCoins"`     // how many a claim gives
	FreeCoinsInMs int64 `json:"freeCoinsInMs"` // 0 when claimable now
	Claimed       bool  `json:"claimed,omitempty"`
}

// grantStarterCoins gives an account its starting coins, once ever. It runs on
// every login, so accounts made before coins existed get them too. The flag is
// written first, against its version, so two logins at once can't both grant.
func grantStarterCoins(ctx context.Context, store walletStore, cfg MatchConfig, userID string) (bool, error) {
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

// walletInfo reads the balance and when free coins can next be claimed.
func walletInfo(ctx context.Context, store walletStore, cfg MatchConfig, userID string, nowMs int64) (WalletInfo, error) {
	coins, err := store.Balance(ctx, userID)
	if err != nil {
		return WalletInfo{}, err
	}
	state, _, err := store.ReadState(ctx, userID)
	if err != nil {
		return WalletInfo{}, err
	}
	return WalletInfo{
		Coins:         coins,
		FreeCoins:     cfg.FreeCoins,
		FreeCoinsInMs: freeCoinsInMs(state, cfg, nowMs),
	}, nil
}

func freeCoinsInMs(state walletState, cfg MatchConfig, nowMs int64) int64 {
	if state.LastFreeClaimMs == 0 {
		return 0
	}
	left := state.LastFreeClaimMs + int64(cfg.FreeCoinsEveryMs) - nowMs
	if left < 0 {
		return 0
	}
	return left
}

// claimFreeCoins grants the free coins if the cooldown has passed. Not being
// ready isn't an error: Claimed is false and FreeCoinsInMs says how long.
func claimFreeCoins(ctx context.Context, store walletStore, cfg MatchConfig, userID string, nowMs int64) (WalletInfo, error) {
	state, version, err := store.ReadState(ctx, userID)
	if err != nil {
		return WalletInfo{}, err
	}
	if freeCoinsInMs(state, cfg, nowMs) > 0 {
		return walletInfo(ctx, store, cfg, userID, nowMs)
	}

	// Claim the slot first, so a double tap can't claim twice.
	state.LastFreeClaimMs = nowMs
	if err := store.WriteState(ctx, userID, state, version); err != nil {
		if errors.Is(err, errAlreadyChanged) {
			return walletInfo(ctx, store, cfg, userID, nowMs)
		}
		return WalletInfo{}, err
	}
	if _, err := store.Add(ctx, userID, int64(cfg.FreeCoins), CoinsFree); err != nil {
		return WalletInfo{}, err
	}

	info, err := walletInfo(ctx, store, cfg, userID, nowMs)
	info.Claimed = err == nil
	return info, err
}

// debugToolsOn reports whether the debug RPCs are allowed on this server.
func debugToolsOn(ctx context.Context) bool {
	env, _ := ctx.Value(runtime.RUNTIME_CTX_ENV).(map[string]string)
	return env[debugToolsEnv] == "true"
}

// debugGrantCoins adds coins for testing. The amount is capped so a typo can't
// wreck an account.
func debugGrantCoins(ctx context.Context, store walletStore, userID string, amount int64) error {
	if amount < 1 || amount > maxDebugGrant {
		return errBadDebugAmount
	}
	_, err := store.Add(ctx, userID, amount, CoinsDebug)
	return err
}

var errBadDebugAmount = errors.New("amount must be between 1 and 100000")

// debugResetFreeCoins makes free coins claimable right away.
func debugResetFreeCoins(ctx context.Context, store walletStore, userID string) error {
	state, version, err := store.ReadState(ctx, userID)
	if err != nil {
		return err
	}
	state.LastFreeClaimMs = 0
	return store.WriteState(ctx, userID, state, version)
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

func rpcGetWallet(ctx context.Context, logger runtime.Logger, db *sql.DB, nk runtime.NakamaModule, payload string) (string, error) {
	userID, err := callerID(ctx)
	if err != nil {
		return "", err
	}
	cfg := LoadMatchConfig(ctx, logger, nk)
	info, err := walletInfo(ctx, nakamaWallet{nk}, cfg, userID, nowMs())
	if err != nil {
		logger.WithField("error", err.Error()).Error("get wallet")
		return "", runtime.NewError("could not read the wallet", 13)
	}
	return marshalInfo(info)
}

func rpcClaimFreeCoins(ctx context.Context, logger runtime.Logger, db *sql.DB, nk runtime.NakamaModule, payload string) (string, error) {
	userID, err := callerID(ctx)
	if err != nil {
		return "", err
	}
	cfg := LoadMatchConfig(ctx, logger, nk)
	info, err := claimFreeCoins(ctx, nakamaWallet{nk}, cfg, userID, nowMs())
	if err != nil {
		logger.WithField("error", err.Error()).Error("claim free coins")
		return "", runtime.NewError("could not claim free coins", 13)
	}
	if info.Claimed {
		logger.WithField("user_id", userID).WithField("coins", info.Coins).Info("free coins claimed")
	}
	return marshalInfo(info)
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

func rpcDebugResetFreeCoins(ctx context.Context, logger runtime.Logger, db *sql.DB, nk runtime.NakamaModule, payload string) (string, error) {
	userID, err := debugCaller(ctx)
	if err != nil {
		return "", err
	}
	if err := debugResetFreeCoins(ctx, nakamaWallet{nk}, userID); err != nil {
		logger.WithField("error", err.Error()).Error("debug reset free coins")
		return "", runtime.NewError("could not reset free coins", 13)
	}
	return walletReply(ctx, logger, nk, userID)
}

func walletReply(ctx context.Context, logger runtime.Logger, nk runtime.NakamaModule, userID string) (string, error) {
	cfg := LoadMatchConfig(ctx, logger, nk)
	info, err := walletInfo(ctx, nakamaWallet{nk}, cfg, userID, nowMs())
	if err != nil {
		return "", runtime.NewError("could not read the wallet", 13)
	}
	return marshalInfo(info)
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
