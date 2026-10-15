package main

import (
	"context"
	"encoding/json"
	"errors"

	"github.com/heroiclabs/nakama-common/runtime"

	"github.com/rohan-more/Uno/server/game"
)

// Stakes: every seat pays in when a match starts, and the pot is paid out by
// place at game over. While a match is played the stakes are held in an
// escrow record, one per match, owned by the server:
//
//   - created in the same transaction that takes the stakes,
//   - updated when a player forfeits (a bot took their seat),
//   - deleted in the same transaction that pays out or refunds.
//
// So a record exists exactly while coins are held, and wallets and record can
// never disagree. A record still there after a restart belongs to a match that
// died with the server: recoverStakes refunds it.
const (
	escrowCollection = "escrow"

	CoinsStake  = "stake"
	CoinsPayout = "payout"
	CoinsRefund = "refund"
)

// escrow is the stored record for one staked match.
type escrow struct {
	MatchID     string       `json:"matchId"`
	Stake       int64        `json:"stake"`
	Seats       []escrowSeat `json:"seats"` // humans who paid
	CreatedAtMs int64        `json:"createdAtMs"`
}

type escrowSeat struct {
	Seat      int    `json:"seat"`
	UserID    string `json:"userId"`
	Paid      int64  `json:"paid"`
	Forfeited bool   `json:"forfeited"` // a bot took the seat: no payout, no refund
}

// heldEscrow is a stored escrow with its storage version.
type heldEscrow struct {
	Escrow  escrow
	Version string
}

// stakeBank is what the stake rules need from Nakama, so tests can use a fake.
type stakeBank interface {
	Balances(ctx context.Context, userIDs []string) (map[string]int64, error)
	// Hold creates the escrow and takes coins (amounts are positive), all or
	// nothing. Fails if any wallet is short or the record already exists.
	Hold(ctx context.Context, e escrow, take map[string]int64) (string, error)
	// Update rewrites the escrow, only if it's still at version.
	Update(ctx context.Context, e escrow, version string) (string, error)
	// Release deletes the escrow (only if still at version) and pays coins,
	// all or nothing. reason goes in the wallet ledger.
	Release(ctx context.Context, matchID, version string, pay map[string]int64, reason string) error
	// Held lists every escrow still holding stakes.
	Held(ctx context.Context) ([]heldEscrow, error)
}

// errShort means someone couldn't cover their stake when it was taken.
var errShort = errors.New("not enough coins for the stake")

// payoutShares splits the pot by place. results are in place order; seats
// that share a place pool the shares of the places they cover and split them
// evenly. Whatever doesn't divide evenly stays with the house.
func payoutShares(pot int64, percents []int, results []game.Result) map[int]int64 {
	coins := make(map[int]int64, len(results))
	for i := 0; i < len(results); {
		j := i
		for j+1 < len(results) && results[j+1].Place == results[i].Place {
			j++
		}

		var pooled int64
		for k := i; k <= j; k++ {
			if k < len(percents) {
				pooled += pot * int64(percents[k]) / 100
			}
		}
		share := pooled / int64(j-i+1)
		for k := i; k <= j; k++ {
			coins[results[k].Seat] = share
		}
		i = j + 1
	}
	return coins
}

// recoverStakes refunds every escrow whose match no longer exists: the server
// stopped or crashed mid-match. Forfeited seats stay forfeited. Run at startup.
func recoverStakes(ctx context.Context, logger runtime.Logger, bank stakeBank, matchAlive func(matchID string) bool) {
	held, err := bank.Held(ctx)
	if err != nil {
		logger.WithField("error", err.Error()).Error("could not list held stakes")
		return
	}

	for _, h := range held {
		if matchAlive(h.Escrow.MatchID) {
			continue
		}
		refund := refundsFor(h.Escrow)
		if err := bank.Release(ctx, h.Escrow.MatchID, h.Version, refund, CoinsRefund); err != nil {
			logger.WithField("match_id", h.Escrow.MatchID).WithField("error", err.Error()).Error("could not refund stakes")
			continue
		}
		logger.WithField("match_id", h.Escrow.MatchID).WithField("players", len(refund)).Info("refunded stakes of a match that was lost")
	}
}

// refundsFor gives back what each seat paid, except to seats that forfeited.
func refundsFor(e escrow) map[string]int64 {
	refund := map[string]int64{}
	for _, s := range e.Seats {
		if !s.Forfeited && s.Paid > 0 {
			refund[s.UserID] += s.Paid
		}
	}
	return refund
}

// ---------- Nakama-backed bank ----------

type nakamaBank struct {
	nk runtime.NakamaModule
}

func (b nakamaBank) Balances(ctx context.Context, userIDs []string) (map[string]int64, error) {
	out := make(map[string]int64, len(userIDs))
	for _, id := range userIDs {
		coins, err := nakamaWallet{b.nk}.Balance(ctx, id)
		if err != nil {
			return nil, err
		}
		out[id] = coins
	}
	return out, nil
}

func (b nakamaBank) write(e escrow, version string) (*runtime.StorageWrite, error) {
	value, err := json.Marshal(e)
	if err != nil {
		return nil, err
	}
	return &runtime.StorageWrite{
		Collection:      escrowCollection,
		Key:             e.MatchID,
		Value:           string(value),
		Version:         version,
		PermissionRead:  0, // server only
		PermissionWrite: 0,
	}, nil
}

func (b nakamaBank) walletUpdates(matchID string, changes map[string]int64, sign int64, reason string) []*runtime.WalletUpdate {
	updates := make([]*runtime.WalletUpdate, 0, len(changes))
	for userID, amount := range changes {
		if amount == 0 {
			continue
		}
		updates = append(updates, &runtime.WalletUpdate{
			UserID:    userID,
			Changeset: map[string]int64{walletCurrency: sign * amount},
			Metadata:  map[string]interface{}{"reason": reason, "matchId": matchID},
		})
	}
	return updates
}

func (b nakamaBank) Hold(ctx context.Context, e escrow, take map[string]int64) (string, error) {
	write, err := b.write(e, "*") // only if there's no record yet
	if err != nil {
		return "", err
	}
	acks, _, err := b.nk.MultiUpdate(ctx, nil, []*runtime.StorageWrite{write}, nil,
		b.walletUpdates(e.MatchID, take, -1, CoinsStake), true)
	if err != nil {
		var negative *runtime.WalletNegativeError
		if errors.As(err, &negative) || containsAny(err.Error(), "negative") {
			return "", errShort
		}
		return "", err
	}
	return acks[0].GetVersion(), nil
}

func (b nakamaBank) Update(ctx context.Context, e escrow, version string) (string, error) {
	write, err := b.write(e, version)
	if err != nil {
		return "", err
	}
	acks, err := b.nk.StorageWrite(ctx, []*runtime.StorageWrite{write})
	if err != nil {
		return "", err
	}
	return acks[0].GetVersion(), nil
}

func (b nakamaBank) Release(ctx context.Context, matchID, version string, pay map[string]int64, reason string) error {
	del := &runtime.StorageDelete{Collection: escrowCollection, Key: matchID, Version: version}
	_, _, err := b.nk.MultiUpdate(ctx, nil, nil, []*runtime.StorageDelete{del},
		b.walletUpdates(matchID, pay, 1, reason), true)
	return err
}

func (b nakamaBank) Held(ctx context.Context) ([]heldEscrow, error) {
	var out []heldEscrow
	cursor := ""
	for {
		objects, next, err := b.nk.StorageList(ctx, "", "", escrowCollection, 100, cursor)
		if err != nil {
			return nil, err
		}
		for _, o := range objects {
			var e escrow
			if err := json.Unmarshal([]byte(o.GetValue()), &e); err != nil {
				continue
			}
			out = append(out, heldEscrow{Escrow: e, Version: o.GetVersion()})
		}
		if next == "" {
			return out, nil
		}
		cursor = next
	}
}

// ---------- the match's side ----------

// pot is what the table plays for: every seat's stake, bots' paid by the house.
func (s *MatchState) pot() int64 { return s.stake * seatCount }

// holdStakes takes every human's stake as the match starts. Anyone who can't
// cover it is sent home and a bot takes the seat. False if nobody is left.
// If the coins can't be reached at all, the match is played for free rather
// than not at all.
func (s *MatchState) holdStakes(ctx context.Context, logger runtime.Logger, nk runtime.NakamaModule, dispatcher runtime.MatchDispatcher) bool {
	if s.stake <= 0 || s.bank == nil {
		return true
	}

	for attempt := 0; attempt < 2; attempt++ {
		var humans []string
		for _, st := range s.seats {
			if st.kind == KindHuman {
				humans = append(humans, st.userID)
			}
		}
		balances, err := s.bank.Balances(ctx, humans)
		if err != nil {
			logger.WithField("error", err.Error()).Error("could not read balances, playing this match for free")
			s.stake = 0
			return true
		}

		for i, st := range s.seats {
			if st.kind != KindHuman || balances[st.userID] >= s.stake {
				continue
			}
			s.sendRemoved(logger, dispatcher, st.presence, RemovedNotEnoughCoins)
			if st.presence != nil {
				_ = dispatcher.MatchKick([]runtime.Presence{st.presence})
			}
			clearCurrentMatch(ctx, logger, nk, st.userID)
			s.seats[i] = s.newBotSeat()
			logger.WithField("seat", i).Info("not enough coins for the stake, bot took the seat")
		}
		if s.seatedHumans() == 0 {
			s.closeRequest = true
			return false
		}

		e := escrow{MatchID: s.matchID, Stake: s.stake, CreatedAtMs: nowMs()}
		take := map[string]int64{}
		for i, st := range s.seats {
			if st.kind == KindHuman {
				e.Seats = append(e.Seats, escrowSeat{Seat: i, UserID: st.userID, Paid: s.stake})
				take[st.userID] = s.stake
			}
		}

		version, err := s.bank.Hold(ctx, e, take)
		if err == nil {
			s.escrow, s.escrowVersion = &e, version
			logger.WithField("stake", s.stake).WithField("players", len(e.Seats)).Info("stakes held")
			return true
		}
		if !errors.Is(err, errShort) {
			logger.WithField("error", err.Error()).Error("could not take stakes, playing this match for free")
			s.stake = 0
			return true
		}
		// Someone spent coins since the check: look again.
	}

	logger.Error("stakes kept falling short, playing this match for free")
	s.stake = 0
	return true
}

// forfeitStake records that a player's seat went to a bot: whatever it wins
// stays with the house, and a refund leaves them out.
func (s *MatchState) forfeitStake(ctx context.Context, logger runtime.Logger, userID string) {
	if s.escrow == nil {
		return
	}
	for i := range s.escrow.Seats {
		if s.escrow.Seats[i].UserID == userID && !s.escrow.Seats[i].Forfeited {
			s.escrow.Seats[i].Forfeited = true
			version, err := s.bank.Update(ctx, *s.escrow, s.escrowVersion)
			if err != nil {
				// Still forfeited here, so a payout or refund from this match
				// leaves them out; only a restart's refund would not.
				logger.WithField("error", err.Error()).Error("could not record a forfeit")
				return
			}
			s.escrowVersion = version
			logger.WithField("user_id", userID).Info("stake forfeited")
		}
	}
}

// payOut pays each human who still holds their seat what their place won, and
// releases the escrow. True when done (or nothing was held).
func (s *MatchState) payOut(ctx context.Context, logger runtime.Logger) bool {
	if s.escrow == nil {
		return true
	}
	pay := map[string]int64{}
	for _, es := range s.escrow.Seats {
		if !es.Forfeited {
			pay[es.UserID] += s.coinsWon[es.Seat]
		}
	}
	if err := s.bank.Release(ctx, s.matchID, s.escrowVersion, pay, CoinsPayout); err != nil {
		logger.WithField("error", err.Error()).Error("could not pay out")
		return false
	}
	s.escrow = nil
	logger.WithField("paid", pay).Info("paid out")
	return true
}

// releaseStakes settles whatever is still held when the match ends: the
// payout if the game finished, otherwise everyone's stake back except for
// seats that forfeited.
func (s *MatchState) releaseStakes(ctx context.Context, logger runtime.Logger) {
	if s.escrow == nil {
		return
	}
	if s.game != nil && s.game.Over() {
		s.payOut(ctx, logger)
		return
	}
	refund := refundsFor(*s.escrow)
	if err := s.bank.Release(ctx, s.matchID, s.escrowVersion, refund, CoinsRefund); err != nil {
		// The record stays, so the next server start refunds it.
		logger.WithField("error", err.Error()).Error("could not refund stakes")
		return
	}
	s.escrow = nil
	logger.WithField("refunded", refund).Info("match ended early, stakes refunded")
}

// resultMsgs is the results with the coins each place won, or nil before the
// game is over.
func (s *MatchState) resultMsgs() []ResultMsg {
	if s.game == nil {
		return nil
	}
	msgs := toResultMsgs(s.game.Results)
	for i := range msgs {
		msgs[i].Coins = s.coinsWon[msgs[i].Seat]
	}
	return msgs
}
