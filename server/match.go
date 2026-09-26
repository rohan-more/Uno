package main

import (
	"context"
	"database/sql"
	"encoding/json"
	"math/rand/v2"
	"time"

	"github.com/heroiclabs/nakama-common/runtime"

	"github.com/rohan-more/Uno/server/names"
)

const (
	matchModuleName = "uno"
	seatCount       = 4
	tickRate        = 5 // loop runs 5x a second, so one tick is 200ms
	msPerTick       = 1000 / tickRate

	phaseLobby   = "lobby"
	phasePlaying = "playing"
	phaseDone    = "done"
)

// seat is one place at the table for the whole match: a human, a bot, or empty
// while the lobby fills. Seats are never renumbered.
type seat struct {
	kind      string // KindEmpty | KindHuman | KindBot
	userID    string
	name      string
	avatar    int
	connected bool
	presence  runtime.Presence // nil for bots and empty seats
}

// MatchState is everything the handler keeps between ticks.
type MatchState struct {
	matchID string
	cfg     MatchConfig
	rng     *rand.Rand

	phase string
	seats [seatCount]seat

	ageTicks       int  // ticks since the match was created
	everHadHuman   bool // so a brand new lobby is not closed before anyone can join
	countdownTicks int  // ticks left in the lobby; -1 until the first player joins
	nextBotMark    int  // index into cfg.BotJoinAtMsLeft
	noHumanTicks   int  // consecutive ticks with nobody connected
	labelDirty     bool // label needs republishing after seat changes

	seq int // increments with every EVENTS broadcast
}

// UnoMatch implements runtime.Match. Nakama creates one instance per match.
type UnoMatch struct{}

func (m *UnoMatch) MatchInit(ctx context.Context, logger runtime.Logger, db *sql.DB, nk runtime.NakamaModule, params map[string]interface{}) (interface{}, int, string) {
	matchID, _ := ctx.Value(runtime.RUNTIME_CTX_MATCH_ID).(string)

	state := &MatchState{
		matchID:        matchID,
		cfg:            LoadMatchConfig(ctx, logger, nk),
		rng:            rand.New(rand.NewPCG(uint64(time.Now().UnixNano()), 0)),
		phase:          phaseLobby,
		countdownTicks: -1,
	}
	for i := range state.seats {
		state.seats[i] = seat{kind: KindEmpty}
	}

	logger.WithField("match_id", matchID).Info("lobby created")
	return state, tickRate, state.label()
}

// MatchJoinAttempt decides whether someone may sit down. Anyone refused here
// gets the reason string, which the client shows before sending them home.
func (m *UnoMatch) MatchJoinAttempt(ctx context.Context, logger runtime.Logger, db *sql.DB, nk runtime.NakamaModule, dispatcher runtime.MatchDispatcher, tick int64, state interface{}, presence runtime.Presence, metadata map[string]string) (interface{}, bool, string) {
	s := state.(*MatchState)

	// Reconnecting to a seat you still hold is always allowed.
	if seatIdx := s.seatOfUser(presence.GetUserId()); seatIdx >= 0 {
		return s, true, ""
	}
	if s.phase != phaseLobby {
		return s, false, "match already started"
	}
	if s.countdownStarted() && s.countdownMsLeft() <= s.cfg.JoinCutoffMs {
		return s, false, "lobby is closing"
	}
	if s.freeSeat() < 0 {
		return s, false, "match is full"
	}
	return s, true, ""
}

// MatchJoin seats each new arrival and tells everyone.
func (m *UnoMatch) MatchJoin(ctx context.Context, logger runtime.Logger, db *sql.DB, nk runtime.NakamaModule, dispatcher runtime.MatchDispatcher, tick int64, state interface{}, presences []runtime.Presence) interface{} {
	s := state.(*MatchState)

	for _, p := range presences {
		// Returning to a seat we already hold: just mark it connected again.
		if idx := s.seatOfUser(p.GetUserId()); idx >= 0 && s.seats[idx].kind == KindHuman {
			s.seats[idx].connected = true
			s.seats[idx].presence = p
			continue
		}

		idx := s.freeSeat()
		if idx < 0 {
			logger.WithField("user_id", p.GetUserId()).Warn("joined with no seat free")
			continue
		}

		name, avatar := profileOf(ctx, logger, nk, p.GetUserId())
		s.seats[idx] = seat{
			kind:      KindHuman,
			userID:    p.GetUserId(),
			name:      name,
			avatar:    avatar,
			connected: true,
			presence:  p,
		}
		writeCurrentMatch(ctx, logger, nk, p.GetUserId(), s.matchID)

		if !s.countdownStarted() {
			s.countdownTicks = s.cfg.LobbyCountdownMs / msPerTick
		}
		logger.WithField("seat", idx).WithField("name", name).Info("player seated")
	}

	s.labelDirty = true
	s.broadcastLobby(logger, dispatcher)
	return s
}

// MatchLeave frees the seat. In the lobby a bot takes over at once so the table
// stays full, and the seat can still be claimed by a later human.
func (m *UnoMatch) MatchLeave(ctx context.Context, logger runtime.Logger, db *sql.DB, nk runtime.NakamaModule, dispatcher runtime.MatchDispatcher, tick int64, state interface{}, presences []runtime.Presence) interface{} {
	s := state.(*MatchState)

	for _, p := range presences {
		idx := s.seatOfUser(p.GetUserId())
		if idx < 0 {
			continue
		}
		clearCurrentMatch(ctx, logger, nk, p.GetUserId())

		if s.phase == phaseLobby {
			s.seats[idx] = s.newBotSeat()
			logger.WithField("seat", idx).Info("player left the lobby, bot took the seat")
			continue
		}

		// TODO: during play, hand the seat to a bot permanently and broadcast
		// SEAT_CONTROL. Comes with the game half of the handler.
		s.seats[idx].connected = false
		s.seats[idx].presence = nil
	}

	s.labelDirty = true
	s.broadcastLobby(logger, dispatcher)
	return s
}

// MatchLoop runs tickRate times a second: it drives the countdown, drops bots
// into empty seats and starts the match.
func (m *UnoMatch) MatchLoop(ctx context.Context, logger runtime.Logger, db *sql.DB, nk runtime.NakamaModule, dispatcher runtime.MatchDispatcher, tick int64, state interface{}, messages []runtime.MatchData) interface{} {
	s := state.(*MatchState)

	s.ageTicks++

	// An empty match should not linger, but a lobby nobody has joined YET must
	// survive long enough for the client that just created it to join.
	if s.humanCount() == 0 {
		s.noHumanTicks++
		switch {
		case !s.everHadHuman:
			if s.ageTicks*msPerTick >= s.cfg.EmptyLobbyGraceMs {
				logger.Info("nobody joined, closing lobby")
				return nil
			}
		case s.phase == phaseLobby:
			logger.Info("last player left the lobby, closing match")
			return nil
		case s.noHumanTicks*msPerTick >= s.cfg.NoHumansCloseMs:
			logger.Info("no humans left, closing match")
			return nil
		}
	} else {
		s.noHumanTicks = 0
		s.everHadHuman = true
	}

	if s.phase == phaseLobby {
		s.tickLobby(logger, dispatcher)
	}

	// TODO: the playing phase (turn timers, actions, bots) is the next chunk of
	// work; incoming messages are ignored until then.

	if s.labelDirty {
		if err := dispatcher.MatchLabelUpdate(s.label()); err != nil {
			logger.WithField("error", err.Error()).Warn("label update failed")
		}
		s.labelDirty = false
	}
	return s
}

func (m *UnoMatch) MatchTerminate(ctx context.Context, logger runtime.Logger, db *sql.DB, nk runtime.NakamaModule, dispatcher runtime.MatchDispatcher, tick int64, state interface{}, graceSeconds int) interface{} {
	s := state.(*MatchState)
	for _, seat := range s.seats {
		if seat.kind == KindHuman {
			clearCurrentMatch(ctx, logger, nk, seat.userID)
		}
	}
	return s
}

func (m *UnoMatch) MatchSignal(ctx context.Context, logger runtime.Logger, db *sql.DB, nk runtime.NakamaModule, dispatcher runtime.MatchDispatcher, tick int64, state interface{}, data string) (interface{}, string) {
	return state, ""
}

// ---------- lobby ----------

// tickLobby counts down, fills seats with bots at the configured marks, and
// starts the match when the table is full or time runs out.
func (s *MatchState) tickLobby(logger runtime.Logger, dispatcher runtime.MatchDispatcher) {
	if !s.countdownStarted() {
		return // waiting for the first player
	}

	s.countdownTicks--
	changed := false

	// Bots fill empty seats at, say, 8s / 5s / 3s remaining.
	for s.nextBotMark < len(s.cfg.BotJoinAtMsLeft) &&
		s.countdownMsLeft() <= s.cfg.BotJoinAtMsLeft[s.nextBotMark] {
		s.nextBotMark++

		if idx := s.emptySeat(); idx >= 0 {
			s.seats[idx] = s.newBotSeat()
			changed = true
			logger.WithField("seat", idx).Info("bot joined")
		}
	}

	// A full table of humans doesn't wait for the clock.
	if s.humanCount() == seatCount {
		s.countdownTicks = 0
	}

	if changed {
		s.labelDirty = true
	}
	if changed || s.countdownTicks%tickRate == 0 { // once a second, or on a change
		s.broadcastLobby(logger, dispatcher)
	}

	if s.countdownTicks <= 0 {
		s.startMatch(logger, dispatcher)
	}
}

// startMatch closes the lobby and moves to the playing phase.
func (s *MatchState) startMatch(logger runtime.Logger, dispatcher runtime.MatchDispatcher) {
	// Any seat still empty becomes a bot: a match is always four players.
	for i := range s.seats {
		if s.seats[i].kind == KindEmpty {
			s.seats[i] = s.newBotSeat()
		}
	}

	s.phase = phasePlaying
	s.labelDirty = true
	logger.WithField("humans", s.humanCount()).Info("match starting")

	// TODO: deal with game.NewGame and send each seat its GAME_STATE. Until the
	// game half exists the match simply sits in the playing phase.
}

func (s *MatchState) broadcastLobby(logger runtime.Logger, dispatcher runtime.MatchDispatcher) {
	if s.phase != phaseLobby {
		return
	}

	msg := LobbyStateMsg{
		CountdownMsLeft: s.countdownMsLeft(),
		Seats:           make([]LobbySeat, 0, seatCount),
	}
	for i, seat := range s.seats {
		ls := LobbySeat{Seat: i, Kind: seat.kind}
		if seat.kind != KindEmpty {
			ls.UserID = seat.userID
			ls.Name = seat.name
			ls.Avatar = seat.avatar
			ls.Connected = seat.kind == KindBot || seat.connected
		}
		msg.Seats = append(msg.Seats, ls)
	}

	data, err := json.Marshal(msg)
	if err != nil {
		logger.WithField("error", err.Error()).Error("encode lobby state")
		return
	}
	if err := dispatcher.BroadcastMessage(OpLobbyState, data, nil, nil, true); err != nil {
		logger.WithField("error", err.Error()).Warn("broadcast lobby state")
	}
}

// ---------- seat helpers ----------

func (s *MatchState) countdownStarted() bool { return s.countdownTicks >= 0 }

func (s *MatchState) countdownMsLeft() int {
	if s.countdownTicks < 0 {
		return s.cfg.LobbyCountdownMs
	}
	return s.countdownTicks * msPerTick
}

func (s *MatchState) seatOfUser(userID string) int {
	for i, seat := range s.seats {
		if seat.kind == KindHuman && seat.userID == userID {
			return i
		}
	}
	return -1
}

func (s *MatchState) emptySeat() int {
	for i, seat := range s.seats {
		if seat.kind == KindEmpty {
			return i
		}
	}
	return -1
}

// freeSeat returns where the next human sits: an empty seat if there is one,
// otherwise the lowest bot seat, which the human takes over.
func (s *MatchState) freeSeat() int {
	if idx := s.emptySeat(); idx >= 0 {
		return idx
	}
	for i, seat := range s.seats {
		if seat.kind == KindBot {
			return i
		}
	}
	return -1
}

func (s *MatchState) humanCount() int {
	n := 0
	for _, seat := range s.seats {
		if seat.kind == KindHuman && seat.connected {
			n++
		}
	}
	return n
}

// newBotSeat makes a bot that looks like a player: a generated name, and an
// avatar nobody else at the table is using.
func (s *MatchState) newBotSeat() seat {
	usedNames := map[string]bool{}
	usedAvatars := map[int]bool{}
	for _, st := range s.seats {
		if st.kind != KindEmpty {
			usedNames[st.name] = true
			usedAvatars[st.avatar] = true
		}
	}

	avatar := s.rng.IntN(avatarCount)
	for i := 0; i < avatarCount && usedAvatars[avatar]; i++ {
		avatar = (avatar + 1) % avatarCount
	}

	return seat{
		kind:      KindBot,
		name:      names.GenerateUnused(s.rng, usedNames),
		avatar:    avatar,
		connected: true,
	}
}

// label is what find_match searches over: open lobbies with room.
func (s *MatchState) label() string {
	open := 0
	if s.phase == phaseLobby && s.freeSeat() >= 0 &&
		(!s.countdownStarted() || s.countdownMsLeft() > s.cfg.JoinCutoffMs) {
		open = 1
	}

	label, err := json.Marshal(map[string]interface{}{
		"open":   open,
		"phase":  s.phase,
		"humans": s.humanCount(),
	})
	if err != nil {
		return `{"open":0}`
	}
	return string(label)
}

// profileOf reads the display name and avatar index the login hook assigned.
func profileOf(ctx context.Context, logger runtime.Logger, nk runtime.NakamaModule, userID string) (string, int) {
	account, err := nk.AccountGetId(ctx, userID)
	if err != nil {
		logger.WithField("error", err.Error()).Warn("could not read account")
		return "Player", 0
	}

	name := account.GetUser().GetDisplayName()
	if name == "" {
		name = account.GetUser().GetUsername()
	}

	var meta struct {
		Avatar int `json:"avatar"`
	}
	if raw := account.GetUser().GetMetadata(); raw != "" {
		_ = json.Unmarshal([]byte(raw), &meta)
	}
	return name, meta.Avatar
}
