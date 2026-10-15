package main

import (
	"context"
	"encoding/json"
	"math"

	"github.com/heroiclabs/nakama-common/runtime"
)

// Tunables live in storage objects so they can be edited in the Nakama console
// without a rebuild, one object per area so each stays short:
//
//   - config/match:   lobby and turn timings, read when a match is created, so
//     changes affect new matches only and never a game in progress.
//   - config/economy: coins, read on every wallet request, so changes apply at
//     once.
//
// Stored values use units that read well (seconds with decimals, minutes) and
// say so in their names. The code works in milliseconds, converted on load.
const (
	configCollection = "config"
	configKey        = "match"
	economyKey       = "economy"
)

// ---------- match ----------

// MatchConfig holds every timing knob for a match, in milliseconds. Anything
// missing or nonsensical in storage falls back to the default below.
type MatchConfig struct {
	// Lobby
	LobbyCountdownMs int   // from the first player joining
	BotJoinAtMsLeft  []int // bots fill empty seats at these marks
	JoinCutoffMs     int   // no new players once this little time is left
	PreMatchMs       int   // dealt table shown before the first turn

	// Turns
	TurnMs     int // per turn, before the server plays it safe
	BotThinkMs int // pause before a bot acts, so moves are watchable
	TurnGapMs  int // pause after any move, so play does not race

	// Absent players
	MissedTurnsForBot int // consecutive timeouts before a bot takes over

	// Shutting down
	EmptyLobbyGraceMs int // how long a new lobby waits for its first player
	NoHumansCloseMs   int // close once no human has been connected this long
	PostGameCloseMs   int // results screen time after GAME_OVER
}

// matchConfigStored is config/match as it appears in the console.
type matchConfigStored struct {
	LobbyCountdownSeconds  float64   `json:"lobbyCountdownSeconds"`
	BotJoinAtSecondsLeft   []float64 `json:"botJoinAtSecondsLeft"`
	JoinCutoffSeconds      float64   `json:"joinCutoffSeconds"`
	PreMatchSeconds        float64   `json:"preMatchSeconds"`
	TurnSeconds            float64   `json:"turnSeconds"`
	BotThinkSeconds        float64   `json:"botThinkSeconds"`
	TurnGapSeconds         float64   `json:"turnGapSeconds"`
	MissedTurnsForBot      int       `json:"missedTurnsForBot"`
	EmptyLobbyGraceSeconds float64   `json:"emptyLobbyGraceSeconds"`
	NoHumansCloseSeconds   float64   `json:"noHumansCloseSeconds"`
	PostGameCloseSeconds   float64   `json:"postGameCloseSeconds"`
}

// DefaultMatchConfig returns the values the game ships with.
func DefaultMatchConfig() MatchConfig {
	return MatchConfig{
		LobbyCountdownMs:  12000,
		BotJoinAtMsLeft:   []int{8000, 5000, 3000},
		JoinCutoffMs:      1000,
		PreMatchMs:        3000,
		TurnMs:            8000,
		BotThinkMs:        1200,
		TurnGapMs:         500,
		MissedTurnsForBot: 3,
		EmptyLobbyGraceMs: 10000,
		NoHumansCloseMs:   10000,
		PostGameCloseMs:   30000,
	}
}

func seconds(ms int) float64 { return float64(ms) / 1000 }
func millis(s float64) int   { return int(math.Round(s * 1000)) }

func (c MatchConfig) stored() matchConfigStored {
	marks := make([]float64, len(c.BotJoinAtMsLeft))
	for i, m := range c.BotJoinAtMsLeft {
		marks[i] = seconds(m)
	}
	return matchConfigStored{
		LobbyCountdownSeconds:  seconds(c.LobbyCountdownMs),
		BotJoinAtSecondsLeft:   marks,
		JoinCutoffSeconds:      seconds(c.JoinCutoffMs),
		PreMatchSeconds:        seconds(c.PreMatchMs),
		TurnSeconds:            seconds(c.TurnMs),
		BotThinkSeconds:        seconds(c.BotThinkMs),
		TurnGapSeconds:         seconds(c.TurnGapMs),
		MissedTurnsForBot:      c.MissedTurnsForBot,
		EmptyLobbyGraceSeconds: seconds(c.EmptyLobbyGraceMs),
		NoHumansCloseSeconds:   seconds(c.NoHumansCloseMs),
		PostGameCloseSeconds:   seconds(c.PostGameCloseMs),
	}
}

func (s matchConfigStored) config() MatchConfig {
	marks := make([]int, len(s.BotJoinAtSecondsLeft))
	for i, m := range s.BotJoinAtSecondsLeft {
		marks[i] = millis(m)
	}
	return MatchConfig{
		LobbyCountdownMs:  millis(s.LobbyCountdownSeconds),
		BotJoinAtMsLeft:   marks,
		JoinCutoffMs:      millis(s.JoinCutoffSeconds),
		PreMatchMs:        millis(s.PreMatchSeconds),
		TurnMs:            millis(s.TurnSeconds),
		BotThinkMs:        millis(s.BotThinkSeconds),
		TurnGapMs:         millis(s.TurnGapSeconds),
		MissedTurnsForBot: s.MissedTurnsForBot,
		EmptyLobbyGraceMs: millis(s.EmptyLobbyGraceSeconds),
		NoHumansCloseMs:   millis(s.NoHumansCloseSeconds),
		PostGameCloseMs:   millis(s.PostGameCloseSeconds),
	}
}

// LoadMatchConfig reads config/match and fills any gap with a default. It never
// fails: a missing or broken object just means default values, logged so a
// typo in the console is visible.
func LoadMatchConfig(ctx context.Context, logger runtime.Logger, nk runtime.NakamaModule) MatchConfig {
	def := DefaultMatchConfig()

	// Decode over the defaults, so a partial object keeps the other values.
	stored := def.stored()
	if !readConfigObject(ctx, logger, nk, configKey, &stored) {
		return def
	}
	return sanitize(stored.config(), def, logger)
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

	c.LobbyCountdownMs = fix("lobbyCountdownSeconds", c.LobbyCountdownMs, def.LobbyCountdownMs, false)
	c.JoinCutoffMs = fix("joinCutoffSeconds", c.JoinCutoffMs, def.JoinCutoffMs, true)
	c.PreMatchMs = fix("preMatchSeconds", c.PreMatchMs, def.PreMatchMs, true)
	c.TurnMs = fix("turnSeconds", c.TurnMs, def.TurnMs, false)
	c.BotThinkMs = fix("botThinkSeconds", c.BotThinkMs, def.BotThinkMs, true)
	c.TurnGapMs = fix("turnGapSeconds", c.TurnGapMs, def.TurnGapMs, true)
	c.MissedTurnsForBot = fix("missedTurnsForBot", c.MissedTurnsForBot, def.MissedTurnsForBot, false)
	c.EmptyLobbyGraceMs = fix("emptyLobbyGraceSeconds", c.EmptyLobbyGraceMs, def.EmptyLobbyGraceMs, false)
	c.NoHumansCloseMs = fix("noHumansCloseSeconds", c.NoHumansCloseMs, def.NoHumansCloseMs, false)
	c.PostGameCloseMs = fix("postGameCloseSeconds", c.PostGameCloseMs, def.PostGameCloseMs, false)

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
		logger.Warn("joinCutoffSeconds >= lobbyCountdownSeconds, using default")
		c.JoinCutoffMs = def.JoinCutoffMs
	}
	return c
}

// ---------- economy ----------

// EconomyConfig holds the coin settings. Stake and payouts join in phase 3.
type EconomyConfig struct {
	StartingCoins int `json:"startingCoins"` // given once to every account

	// Free coins rescue players who can't afford a match: below FreeCoinsBelow,
	// after FreeCoinsEveryMinutes the balance is topped up to FreeCoinsUpTo.
	FreeCoinsBelow        int `json:"freeCoinsBelow"`
	FreeCoinsUpTo         int `json:"freeCoinsUpTo"`
	FreeCoinsEveryMinutes int `json:"freeCoinsEveryMinutes"`
}

// DefaultEconomyConfig returns the coin settings the game ships with.
func DefaultEconomyConfig() EconomyConfig {
	return EconomyConfig{
		StartingCoins:         500,
		FreeCoinsBelow:        50,  // one stake: can't play
		FreeCoinsUpTo:         100, // two matches
		FreeCoinsEveryMinutes: 240, // 4 hours
	}
}

// FreeCoinsEveryMillis is the free-coins wait in milliseconds, the unit the
// wallet works in.
func (c EconomyConfig) FreeCoinsEveryMillis() int64 {
	return int64(c.FreeCoinsEveryMinutes) * 60_000
}

// LoadEconomyConfig reads config/economy, with defaults for anything missing
// or impossible.
func LoadEconomyConfig(ctx context.Context, logger runtime.Logger, nk runtime.NakamaModule) EconomyConfig {
	def := DefaultEconomyConfig()
	c := def
	if !readConfigObject(ctx, logger, nk, economyKey, &c) {
		return def
	}

	if c.StartingCoins < 0 {
		logger.WithField("value", c.StartingCoins).Warn("bad startingCoins, using default")
		c.StartingCoins = def.StartingCoins
	}
	if c.FreeCoinsBelow <= 0 || c.FreeCoinsUpTo < c.FreeCoinsBelow {
		logger.WithField("below", c.FreeCoinsBelow).WithField("upTo", c.FreeCoinsUpTo).
			Warn("freeCoinsBelow must be positive and at most freeCoinsUpTo, using defaults")
		c.FreeCoinsBelow, c.FreeCoinsUpTo = def.FreeCoinsBelow, def.FreeCoinsUpTo
	}
	if c.FreeCoinsEveryMinutes <= 0 {
		logger.WithField("value", c.FreeCoinsEveryMinutes).Warn("bad freeCoinsEveryMinutes, using default")
		c.FreeCoinsEveryMinutes = def.FreeCoinsEveryMinutes
	}
	return c
}

// ---------- storage ----------

// readConfigObject decodes config/<key> over whatever into already holds.
// False when there's nothing usable stored, so the caller keeps its defaults.
func readConfigObject(ctx context.Context, logger runtime.Logger, nk runtime.NakamaModule, key string, into interface{}) bool {
	objects, err := nk.StorageRead(ctx, []*runtime.StorageRead{{
		Collection: configCollection,
		Key:        key,
	}})
	if err != nil {
		logger.WithField("key", key).WithField("error", err.Error()).Warn("config read failed, using defaults")
		return false
	}
	if len(objects) == 0 {
		return false // nothing stored yet
	}
	if err := json.Unmarshal([]byte(objects[0].Value), into); err != nil {
		logger.WithField("key", key).WithField("error", err.Error()).Warn("config is not valid JSON, using defaults")
		return false
	}
	return true
}

// WriteDefaultConfig stores the shipped values for every area, so the objects
// exist in the console ready to edit. Also how you undo a bad edit.
func WriteDefaultConfig(ctx context.Context, nk runtime.NakamaModule) error {
	match, err := json.Marshal(DefaultMatchConfig().stored())
	if err != nil {
		return err
	}
	economy, err := json.Marshal(DefaultEconomyConfig())
	if err != nil {
		return err
	}

	write := func(key string, value []byte) *runtime.StorageWrite {
		return &runtime.StorageWrite{
			Collection:      configCollection,
			Key:             key,
			Value:           string(value),
			PermissionRead:  2, // public: handy to inspect from a client while developing
			PermissionWrite: 0, // server only
		}
	}
	_, err = nk.StorageWrite(ctx, []*runtime.StorageWrite{write(configKey, match), write(economyKey, economy)})
	return err
}
