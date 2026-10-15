package main

import "github.com/rohan-more/Uno/server/game"

// Opcodes, mirrored in the client's UnoOpCodes.cs and PROTOCOL.md.
const (
	// client -> server
	OpPlayCard     int64 = 2
	OpDrawCard     int64 = 3
	OpPass         int64 = 4
	OpRequestState int64 = 5
	OpExtendTurn   int64 = 6
	OpLeaveMatch   int64 = 7

	// client -> server, testing only: refused unless UNO_DEBUG_TOOLS=true
	OpDebugWin     int64 = 90
	OpDebugOneCard int64 = 91

	// server -> client
	OpLobbyState int64 = 100
	OpGameState  int64 = 101
	OpEvents     int64 = 102
	OpError      int64 = 103
	OpRemoved    int64 = 104
)

// Seat kinds as the client sees them.
const (
	KindEmpty = "empty"
	KindHuman = "human"
	KindBot   = "bot"
)

// ---------- server -> client ----------

// LobbySeat is one seat while the table is filling up.
type LobbySeat struct {
	Seat      int    `json:"seat"`
	Kind      string `json:"kind"`             // empty | human | bot
	UserID    string `json:"userId,omitempty"` // humans only
	Name      string `json:"name,omitempty"`
	Avatar    int    `json:"avatar,omitempty"`
	Connected bool   `json:"connected,omitempty"`
}

// LobbyStateMsg (op 100) is broadcast whenever the lobby changes.
type LobbyStateMsg struct {
	CountdownMsLeft int         `json:"countdownMsLeft"`
	Seats           []LobbySeat `json:"seats"`
}

// CardMsg is a card the receiver is allowed to see.
type CardMsg struct {
	ID    int    `json:"id"`
	DefID string `json:"defId"`
}

// GameSeat is one seat during play. Hands are never included here; everyone
// else is a card count.
type GameSeat struct {
	Seat      int    `json:"seat"`
	Kind      string `json:"kind"`
	Name      string `json:"name"`
	CardCount int    `json:"cardCount"`
	Place     int    `json:"place"` // 0 while still playing
	Avatar    int    `json:"avatar"`
	Connected bool   `json:"connected"`
}

// GameStateMsg (op 101) is the personalized snapshot: sent at the start of a
// match, on reconnect, and in reply to REQUEST_STATE.
type GameStateMsg struct {
	You         int         `json:"you"`
	Seats       []GameSeat  `json:"seats"`
	Hand        []CardMsg   `json:"hand"`
	TopCard     CardMsg     `json:"topCard"`
	ActiveColor string      `json:"activeColor"`
	Direction   int         `json:"direction"`
	CurrentSeat int         `json:"currentSeat"`
	PendingDraw int         `json:"pendingDraw"`
	DrawnCardID int         `json:"drawnCardId"` // -1 when there is none
	DeckCount   int         `json:"deckCount"`
	TurnMsLeft  int         `json:"turnMsLeft"`
	StartsInMs  int         `json:"startsInMs,omitempty"` // only before the first turn
	Ranking     []int       `json:"ranking"`
	Results     []ResultMsg `json:"results,omitempty"` // once the game is over
	Stake       int64       `json:"stake"`             // each seat's stake; 0 for a free match
	Pot         int64       `json:"pot"`
	Seq         int         `json:"seq"`
}

// ResultMsg is one seat's line once the game is over, in place order.
type ResultMsg struct {
	Seat      int       `json:"seat"`
	Place     int       `json:"place"` // 1 = winner; tied seats share a place
	CardsLeft int       `json:"cardsLeft"`
	Penalty   int       `json:"penalty"`
	Coins     int64     `json:"coins"` // what this place won; a bot's is for show
	Cards     []CardMsg `json:"cards"` // what they were left holding
}

// EventMsg is one thing that happened. Irrelevant fields are omitted, and
// Cards is filled in only for the player who drew them.
type EventMsg struct {
	Type        string      `json:"type"`
	Seat        int         `json:"seat,omitempty"`
	Card        *CardMsg    `json:"card,omitempty"`
	Cards       []CardMsg   `json:"cards,omitempty"`
	Count       int         `json:"count,omitempty"`
	Color       string      `json:"color,omitempty"`
	Direction   int         `json:"direction,omitempty"`
	TurnMs      int         `json:"turnMs,omitempty"`
	PendingDraw int         `json:"pendingDraw,omitempty"`
	Kind        string      `json:"kind,omitempty"`      // SEAT_CONTROL
	Connected   *bool       `json:"connected,omitempty"` // PLAYER_CONNECTION
	Ranking     []int       `json:"ranking,omitempty"`   // GAME_OVER
	Results     []ResultMsg `json:"results,omitempty"`   // GAME_OVER
	Stake       int64       `json:"stake,omitempty"`     // GAME_OVER
	Pot         int64       `json:"pot,omitempty"`       // GAME_OVER
}

// EventsMsg (op 102) carries everything one action caused, in order.
type EventsMsg struct {
	Seq    int        `json:"seq"`
	Events []EventMsg `json:"events"`
}

// ErrorMsg (op 103) goes only to the client whose action was rejected.
type ErrorMsg struct {
	Code    string `json:"code"`
	Message string `json:"message"`
}

// RemovedMsg (op 104) goes only to a player who just lost their seat, right
// before they are kicked or the match closes, so their client can go home
// instead of waiting for messages that will never come.
type RemovedMsg struct {
	Reason string `json:"reason"` // one of the Removed* reasons
}

// Why a player lost their seat.
const (
	RemovedMissedTurns    = "MISSED_TURNS"     // includes being disconnected through them
	RemovedLeft           = "LEFT"             // LEAVE_MATCH
	RemovedNotEnoughCoins = "NOT_ENOUGH_COINS" // couldn't cover the stake when the match started
)

// Event type names, as sent to clients.
const (
	EvCardPlayed       = "CARD_PLAYED"
	EvCardsDrawn       = "CARDS_DRAWN"
	EvPlayerSkipped    = "PLAYER_SKIPPED"
	EvDirectionChanged = "DIRECTION_CHANGED"
	EvTurnChanged      = "TURN_CHANGED"
	EvTurnTimedOut     = "TURN_TIMED_OUT"
	EvSeatControl      = "SEAT_CONTROL"
	EvPlayerConnection = "PLAYER_CONNECTION"
	EvGameOver         = "GAME_OVER"
	EvTurnExtended     = "TURN_EXTENDED"
)

// Error codes, as sent to clients.
const (
	ErrCodeNotYourTurn       = "NOT_YOUR_TURN"
	ErrCodeCardNotInHand     = "CARD_NOT_IN_HAND"
	ErrCodeIllegalCard       = "ILLEGAL_CARD"
	ErrCodeColorRequired     = "COLOR_REQUIRED"
	ErrCodeMustPlayDrawnCard = "MUST_PLAY_DRAWN_CARD"
	ErrCodeAlreadyDrew       = "ALREADY_DREW"
	ErrCodeCannotPass        = "CANNOT_PASS"
	ErrCodeGameNotStarted    = "GAME_NOT_STARTED"
	ErrCodeGameOver          = "GAME_OVER"
	ErrCodeBadMessage        = "BAD_MESSAGE"
	ErrCodeNotImplemented    = "NOT_IMPLEMENTED"
	ErrCodeCannotExtend      = "CANNOT_EXTEND"
	ErrCodeDebugToolsOff     = "DEBUG_TOOLS_OFF"
	ErrCodeNotEnoughCoins    = "NOT_ENOUGH_COINS" // find_match, and joining a staked lobby
)

// ---------- client -> server ----------

// PlayCardReq is the body of OpPlayCard. Color is required for wild cards.
type PlayCardReq struct {
	CardID int    `json:"cardId"`
	Color  string `json:"color"`
}

// DebugSeatReq is the body of OpDebugWin and OpDebugOneCard.
type DebugSeatReq struct {
	Seat int `json:"seat"`
}

// ---------- helpers ----------

func toCardMsg(c game.Card) CardMsg {
	return CardMsg{ID: c.ID, DefID: c.DefID}
}

func toCardMsgs(cards []game.Card) []CardMsg {
	out := make([]CardMsg, 0, len(cards))
	for _, c := range cards {
		out = append(out, toCardMsg(c))
	}
	return out
}

func toResultMsgs(results []game.Result) []ResultMsg {
	if results == nil {
		return nil
	}
	out := make([]ResultMsg, 0, len(results))
	for _, r := range results {
		out = append(out, ResultMsg{Seat: r.Seat, Place: r.Place, CardsLeft: r.CardsLeft, Penalty: r.Penalty, Cards: toCardMsgs(r.Hand)})
	}
	return out
}

// toEventMsgs converts what the rules engine reported into wire events. The
// turn timer and pending draw live on the handler, so they are attached here.
func toEventMsgs(events []game.Event, turnMs, pendingDraw int) []EventMsg {
	out := make([]EventMsg, 0, len(events))

	for _, e := range events {
		msg := EventMsg{Type: string(e.Type), Seat: e.Seat}

		switch e.Type {
		case game.EvCardPlayed:
			card := toCardMsg(e.Card)
			msg.Card = &card
			msg.Color = string(e.Color)

		case game.EvCardsDrawn:
			msg.Count = len(e.Cards)
			msg.Cards = toCardMsgs(e.Cards) // stripped per player before sending

		case game.EvDirectionChanged:
			msg.Direction = 0 // filled in by the caller, which knows the state

		case game.EvTurnChanged:
			msg.TurnMs = turnMs
			msg.PendingDraw = pendingDraw

		case game.EvGameOver:
			msg.Ranking = e.Ranking
			msg.Results = toResultMsgs(e.Results)
		}

		out = append(out, msg)
	}
	return out
}
