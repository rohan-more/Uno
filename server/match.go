package main

import (
	"context"
	"database/sql"
	"encoding/json"
	"errors"
	"fmt"
	"math/rand/v2"
	"time"

	"github.com/heroiclabs/nakama-common/runtime"

	"github.com/rohan-more/Uno/server/game"
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
	userID    string // the human who holds or held the seat; empty for lobby bots
	publicID  string // the user id other clients see: real for humans, made up for bots
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

	// skipLobby is set by the quick_match RPC: bots take every other seat and
	// the game starts as soon as the first player joins. For testing.
	skipLobby bool

	ageTicks      int             // ticks since the match was created
	everHadHuman  bool            // so a brand new lobby is not closed before anyone can join
	game          *game.GameState // nil until the cards are dealt
	preMatchTicks int             // dealt table shown before the first turn
	turnTicks     int             // ticks left in the current turn
	botThinkTicks int             // pause before a bot plays, so moves are watchable
	gapTicks      int             // pause after any move, so turns do not race past
	turnExtended  bool            // EXTEND_TURN already used this turn
	closeTicks    int             // ticks left before a finished match shuts down

	missedTurns  [seatCount]int // consecutive timeouts, reset by acting in time
	closeRequest bool           // set when the match should end on this tick

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

	skipLobby, _ := params["skipLobby"].(bool)

	state := &MatchState{
		matchID:        matchID,
		skipLobby:      skipLobby,
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
		// Returning to a seat we already hold: mark it connected again and
		// catch the player up with a fresh snapshot.
		if idx := s.seatOfUser(p.GetUserId()); idx >= 0 && s.seats[idx].kind == KindHuman {
			s.seats[idx].connected = true
			s.seats[idx].presence = p
			logger.WithField("seat", idx).Info("player reconnected")

			if s.phase == phasePlaying {
				s.sendGameState(logger, dispatcher, idx)
				s.broadcastEvents(logger, dispatcher, []EventMsg{connectionEvent(idx, true)})
			}
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
			publicID:  p.GetUserId(),
			name:      name,
			avatar:    avatar,
			connected: true,
			presence:  p,
		}
		writeCurrentMatch(ctx, logger, nk, p.GetUserId(), s.matchID)

		if !s.countdownStarted() {
			s.countdownTicks = s.cfg.LobbyCountdownMs / msPerTick
		}
		if s.skipLobby {
			s.countdownTicks = 0 // the next tick fills the seats and deals
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

		if s.phase == phaseLobby {
			clearCurrentMatch(ctx, logger, nk, p.GetUserId())
			s.seats[idx] = s.newBotSeat()
			logger.WithField("seat", idx).Info("player left the lobby, bot took the seat")
			continue
		}

		// During play a closing socket could be a dropped connection or a
		// player quitting; there's no telling them apart. Either way the seat
		// is held and their turns time out, and the current-match record stays
		// so their client can find its way back. Miss enough turns and a bot
		// takes over. Quitting on purpose is LEAVE_MATCH.
		if s.phase != phasePlaying {
			clearCurrentMatch(ctx, logger, nk, p.GetUserId())
		}
		s.seats[idx].connected = false
		s.seats[idx].presence = nil
		logger.WithField("seat", idx).Info("player disconnected")
		s.broadcastEvents(logger, dispatcher, []EventMsg{connectionEvent(idx, false)})
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
	} else if s.phase == phasePlaying || s.phase == phaseDone {
		s.tickPlaying(ctx, logger, nk, dispatcher)
	}

	if s.closeRequest {
		logger.Info("match finished, closing")
		return nil
	}

	s.handleMessages(ctx, logger, nk, dispatcher, messages)

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

	// Player ids are labels for the rules package; it never sees Nakama.
	playerIDs := make([]string, seatCount)
	for i, st := range s.seats {
		if st.kind == KindHuman {
			playerIDs[i] = st.userID
		} else {
			playerIDs[i] = "bot:" + st.name
		}
	}

	g, err := game.NewGame(catalog, playerIDs, s.rng.Uint64())
	if err != nil {
		logger.WithField("error", err.Error()).Error("could not deal, closing match")
		s.phase = phaseDone
		return
	}

	s.game = g
	s.phase = phasePlaying
	s.preMatchTicks = s.cfg.PreMatchMs / msPerTick
	s.labelDirty = true
	logger.WithField("humans", s.humanCount()).WithField("seed", g.Seed).Info("match starting")

	// Everyone gets their own snapshot: their hand, and counts for the rest.
	for i, st := range s.seats {
		if st.kind == KindHuman && st.presence != nil {
			s.sendGameState(logger, dispatcher, i)
		}
	}
}

// tickPlaying drives everything that happens on a clock during a match: the
// pre-match pause, bot moves, the turn timer and shutting down at the end.
func (s *MatchState) tickPlaying(ctx context.Context, logger runtime.Logger, nk runtime.NakamaModule, dispatcher runtime.MatchDispatcher) {
	if s.game == nil { // dealing failed; nothing to run
		s.closeRequest = true
		return
	}

	if s.preMatchTicks > 0 {
		s.preMatchTicks--
		if s.preMatchTicks > 0 {
			return
		}

		s.beginTurn()
		s.broadcastEvents(logger, dispatcher, []EventMsg{s.turnChangedEvent()})
		logger.WithField("seat", s.game.Current).Info("first turn")
		return
	}

	if s.game.Over() {
		s.closeTicks--
		if s.closeTicks <= 0 {
			s.closeRequest = true
		}
		return
	}

	// A short breath after every move, so clients can finish animating and
	// three bots in a row do not resolve in one frame.
	if s.gapTicks > 0 {
		s.gapTicks--
		return
	}

	// A bot seat plays by itself after a short pause.
	if s.seats[s.game.Current].kind == KindBot {
		if s.botThinkTicks > 0 {
			s.botThinkTicks--
			return
		}
		// Note: Apply advances the turn, so the seat must be captured first.
		seat := s.game.Current
		action := game.BotAction(s.game, seat, s.rng)
		s.applyAction(logger, dispatcher, seat, action, nil)
		return
	}

	// Owing cards with nothing to stack on them leaves no choice, so take them
	// now rather than make the player find the deck or wait out the clock.
	if s.game.PendingDraw > 0 && len(s.game.PlayableCards(s.game.Current)) == 0 {
		s.applyAction(logger, dispatcher, s.game.Current, game.Action{Type: game.DrawCard}, nil)
		return
	}

	// A human seat runs out of time.
	s.turnTicks--
	if s.turnTicks <= 0 {
		s.timeOutTurn(ctx, logger, nk, dispatcher)
	}
}

// timeOutTurn makes the safe move for a player who ran out of time: take any
// cards owed, otherwise draw one and end the turn. It never plays a card for
// them. Enough of these in a row and a bot takes the seat.
func (s *MatchState) timeOutTurn(ctx context.Context, logger runtime.Logger, nk runtime.NakamaModule, dispatcher runtime.MatchDispatcher) {
	seat := s.game.Current
	logger.WithField("seat", seat).Info("turn timed out")

	events := []EventMsg{{Type: EvTurnTimedOut, Seat: seat}}
	if s.game.DrawnCard != nil {
		s.applyAction(logger, dispatcher, seat, game.Action{Type: game.Pass}, events)
	} else {
		s.applyAction(logger, dispatcher, seat, game.Action{Type: game.DrawCard}, events)

		// A drawn card that could be played still ends the turn: the player was
		// not there to choose.
		if !s.game.Over() && s.game.DrawnCard != nil && s.game.Current == seat {
			s.applyAction(logger, dispatcher, seat, game.Action{Type: game.Pass}, nil)
		}
	}

	s.missedTurns[seat]++
	// Quick matches are for testing the client: sitting idle shouldn't end them.
	if !s.skipLobby && s.missedTurns[seat] >= s.cfg.MissedTurnsForBot {
		s.giveSeatToBot(ctx, logger, nk, dispatcher, seat, RemovedMissedTurns)
	}
}

// applyAction runs one move through the rules and tells everyone what happened.
// prefix events (like TURN_TIMED_OUT) are sent in the same batch.
func (s *MatchState) applyAction(logger runtime.Logger, dispatcher runtime.MatchDispatcher, seat int, action game.Action, prefix []EventMsg) {
	events, err := s.game.Apply(seat, action)
	if err != nil {
		logger.WithField("seat", seat).WithField("error", err.Error()).Warn("server move rejected")
		return
	}

	s.afterAction(logger, dispatcher, seat, events, prefix)
}

// afterAction restarts the timers and broadcasts, once a move has been applied.
func (s *MatchState) afterAction(logger runtime.Logger, dispatcher runtime.MatchDispatcher, seat int, events []game.Event, prefix []EventMsg) {
	s.beginTurn()

	batch := append(prefix, toEventMsgs(events, s.turnTicks*msPerTick, s.game.PendingDraw)...)
	for i := range batch {
		if batch[i].Type == EvDirectionChanged {
			batch[i].Direction = s.game.Direction
		}
	}
	s.broadcastEvents(logger, dispatcher, batch)

	if s.game.Over() {
		s.closeTicks = s.cfg.PostGameCloseMs / msPerTick
		s.phase = phaseDone
		s.labelDirty = true
		logger.WithField("ranking", s.game.Ranking).Info("match over")
	}
}

// beginTurn restarts the clocks for whoever is on turn now. The gap runs first,
// then the bot's thinking pause, then the turn timer.
func (s *MatchState) beginTurn() {
	s.turnTicks = s.cfg.TurnMs / msPerTick
	s.botThinkTicks = s.cfg.BotThinkMs / msPerTick
	s.gapTicks = s.cfg.TurnGapMs / msPerTick
	s.turnExtended = false
}

func (s *MatchState) turnChangedEvent() EventMsg {
	return EventMsg{
		Type:        EvTurnChanged,
		Seat:        s.game.Current,
		TurnMs:      s.cfg.TurnMs,
		PendingDraw: s.game.PendingDraw,
	}
}

// giveSeatToBot is permanent: the player is removed from the match and cannot
// rejoin, so their client falls back to the home screen.
func (s *MatchState) giveSeatToBot(ctx context.Context, logger runtime.Logger, nk runtime.NakamaModule, dispatcher runtime.MatchDispatcher, seat int, reason string) {
	if s.seats[seat].kind != KindHuman {
		return
	}

	// Tell them first, while they are still in the match to receive it.
	s.sendRemoved(logger, dispatcher, s.seats[seat].presence, reason)

	// Nobody would be left watching, so end the match instead of handing the
	// last seat to a bot and playing to an empty room.
	if s.seatedHumans() == 1 {
		logger.WithField("seat", seat).WithField("reason", reason).Info("last player gone, closing match")
		clearCurrentMatch(ctx, logger, nk, s.seats[seat].userID)
		s.closeRequest = true
		return
	}

	presence := s.seats[seat].presence
	userID := s.seats[seat].userID
	wasAway := !s.seats[seat].connected
	// The bot plays on under the same name and avatar, and nobody is told:
	// players shouldn't be able to tell a bot from a person.
	s.seats[seat].kind = KindBot
	s.seats[seat].presence = nil
	s.seats[seat].connected = true
	s.missedTurns[seat] = 0
	clearCurrentMatch(ctx, logger, nk, userID) // so their client goes home, not back here

	logger.WithField("seat", seat).WithField("reason", reason).Info("bot took over the seat")
	if wasAway {
		// The seat was shown as away; its "player" is back now.
		s.broadcastEvents(logger, dispatcher, []EventMsg{connectionEvent(seat, true)})
	}

	if presence != nil {
		if err := dispatcher.MatchKick([]runtime.Presence{presence}); err != nil {
			logger.WithField("error", err.Error()).Warn("could not kick the replaced player")
		}
	}
}

// handleMessages processes what clients sent this tick. Card play arrives with
// the next chunk of work; until then only REQUEST_STATE does anything.
func (s *MatchState) handleMessages(ctx context.Context, logger runtime.Logger, nk runtime.NakamaModule, dispatcher runtime.MatchDispatcher, messages []runtime.MatchData) {
	for _, m := range messages {
		seat := s.seatOfUser(m.GetUserId())
		if seat < 0 {
			continue
		}

		switch m.GetOpCode() {
		case OpRequestState:
			if s.phase == phasePlaying {
				s.sendGameState(logger, dispatcher, seat)
			}

		case OpPlayCard, OpDrawCard, OpPass:
			s.handleAction(logger, dispatcher, seat, m)

		case OpExtendTurn:
			s.handleExtendTurn(logger, dispatcher, seat, m)

		case OpLeaveMatch:
			// Quitting on purpose: a bot takes the seat now rather than after
			// missed turns. In the lobby, closing the socket is enough.
			if s.phase == phasePlaying {
				s.giveSeatToBot(ctx, logger, nk, dispatcher, seat, RemovedLeft)
			}

		default:
			s.sendError(logger, dispatcher, m, ErrCodeBadMessage, "unknown opcode")
		}
	}
}

// sendGameState sends one seat its personalized snapshot.
func (s *MatchState) sendGameState(logger runtime.Logger, dispatcher runtime.MatchDispatcher, seat int) {
	if s.game == nil || !s.seatValid(seat) || s.seats[seat].presence == nil {
		return
	}

	data, err := json.Marshal(s.buildGameState(seat))
	if err != nil {
		logger.WithField("error", err.Error()).Error("encode game state")
		return
	}
	err = dispatcher.BroadcastMessage(OpGameState, data, []runtime.Presence{s.seats[seat].presence}, nil, true)
	if err != nil {
		logger.WithField("error", err.Error()).Warn("send game state")
	}
}

// buildGameState is the only place that decides what a player may see: their
// own hand in full, everyone else as a card count.
func (s *MatchState) buildGameState(seat int) GameStateMsg {
	g := s.game

	msg := GameStateMsg{
		You:         seat,
		Seats:       make([]GameSeat, 0, seatCount),
		Hand:        toCardMsgs(g.Players[seat].Hand),
		TopCard:     toCardMsg(g.TopCard()),
		ActiveColor: string(g.ActiveColor),
		Direction:   g.Direction,
		CurrentSeat: g.Current,
		PendingDraw: g.PendingDraw,
		DeckCount:   g.Deck.Len(),
		DrawnCardID: -1,
		TurnMsLeft:  s.turnTicks * msPerTick,
		StartsInMs:  s.preMatchTicks * msPerTick,
		Ranking:     g.Ranking,
		Seq:         s.seq,
	}

	for i, st := range s.seats {
		msg.Seats = append(msg.Seats, GameSeat{
			Seat:      i,
			Kind:      publicKind(st.kind),
			Name:      st.name,
			Avatar:    st.avatar,
			CardCount: len(g.Players[i].Hand),
			Place:     g.Players[i].Place,
			Connected: st.kind == KindBot || st.connected,
		})
	}

	// Only the player who drew it knows which card is waiting to be played.
	if seat == g.Current && g.DrawnCard != nil {
		msg.DrawnCardID = g.DrawnCard.ID
	}
	return msg
}

// broadcastEvents sends one batch to everyone, stripping cards each player is
// not allowed to see.
func (s *MatchState) broadcastEvents(logger runtime.Logger, dispatcher runtime.MatchDispatcher, events []EventMsg) {
	if len(events) == 0 {
		return
	}
	s.seq++

	for i, st := range s.seats {
		if st.kind != KindHuman || st.presence == nil {
			continue
		}

		msg := EventsMsg{Seq: s.seq, Events: hideCards(events, i)}
		data, err := json.Marshal(msg)
		if err != nil {
			logger.WithField("error", err.Error()).Error("encode events")
			return
		}
		err = dispatcher.BroadcastMessage(OpEvents, data, []runtime.Presence{st.presence}, nil, true)
		if err != nil {
			logger.WithField("error", err.Error()).Warn("send events")
		}
	}
}

// hideCards copies the batch for one seat, removing drawn cards that belong to
// somebody else. The count stays, so everyone can animate the draw.
func hideCards(events []EventMsg, seat int) []EventMsg {
	out := make([]EventMsg, len(events))
	for i, e := range events {
		if e.Type == EvCardsDrawn && e.Seat != seat {
			e.Cards = nil
		}
		out[i] = e
	}
	return out
}

// sendError replies to one player; nothing about the match changes.
func (s *MatchState) sendError(logger runtime.Logger, dispatcher runtime.MatchDispatcher, presence runtime.Presence, code, message string) {
	data, err := json.Marshal(ErrorMsg{Code: code, Message: message})
	if err != nil {
		return
	}
	if err := dispatcher.BroadcastMessage(OpError, data, []runtime.Presence{presence}, nil, true); err != nil {
		logger.WithField("error", err.Error()).Warn("send error")
	}
}

// sendRemoved tells a player they no longer have a seat. A disconnected
// player has no presence and simply finds out on their next launch.
func (s *MatchState) sendRemoved(logger runtime.Logger, dispatcher runtime.MatchDispatcher, presence runtime.Presence, reason string) {
	if presence == nil {
		return
	}
	data, err := json.Marshal(RemovedMsg{Reason: reason})
	if err != nil {
		return
	}
	if err := dispatcher.BroadcastMessage(OpRemoved, data, []runtime.Presence{presence}, nil, true); err != nil {
		logger.WithField("error", err.Error()).Warn("send removed")
	}
}

func (s *MatchState) seatValid(seat int) bool { return seat >= 0 && seat < seatCount }

func (s *MatchState) broadcastLobby(logger runtime.Logger, dispatcher runtime.MatchDispatcher) {
	if s.phase != phaseLobby {
		return
	}

	msg := LobbyStateMsg{
		CountdownMsLeft: s.countdownMsLeft(),
		Seats:           make([]LobbySeat, 0, seatCount),
	}
	for i, seat := range s.seats {
		ls := LobbySeat{Seat: i, Kind: publicKind(seat.kind)}
		if seat.kind != KindEmpty {
			ls.UserID = seat.publicID
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

// seatedHumans counts human seats whether or not they are currently connected,
// unlike humanCount which only counts the ones present.
func (s *MatchState) seatedHumans() int {
	n := 0
	for _, seat := range s.seats {
		if seat.kind == KindHuman {
			n++
		}
	}
	return n
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
		publicID:  s.fakeUserID(),
		name:      names.GenerateUnused(s.rng, usedNames),
		avatar:    avatar,
		connected: true,
	}
}

// fakeUserID looks like a Nakama user id, so a bot's lobby seat looks like
// anyone else's.
func (s *MatchState) fakeUserID() string {
	return fmt.Sprintf("%08x-%04x-4%03x-%04x-%012x",
		s.rng.Uint32(), s.rng.Uint32()&0xffff, s.rng.Uint32()&0xfff,
		0x8000|s.rng.Uint32()&0x3fff, s.rng.Uint64()&0xffffffffffff)
}

// publicKind is the seat kind other clients are told: a bot is reported as a
// human, so nobody can tell them apart.
func publicKind(kind string) string {
	if kind == KindEmpty {
		return KindEmpty
	}
	return KindHuman
}

// label is what find_match searches over: open lobbies with room.
func (s *MatchState) label() string {
	open := 0
	if s.phase == phaseLobby && !s.skipLobby && s.freeSeat() >= 0 &&
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

// connectionEvent tells the table someone dropped or came back.
func connectionEvent(seat int, connected bool) EventMsg {
	return EventMsg{Type: EvPlayerConnection, Seat: seat, Connected: &connected}
}

// handleAction runs one client move through the rules. A rejected move changes
// nothing and the sender is told why.
func (s *MatchState) handleAction(logger runtime.Logger, dispatcher runtime.MatchDispatcher, seat int, m runtime.MatchData) {
	if s.phase != phasePlaying || s.game == nil {
		s.sendError(logger, dispatcher, m, ErrCodeGameNotStarted, "the match has not started")
		return
	}
	if s.preMatchTicks > 0 {
		s.sendError(logger, dispatcher, m, ErrCodeGameNotStarted, "the first turn has not begun")
		return
	}

	action, err := parseAction(m)
	if err != nil {
		s.sendError(logger, dispatcher, m, ErrCodeBadMessage, err.Error())
		return
	}

	events, err := s.game.Apply(seat, action)
	if err != nil {
		code, message := errorCode(err)
		s.sendError(logger, dispatcher, m, code, message)
		return
	}

	s.missedTurns[seat] = 0 // they are clearly still here
	s.afterAction(logger, dispatcher, seat, events, nil)
}

// handleExtendTurn restarts the clock for a player who drew a wild and chose to
// play it, so picking its colour gets a full turn of its own. Once per turn,
// and only in that spot, so it can't be used to stall.
func (s *MatchState) handleExtendTurn(logger runtime.Logger, dispatcher runtime.MatchDispatcher, seat int, m runtime.MatchData) {
	if s.phase != phasePlaying || s.game == nil || s.preMatchTicks > 0 || s.game.Over() {
		s.sendError(logger, dispatcher, m, ErrCodeGameNotStarted, "no turn to extend")
		return
	}
	if seat != s.game.Current {
		s.sendError(logger, dispatcher, m, ErrCodeNotYourTurn, "not your turn")
		return
	}
	if s.turnExtended || !s.game.DrawnCardIsWild() {
		s.sendError(logger, dispatcher, m, ErrCodeCannotExtend, "only once, after drawing a wild you mean to play")
		return
	}

	s.turnExtended = true
	s.turnTicks = s.cfg.TurnMs / msPerTick
	s.missedTurns[seat] = 0
	s.broadcastEvents(logger, dispatcher, []EventMsg{{Type: EvTurnExtended, Seat: seat, TurnMs: s.cfg.TurnMs}})
}

// parseAction turns one match message into a rules action.
func parseAction(m runtime.MatchData) (game.Action, error) {
	switch m.GetOpCode() {
	case OpDrawCard:
		return game.Action{Type: game.DrawCard}, nil

	case OpPass:
		return game.Action{Type: game.Pass}, nil

	case OpPlayCard:
		var req PlayCardReq
		if err := json.Unmarshal(m.GetData(), &req); err != nil {
			return game.Action{}, errors.New("could not read the play")
		}
		return game.Action{
			Type:   game.PlayCard,
			CardID: req.CardID,
			Color:  game.Color(req.Color),
		}, nil
	}
	return game.Action{}, errors.New("unknown opcode")
}

// errorCode maps a rules error to the code the client switches on. The message
// is the rules text, which is already written for a player to read.
func errorCode(err error) (string, string) {
	switch {
	case errors.Is(err, game.ErrNotYourTurn):
		return ErrCodeNotYourTurn, err.Error()
	case errors.Is(err, game.ErrCardNotInHand):
		return ErrCodeCardNotInHand, err.Error()
	case errors.Is(err, game.ErrIllegalCard):
		return ErrCodeIllegalCard, err.Error()
	case errors.Is(err, game.ErrColorRequired):
		return ErrCodeColorRequired, err.Error()
	case errors.Is(err, game.ErrMustPlayDrawnCard):
		return ErrCodeMustPlayDrawnCard, err.Error()
	case errors.Is(err, game.ErrAlreadyDrew):
		return ErrCodeAlreadyDrew, err.Error()
	case errors.Is(err, game.ErrCannotPass):
		return ErrCodeCannotPass, err.Error()
	case errors.Is(err, game.ErrGameOver):
		return ErrCodeGameOver, err.Error()
	}
	return ErrCodeBadMessage, err.Error()
}
