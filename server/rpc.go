package main

import (
	"context"
	"database/sql"
	"encoding/json"

	"github.com/heroiclabs/nakama-common/runtime"
)

// Where a player's current match is recorded, so a relaunched client can find
// its way back. Written by the match handler, read by current_match.
const (
	matchCollection = "match"
	matchKey        = "current"
)

// currentMatchRecord is the stored value and also the reply of both RPCs.
type currentMatchRecord struct {
	MatchID string `json:"matchId,omitempty"`
	Phase   string `json:"phase,omitempty"`
}

// rpcFindMatch returns a lobby with a free seat, creating one if there is none.
// The client then joins it over the socket.
func rpcFindMatch(ctx context.Context, logger runtime.Logger, db *sql.DB, nk runtime.NakamaModule, payload string) (string, error) {
	// Authoritative matches whose label says they are still open.
	matches, err := nk.MatchList(ctx, 10, true, "", nil, nil, "+label.open:1")
	if err != nil {
		logger.WithField("error", err.Error()).Error("match list failed")
		return "", runtime.NewError("could not search for matches", 13) // INTERNAL
	}

	for _, match := range matches {
		if match.GetSize() < seatCount {
			return marshalMatchID(match.GetMatchId())
		}
	}

	matchID, err := nk.MatchCreate(ctx, matchModuleName, nil)
	if err != nil {
		logger.WithField("error", err.Error()).Error("match create failed")
		return "", runtime.NewError("could not create a match", 13)
	}

	logger.WithField("match_id", matchID).Info("created a lobby")
	return marshalMatchID(matchID)
}

// rpcCurrentMatch tells a client whether it still holds a seat somewhere, and
// in which phase, so it knows which screen to open. An empty reply means the
// home screen: no match, or a bot has taken the seat.
func rpcCurrentMatch(ctx context.Context, logger runtime.Logger, db *sql.DB, nk runtime.NakamaModule, payload string) (string, error) {
	userID, ok := ctx.Value(runtime.RUNTIME_CTX_USER_ID).(string)
	if !ok || userID == "" {
		return "{}", nil
	}

	objects, err := nk.StorageRead(ctx, []*runtime.StorageRead{{
		Collection: matchCollection,
		Key:        matchKey,
		UserID:     userID,
	}})
	if err != nil || len(objects) == 0 {
		return "{}", nil
	}

	var record currentMatchRecord
	if err := json.Unmarshal([]byte(objects[0].Value), &record); err != nil || record.MatchID == "" {
		return "{}", nil
	}

	// The match may have ended while the client was away.
	match, err := nk.MatchGet(ctx, record.MatchID)
	if err != nil || match == nil {
		clearCurrentMatch(ctx, logger, nk, userID)
		return "{}", nil
	}

	var label struct {
		Phase string `json:"phase"`
	}
	_ = json.Unmarshal([]byte(match.GetLabel().GetValue()), &label)
	record.Phase = label.Phase

	reply, err := json.Marshal(record)
	if err != nil {
		return "{}", nil
	}
	return string(reply), nil
}

// rpcResetConfig writes the shipped tunables back, undoing console edits.
func rpcResetConfig(ctx context.Context, logger runtime.Logger, db *sql.DB, nk runtime.NakamaModule, payload string) (string, error) {
	if err := WriteDefaultConfig(ctx, nk); err != nil {
		logger.WithField("error", err.Error()).Error("config reset failed")
		return "", runtime.NewError("could not write config", 13)
	}
	return `{"ok":true}`, nil
}

// ---------- storage helpers used by the match handler ----------

func writeCurrentMatch(ctx context.Context, logger runtime.Logger, nk runtime.NakamaModule, userID, matchID string) {
	value, err := json.Marshal(currentMatchRecord{MatchID: matchID})
	if err != nil {
		return
	}

	_, err = nk.StorageWrite(ctx, []*runtime.StorageWrite{{
		Collection:      matchCollection,
		Key:             matchKey,
		UserID:          userID,
		Value:           string(value),
		PermissionRead:  1, // the owner may read it
		PermissionWrite: 0, // only the server writes it
	}})
	if err != nil {
		logger.WithField("error", err.Error()).Warn("could not record current match")
	}
}

func clearCurrentMatch(ctx context.Context, logger runtime.Logger, nk runtime.NakamaModule, userID string) {
	err := nk.StorageDelete(ctx, []*runtime.StorageDelete{{
		Collection: matchCollection,
		Key:        matchKey,
		UserID:     userID,
	}})
	if err != nil {
		logger.WithField("error", err.Error()).Warn("could not clear current match")
	}
}

func marshalMatchID(matchID string) (string, error) {
	reply, err := json.Marshal(currentMatchRecord{MatchID: matchID})
	if err != nil {
		return "", runtime.NewError("could not encode reply", 13)
	}
	return string(reply), nil
}
