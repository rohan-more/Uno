package game

import "math/rand/v2"

// BotAction picks a move for a seat the server is playing. It only looks at
// what a real player at that seat could see: their own hand, the discard pile
// and the pending draw.
//
// This is deliberately simple for now: play the first legal card, choosing the
// colour you hold most of, otherwise draw, otherwise pass. A stronger bot
// replaces this function without touching anything else, because the server
// only ever calls BotAction.
func BotAction(s *GameState, seat int, rng *rand.Rand) Action {
	if playable := s.PlayableCards(seat); len(playable) > 0 {
		card := playable[0]

		def, _ := s.cat.Def(card)
		action := Action{Type: PlayCard, CardID: card.ID}
		if def.Type.IsWild() {
			action.Color = bestColor(s, seat, rng)
		}
		return action
	}

	// A card was drawn this turn and can't be played: end the turn.
	if s.DrawnCard != nil {
		return Action{Type: Pass}
	}
	return Action{Type: DrawCard}
}

// bestColor is the colour the seat holds most of, so a wild keeps its options
// open. Ties, and a hand of nothing but wilds, are broken randomly.
func bestColor(s *GameState, seat int, rng *rand.Rand) Color {
	colors := []Color{Red, Yellow, Green, Blue}

	counts := map[Color]int{}
	for _, card := range s.Players[seat].Hand {
		def, ok := s.cat.Def(card)
		if ok && !def.Type.IsWild() {
			counts[def.Color]++
		}
	}

	best, bestCount := colors[rng.IntN(len(colors))], 0
	for _, c := range colors {
		if counts[c] > bestCount {
			best, bestCount = c, counts[c]
		}
	}
	return best
}
