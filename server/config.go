package main

import (
	"context"
	"encoding/json"

	"github.com/heroiclabs/nakama-common/runtime"
)

// Tunables live in a storage object so they can be edited in the Nakama console
// without a rebuild. They are read when a match is created, so changes affect
// new matches only and never a game in progress.
const (
	configCollection = "config"
	configKey        = "match"
)

// MatchConfig holds every timing knob for a match. Anything missing or
// nonsensical in storage falls back to the default below.
type MatchConfig struct {
	// Lobby
	LobbyCountdownMs int   `json:"lobbyCountdownMs"` // from the first player joining
	BotJoinAtMsLeft  []int `json:"botJoinAtMsLeft"`  // bots fill empty seats at these marks
	JoinCutoffMs     int   `json:"joinCutoffMs"`     // no new players once this little time is left
	PreMatchMs       int   `json:"preMatchMs"`       // dealt table shown before the first turn

	// Turns
	TurnMs     int `json:"turnMs"`     // per turn, before the server plays it safe
	BotThinkMs int `json:"botThinkMs"` // pause before a bot acts, so moves are watchable

	// Absent players
	MissedTurnsForBot int `json:"missedTurnsForBot"` // consecutive timeouts before a bot takes over
	DisconnectBotMs   int `json:"disconnectBotMs"`   // or this long disconnected

	// Shutting down
	EmptyLobbyGraceMs int `json:"emptyLobbyGraceMs"` // how long a new lobby waits for its first player
	NoHumansCloseMs   int `json:"noHumansCloseMs"`   // close once no human has been connected this long
	PostGameCloseMs   int `json:"postGameCloseMs"`   // results screen time after GAME_OVER
}

// DefaultMatchConfig returns the values the game ships with.
func DefaultMatchConfig() MatchConfig {
	return MatchConfig{
		LobbyCountdownMs:  12000,
		BotJoinAtMsLeft:   []int{8000, 5000, 3000},
		JoinCutoffMs:      1000,
		PreMatchMs:        3000,
		TurnMs:            8000,
		BotThinkMs:        0,
		MissedTurnsForBot: 2,
		DisconnectBotMs:   15000,
		EmptyLobbyGraceMs: 10000,
		NoHumansCloseMs:   10000,
		PostGameCloseMs:   30000,
	}
}

// LoadMatchConfig reads the storage object and fills any gap with a default.
// It never fails: a missing or broken object just means default values, logged
// so a typo in the console is visible.
func LoadMatchConfig(ctx context.Context, logger runtime.Logger, nk runtime.NakamaModule) MatchConfig {
	cfg := DefaultMatchConfig()

	objects, err := nk.StorageRead(ctx, []*runtime.StorageRead{{
		Collection: configCollection,
		Key:        configKey,
	}})
	if err != nil {
		logger.WithField("error", err.Error()).Warn("config read failed, using defaults")
		return cfg
	}
	if len(objects) == 0 {
		return cfg // nothing stored yet
	}

	// Decode over a copy so a partial object keeps the other defaults.
	stored := cfg
	if err := json.Unmarshal([]byte(objects[0].Value), &stored); err != nil {
		logger.WithField("error", err.Error()).Warn("config is not valid JSON, using defaults")
		return cfg
	}

	return sanitize(stored, cfg, logger)
}

// sanitize replaces impossible values with the default and says which ones.
func sanitize(c, def MatchConfig, logger runtime.Logger) MatchConfig {
	fix := func(name string, value, fallback int, allowZero bool) int {
		if value > 0 || (allowZero && value == 0) {
			return value
		}
		logger.WithField("field", name).WithField("value", value).Warn("bad config value, using default")
		return fallback
	}

	c.LobbyCountdownMs = fix("lobbyCountdownMs", c.LobbyCountdownMs, def.LobbyCountdownMs, false)
	c.JoinCutoffMs = fix("joinCutoffMs", c.JoinCutoffMs, def.JoinCutoffMs, true)
	c.PreMatchMs = fix("preMatchMs", c.PreMatchMs, def.PreMatchMs, true)
	c.TurnMs = fix("turnMs", c.TurnMs, def.TurnMs, false)
	c.BotThinkMs = fix("botThinkMs", c.BotThinkMs, def.BotThinkMs, true)
	c.MissedTurnsForBot = fix("missedTurnsForBot", c.MissedTurnsForBot, def.MissedTurnsForBot, false)
	c.DisconnectBotMs = fix("disconnectBotMs", c.DisconnectBotMs, def.DisconnectBotMs, false)
	c.EmptyLobbyGraceMs = fix("emptyLobbyGraceMs", c.EmptyLobbyGraceMs, def.EmptyLobbyGraceMs, false)
	c.NoHumansCloseMs = fix("noHumansCloseMs", c.NoHumansCloseMs, def.NoHumansCloseMs, false)
	c.PostGameCloseMs = fix("postGameCloseMs", c.PostGameCloseMs, def.PostGameCloseMs, false)

	// Bot marks must be inside the countdown, and later marks come first.
	marks := make([]int, 0, len(c.BotJoinAtMsLeft))
	for _, m := range c.BotJoinAtMsLeft {
		if m > 0 && m < c.LobbyCountdownMs {
			marks = append(marks, m)
		} else {
			logger.WithField("mark", m).Warn("bot join mark outside the countdown, dropped")
		}
	}
	if len(marks) == 0 {
		marks = def.BotJoinAtMsLeft
	}
	for i := 1; i < len(marks); i++ { // insertion sort, descending; at most a handful
		for j := i; j > 0 && marks[j] > marks[j-1]; j-- {
			marks[j], marks[j-1] = marks[j-1], marks[j]
		}
	}
	c.BotJoinAtMsLeft = marks

	// A cutoff at or beyond the whole countdown would let nobody in.
	if c.JoinCutoffMs >= c.LobbyCountdownMs {
		logger.Warn("joinCutoffMs >= lobbyCountdownMs, using default")
		c.JoinCutoffMs = def.JoinCutoffMs
	}
	return c
}

// WriteDefaultConfig stores the shipped values, so the object exists in the
// console ready to edit. Also how you undo a bad edit.
func WriteDefaultConfig(ctx context.Context, nk runtime.NakamaModule) error {
	value, err := json.Marshal(DefaultMatchConfig())
	if err != nil {
		return err
	}

	_, err = nk.StorageWrite(ctx, []*runtime.StorageWrite{{
		Collection:      configCollection,
		Key:             configKey,
		Value:           string(value),
		PermissionRead:  2, // public: handy to inspect from a client while developing
		PermissionWrite: 0, // server only
	}})
	return err
}
