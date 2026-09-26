package main

import "github.com/rohan-more/Uno/server/game"

// Opcodes, mirrored in the client's UnoOpCodes.cs and PROTOCOL.md.
const (
	// client -> server
	OpPlayCard     int64 = 2
	OpDrawCard     int64 = 3
	OpPass         int64 = 4
	OpRequestState int64 = 5

	// server -> client
	OpLobbyState int64 = 100
	OpGameState  int64 = 101
	OpEvents     int64 = 102
	OpError      int64 = 103
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
	You         int        `json:"you"`
	Seats       []GameSeat `json:"seats"`
	Hand        []CardMsg  `json:"hand"`
	TopCard     CardMsg    `json:"topCard"`
	ActiveColor string     `json:"activeColor"`
	Direction   int        `json:"direction"`
	CurrentSeat int        `json:"currentSeat"`
	PendingDraw int        `json:"pendingDraw"`
	DrawnCardID *int       `json:"drawnCardId"`
	DeckCount   int        `json:"deckCount"`
	TurnMsLeft  int        `json:"turnMsLeft"`
	StartsInMs  int        `json:"startsInMs,omitempty"` // only before the first turn
	Ranking     []int      `json:"ranking"`
	Seq         int        `json:"seq"`
}

// EventMsg is one thing that happened. Irrelevant fields are omitted, and
// Cards is filled in only for the player who drew them.
type EventMsg struct {
	Type        string    `json:"type"`
	Seat        int       `json:"seat,omitempty"`
	Card        *CardMsg  `json:"card,omitempty"`
	Cards       []CardMsg `json:"cards,omitempty"`
	Count       int       `json:"count,omitempty"`
	Color       string    `json:"color,omitempty"`
	Direction   int       `json:"direction,omitempty"`
	TurnMs      int       `json:"turnMs,omitempty"`
	PendingDraw int       `json:"pendingDraw,omitempty"`
	Place       int       `json:"place,omitempty"`
	Kind        string    `json:"kind,omitempty"`      // SEAT_CONTROL
	Connected   *bool     `json:"connected,omitempty"` // PLAYER_CONNECTION
	Ranking     []int     `json:"ranking,omitempty"`
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
	EvPlayerFinished   = "PLAYER_FINISHED"
	EvGameOver         = "GAME_OVER"
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
)

// ---------- client -> server ----------

// PlayCardReq is the body of OpPlayCard. Color is required for wild cards.
type PlayCardReq struct {
	CardID int    `json:"cardId"`
	Color  string `json:"color"`
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
