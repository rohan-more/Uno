package game

import "math/rand/v2"

// Deck is the draw pile. The top of the pile is the END of the slice, so
// drawing is "take the last element and shrink the slice" — no copying.
//
// A slice ([]Card) is Go's growable list, like C#'s List<T>. Under the hood it's
// a view onto an array: a pointer, a length (len) and a capacity (cap).
type Deck struct {
	cards []Card
}

// NewDeck builds one physical card per copy in the catalog, in catalog order,
// giving each a unique ID starting at 0. It does NOT shuffle; call Shuffle.
//
// Returning *Deck (a pointer) means callers share one Deck and methods that
// change it (Draw, Refill) change the same one — like a class reference in C#.
func NewDeck(cat *Catalog) *Deck {
	// make([]Card, 0, n) creates an empty slice with room for n cards, so the
	// appends below never have to reallocate.
	cards := make([]Card, 0, totalCardCount)

	// Iterate the slice, not the map: map iteration order is random in Go.
	nextID := 0
	for _, def := range cat.Defs() {
		for i := 0; i < def.Count; i++ {
			cards = append(cards, Card{ID: nextID, DefID: def.ID})
			nextID++
		}
	}

	return &Deck{cards: cards}
}

// Len returns how many cards are left to draw.
func (d *Deck) Len() int {
	return len(d.cards)
}

// Shuffle randomizes the order using the given generator. The caller owns the
// generator, so a fixed seed gives the same order every time:
//
//	rng := rand.New(rand.NewPCG(42, 0)) // seed with any two numbers
//	deck.Shuffle(rng)
//
// Never use the package-level rand.Shuffle here: it's randomly seeded, so
// tests and bug replays couldn't reproduce a game.
func (d *Deck) Shuffle(rng *rand.Rand) {
	// rng.Shuffle calls our func to swap positions i and j. The func is a
	// closure: it can read and modify d.cards from the surrounding scope.
	rng.Shuffle(len(d.cards), func(i, j int) {
		d.cards[i], d.cards[j] = d.cards[j], d.cards[i] // Go can swap in one line
	})
}

// Draw removes and returns the top card. ok is false if the deck is empty;
// deciding what happens then (refill from the discard pile) is the game's job.
func (d *Deck) Draw() (Card, bool) {
	if len(d.cards) == 0 {
		return Card{}, false
	}

	last := len(d.cards) - 1
	card := d.cards[last]
	d.cards = d.cards[:last]
	return card, true
}

// Refill adds cards (e.g. the discard pile minus its top card) back into the
// deck and shuffles the whole deck.
func (d *Deck) Refill(cards []Card, rng *rand.Rand) {
	// cards... spreads the slice so append adds each element.
	d.cards = append(d.cards, cards...)
	d.Shuffle(rng)
}
