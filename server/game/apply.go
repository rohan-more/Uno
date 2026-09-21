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
	Pass     ActionType = "PASS" // only after drawing a playable card
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
	EvPlayerFinished   EventType = "PLAYER_FINISHED"
	EvGameOver         EventType = "GAME_OVER"
)

// Event is one change, in the order it happened. Fields not relevant to the
// type are left zero. EvCardsDrawn lists the actual cards; the server must
// only send them to the drawing player and send a count to everyone else.
type Event struct {
	Type    EventType
	Seat    int    // who it happened to (the player, the new current seat...)
	Card    Card   // EvCardPlayed
	Cards   []Card // EvCardsDrawn
	Color   Color  // EvCardPlayed: the active color after the play
	Place   int    // EvPlayerFinished: 1 = first to finish
	Ranking []int  // EvGameOver: seats in finishing order
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
	if seat != s.Current {
		return nil, ErrNotYourTurn // finished players are never Current
	}

	switch a.Type {
	case PlayCard:
		return s.playCard(seat, a.CardID, a.Color)
	case DrawCard:
		return s.drawCard(seat)
	case Pass:
		return s.pass()
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

	if len(s.Players[seat].Hand) == 0 {
		events = append(events, s.finish(seat))
		if s.inGameCount() == 1 {
			for last := range s.Players {
				if s.InGame(last) {
					events = append(events, s.finish(last))
				}
			}
			return append(events, Event{Type: EvGameOver, Ranking: append([]int(nil), s.Ranking...)}), nil
		}
		// Otherwise the game goes on, and the card's effect below still applies.
	}

	step := 1
	switch def.Type {
	case Skip:
		step = 2
	case Reverse:
		s.Direction = -s.Direction
		events = append(events, Event{Type: EvDirectionChanged, Seat: seat})
		// With two players left, Reverse acts as Skip: the same player goes
		// again. If the player who reversed just finished, there's nobody to
		// "go again", so play simply continues in the new direction.
		if s.inGameCount() == 2 && s.InGame(seat) {
			step = 2
		}
	case DrawTwo:
		s.PendingDraw += 2
	case WildDrawFour:
		s.PendingDraw += 4
	}
	return append(events, s.advance(step)), nil
}

func (s *GameState) drawCard(seat int) ([]Event, error) {
	if s.DrawnCard != nil {
		return nil, ErrAlreadyDrew
	}

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

func (s *GameState) pass() ([]Event, error) {
	if s.DrawnCard == nil {
		return nil, ErrCannotPass
	}
	return []Event{s.advance(1)}, nil
}

// finish gives seat the next finishing place.
func (s *GameState) finish(seat int) Event {
	s.Ranking = append(s.Ranking, seat)
	s.Players[seat].Place = len(s.Ranking)
	return Event{Type: EvPlayerFinished, Seat: seat, Place: s.Players[seat].Place}
}

// advance ends the current turn, moving step players in the current direction.
func (s *GameState) advance(step int) Event {
	s.DrawnCard = nil
	s.Current = s.nextSeat(s.Current, step)
	return Event{Type: EvTurnChanged, Seat: s.Current}
}
