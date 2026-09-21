package game

import (
	"errors"
	"fmt"
)

// ActionType is what a player is trying to do.
type ActionType string

const (
	PlayCard ActionType = "PLAY_CARD"
	DrawCard ActionType = "DRAW_CARD"
	Pass     ActionType = "PASS"      // only after drawing a playable card
	CallUno  ActionType = "CALL_UNO"  // with 1 card, or 2 cards on your own turn
	CatchUno ActionType = "CATCH_UNO" // catch the player in UnoTarget
)

// Action is one request from a seat. CardID and Color are only used by PlayCard;
// Color is required for wild cards and ignored otherwise.
type Action struct {
	Type   ActionType
	CardID int
	Color  Color
}

// EventType describes something that happened, for the server to broadcast.
type EventType string

const (
	EvCardPlayed       EventType = "CARD_PLAYED"
	EvCardsDrawn       EventType = "CARDS_DRAWN"
	EvDirectionChanged EventType = "DIRECTION_CHANGED"
	EvTurnChanged      EventType = "TURN_CHANGED"
	EvUnoCalled        EventType = "UNO_CALLED"
	EvUnoCaught        EventType = "UNO_CAUGHT"
	EvGameOver         EventType = "GAME_OVER"
)

// Event is one change, in the order it happened. Fields not relevant to the
// type are left zero. EvCardsDrawn lists the actual cards; the server must
// only send them to the drawing player and send a count to everyone else.
type Event struct {
	Type  EventType
	Seat  int    // who it happened to (the player, the new current seat, the winner...)
	By    int    // EvUnoCaught: who caught them
	Card  Card   // EvCardPlayed
	Cards []Card // EvCardsDrawn
	Color Color  // EvCardPlayed: the active color after the play
}

var (
	ErrGameOver          = errors.New("game is over")
	ErrBadSeat           = errors.New("no such seat")
	ErrNotYourTurn       = errors.New("not your turn")
	ErrCardNotInHand     = errors.New("card not in hand")
	ErrIllegalCard       = errors.New("card can't be played on the current pile")
	ErrColorRequired     = errors.New("wild card needs a color: RED, YELLOW, GREEN or BLUE")
	ErrMustPlayDrawnCard = errors.New("after drawing you may only play the drawn card or pass")
	ErrAlreadyDrew       = errors.New("already drew this turn")
	ErrCannotPass        = errors.New("can only pass after drawing a playable card")
	ErrCannotCallUno     = errors.New("can only call UNO with 1 card, or 2 cards on your turn")
	ErrNothingToCatch    = errors.New("nobody to catch")
	ErrUnknownAction     = errors.New("unknown action")
)

// Apply validates an action from seat and, only if it's legal, changes the
// state and returns what happened. On error the state is untouched: every
// check runs before the first change.
func (s *GameState) Apply(seat int, a Action) ([]Event, error) {
	if s.Over() {
		return nil, ErrGameOver
	}
	if seat < 0 || seat >= len(s.Players) {
		return nil, fmt.Errorf("%w: %d", ErrBadSeat, seat)
	}

	switch a.Type {
	case PlayCard:
		return s.playCard(seat, a.CardID, a.Color)
	case DrawCard:
		return s.drawCard(seat)
	case Pass:
		return s.pass(seat)
	case CallUno:
		return s.callUno(seat)
	case CatchUno:
		return s.catchUno(seat)
	}
	return nil, fmt.Errorf("%w: %q", ErrUnknownAction, a.Type)
}

// CanPlay reports whether a card with this definition may be played now,
// ignoring whose turn it is. Used by Apply, bots and client highlighting.
func (s *GameState) CanPlay(def CardDef) bool {
	top := s.TopDef()

	// A pending draw can only be answered with the same draw card.
	if s.PendingDraw > 0 {
		return def.Type == top.Type
	}
	if def.Type.IsWild() {
		return true
	}
	if def.Color == s.ActiveColor {
		return true
	}
	if def.Type == Number {
		return top.Type == Number && def.Number == top.Number
	}
	return def.Type == top.Type // symbol match: Skip on Skip, etc.
}

// PlayableCards returns the cards seat may play right now, or nil if it isn't
// their turn.
func (s *GameState) PlayableCards(seat int) []Card {
	if s.Over() || seat != s.Current {
		return nil
	}
	var out []Card
	for _, c := range s.Players[seat].Hand {
		if s.DrawnCard != nil && c.ID != s.DrawnCard.ID {
			continue
		}
		def, _ := s.cat.Def(c)
		if s.CanPlay(def) {
			out = append(out, c)
		}
	}
	return out
}

func (s *GameState) playCard(seat, cardID int, chosen Color) ([]Event, error) {
	// ---- checks ----
	if seat != s.Current {
		return nil, ErrNotYourTurn
	}
	hand := s.Players[seat].Hand
	idx := -1
	for i, c := range hand {
		if c.ID == cardID {
			idx = i
			break
		}
	}
	if idx < 0 {
		return nil, fmt.Errorf("%w: %d", ErrCardNotInHand, cardID)
	}
	if s.DrawnCard != nil && cardID != s.DrawnCard.ID {
		return nil, ErrMustPlayDrawnCard
	}
	card := hand[idx]
	def, _ := s.cat.Def(card)
	if def.Type.IsWild() && (!chosen.valid() || chosen == Wild) {
		return nil, ErrColorRequired
	}
	if !s.CanPlay(def) {
		return nil, fmt.Errorf("%w: %s on %s (active %s)", ErrIllegalCard, def.ID, s.TopDef().ID, s.ActiveColor)
	}

	// ---- changes ----
	s.UnoTarget = NoSeat // any Play/Draw/Pass closes the catch window
	s.DrawnCard = nil

	// Remove from hand, keeping the order of the rest.
	s.Players[seat].Hand = append(hand[:idx], hand[idx+1:]...)
	s.Discard = append(s.Discard, card)
	if def.Type.IsWild() {
		s.ActiveColor = chosen
	} else {
		s.ActiveColor = def.Color
	}
	events := []Event{{Type: EvCardPlayed, Seat: seat, Card: card, Color: s.ActiveColor}}

	left := len(s.Players[seat].Hand)
	if left == 0 {
		s.Winner = seat
		return append(events, Event{Type: EvGameOver, Seat: seat}), nil
	}
	if left == 1 && !s.Players[seat].CalledUno {
		s.UnoTarget = seat
	}

	step := 1
	switch def.Type {
	case Skip:
		step = 2
	case Reverse:
		s.Direction = -s.Direction
		events = append(events, Event{Type: EvDirectionChanged, Seat: seat})
		if len(s.Players) == 2 {
			step = 2 // with two players Reverse acts as Skip
		}
	case DrawTwo:
		s.PendingDraw += 2
	case WildDrawFour:
		s.PendingDraw += 4
	}
	return append(events, s.advance(step)), nil
}

func (s *GameState) drawCard(seat int) ([]Event, error) {
	if seat != s.Current {
		return nil, ErrNotYourTurn
	}
	if s.DrawnCard != nil {
		return nil, ErrAlreadyDrew
	}

	s.UnoTarget = NoSeat

	// Owing cards from a Draw Two / Wild Draw Four: take them all, lose the turn.
	if s.PendingDraw > 0 {
		n := s.PendingDraw
		s.PendingDraw = 0
		drawn := s.drawCards(seat, n)
		return []Event{
			{Type: EvCardsDrawn, Seat: seat, Cards: drawn},
			s.advance(1),
		}, nil
	}

	drawn := s.drawCards(seat, 1)
	events := []Event{{Type: EvCardsDrawn, Seat: seat, Cards: drawn}}
	if len(drawn) == 1 {
		def, _ := s.cat.Def(drawn[0])
		if s.CanPlay(def) {
			c := drawn[0]
			s.DrawnCard = &c
			return events, nil // same player: play it or pass
		}
	}
	return append(events, s.advance(1)), nil
}

func (s *GameState) pass(seat int) ([]Event, error) {
	if seat != s.Current {
		return nil, ErrNotYourTurn
	}
	if s.DrawnCard == nil {
		return nil, ErrCannotPass
	}
	s.UnoTarget = NoSeat
	return []Event{s.advance(1)}, nil
}

func (s *GameState) callUno(seat int) ([]Event, error) {
	n := len(s.Players[seat].Hand)
	if n != 1 && !(n == 2 && seat == s.Current) {
		return nil, ErrCannotCallUno
	}
	s.Players[seat].CalledUno = true
	if s.UnoTarget == seat {
		s.UnoTarget = NoSeat
	}
	return []Event{{Type: EvUnoCalled, Seat: seat}}, nil
}

func (s *GameState) catchUno(seat int) ([]Event, error) {
	target := s.UnoTarget
	if target == NoSeat || target == seat {
		return nil, ErrNothingToCatch
	}
	s.UnoTarget = NoSeat
	drawn := s.drawCards(target, 2)
	return []Event{
		{Type: EvUnoCaught, Seat: target, By: seat},
		{Type: EvCardsDrawn, Seat: target, Cards: drawn},
	}, nil
}

// advance ends the current turn, moving step seats in the current direction.
func (s *GameState) advance(step int) Event {
	s.DrawnCard = nil
	s.Current = s.nextSeat(s.Current, step)
	return Event{Type: EvTurnChanged, Seat: s.Current}
}
