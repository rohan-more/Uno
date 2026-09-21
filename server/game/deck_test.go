package game

import (
	"math/rand/v2"
	"os"
	"testing"
)

// loadTestCatalog is a test helper: shared setup that several tests need.
// t.Helper() makes failures point at the line in the calling test instead of
// a line inside this function.
func loadTestCatalog(t *testing.T) *Catalog {
	t.Helper()
	data, err := os.ReadFile("../data/cards.json")
	if err != nil {
		t.Fatalf("read cards.json: %v", err)
	}
	cat, err := LoadCatalog(data)
	if err != nil {
		t.Fatalf("LoadCatalog: %v", err)
	}
	return cat
}

// seededRNG returns a new generator with a fixed seed. Each call gives a fresh
// generator, so two decks shuffled with seededRNG(42) end up identical.
func seededRNG(seed uint64) *rand.Rand {
	return rand.New(rand.NewPCG(seed, 0))
}

// requireEveryCardOnce fails unless cards holds each ID 0..107 exactly once.
// This "no card lost or duplicated" check will be reused by the rules tests.
func requireEveryCardOnce(t *testing.T, cards []Card) {
	t.Helper()
	if len(cards) != totalCardCount {
		t.Fatalf("got %d cards, want %d", len(cards), totalCardCount)
	}
	// map[int]bool as a set: "have I seen this ID before?"
	seen := map[int]bool{}
	for _, c := range cards {
		if seen[c.ID] { // a missing key reads as the zero value, false
			t.Errorf("duplicate card ID %d", c.ID)
		}
		seen[c.ID] = true
	}
	for id := 0; id < totalCardCount; id++ {
		if !seen[id] {
			t.Errorf("missing card ID %d", id)
		}
	}
}

// sameOrder reports whether two decks hold the same cards in the same order.
func sameOrder(a, b *Deck) bool {
	if len(a.cards) != len(b.cards) {
		return false
	}
	for i := range a.cards {
		if a.cards[i] != b.cards[i] { // Card fields are comparable, so != works
			return false
		}
	}
	return true
}

func TestNewDeck(t *testing.T) {
	deck := NewDeck(loadTestCatalog(t))

	// Same package, so tests can read the unexported cards field.
	requireEveryCardOnce(t, deck.cards)

	copies := map[string]int{} // DefID -> how many physical cards
	for _, c := range deck.cards {
		copies[c.DefID]++
	}
	want := map[string]int{
		"RED_0":          1,
		"RED_5":          2,
		"BLUE_SKIP":      2,
		"WILD":           4,
		"WILD_DRAW_FOUR": 4,
	}
	for id, n := range want {
		if copies[id] != n {
			t.Errorf("copies of %s = %d, want %d", id, copies[id], n)
		}
	}
}

func TestShuffle_SameSeedSameOrder(t *testing.T) {
	cat := loadTestCatalog(t)
	a, b := NewDeck(cat), NewDeck(cat)

	// Two separate generators with the same seed. Sharing ONE generator would
	// give different orders, because the second shuffle continues its sequence.
	a.Shuffle(seededRNG(42))
	b.Shuffle(seededRNG(42))

	if !sameOrder(a, b) {
		t.Error("same seed produced different orders")
	}
}

func TestShuffle_DifferentSeedDifferentOrder(t *testing.T) {
	cat := loadTestCatalog(t)
	unshuffled := NewDeck(cat)
	a, b := NewDeck(cat), NewDeck(cat)

	a.Shuffle(seededRNG(1))
	b.Shuffle(seededRNG(2))

	if sameOrder(a, b) {
		t.Error("different seeds produced the same order")
	}
	if sameOrder(a, unshuffled) {
		t.Error("Shuffle left the deck in its original order")
	}
	requireEveryCardOnce(t, a.cards) // shuffling must not lose or copy cards
}

func TestDraw(t *testing.T) {
	deck := NewDeck(loadTestCatalog(t))

	// Unshuffled, the top (end of the slice) is the last card NewDeck added:
	// the final WILD_DRAW_FOUR in cards.json, ID 107.
	first, ok := deck.Draw()
	if !ok {
		t.Fatal("first Draw() returned ok = false")
	}
	if first.ID != totalCardCount-1 || first.DefID != "WILD_DRAW_FOUR" {
		t.Errorf("first card = %+v, want {ID:107 DefID:WILD_DRAW_FOUR}", first)
	}

	for remaining := totalCardCount - 1; remaining > 0; remaining-- {
		if deck.Len() != remaining {
			t.Fatalf("Len() = %d, want %d", deck.Len(), remaining)
		}
		if _, ok := deck.Draw(); !ok {
			t.Fatalf("Draw() failed with %d cards left", remaining)
		}
	}

	if deck.Len() != 0 {
		t.Fatalf("Len() = %d after drawing everything, want 0", deck.Len())
	}
	if card, ok := deck.Draw(); ok {
		t.Errorf("Draw() on empty deck returned %+v, ok = true", card)
	}
}

func TestRefill_KeepsEveryCard(t *testing.T) {
	deck := NewDeck(loadTestCatalog(t))
	rng := seededRNG(7)
	deck.Shuffle(rng)

	// Draw 30 cards into a pretend discard pile.
	var discard []Card // a nil slice; append works on it like an empty one
	for i := 0; i < 30; i++ {
		c, ok := deck.Draw()
		if !ok {
			t.Fatalf("Draw() failed at %d", i)
		}
		discard = append(discard, c)
	}
	if deck.Len() != totalCardCount-30 {
		t.Fatalf("Len() = %d after 30 draws, want %d", deck.Len(), totalCardCount-30)
	}

	deck.Refill(discard, rng)

	requireEveryCardOnce(t, deck.cards)
}
