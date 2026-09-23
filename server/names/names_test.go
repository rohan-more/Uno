package names

import (
	"math/rand/v2"
	"regexp"
	"testing"
)

func seeded(seed uint64) *rand.Rand { return rand.New(rand.NewPCG(seed, 0)) }

// A name is words in CamelCase followed by exactly two digits.
var shape = regexp.MustCompile(`^[A-Z][a-z]+[A-Z][a-z]+[A-Z][a-z]+[1-9][0-9]$`)

func TestGenerate(t *testing.T) {
	rng := seeded(1)
	seen := map[string]bool{}

	for i := 0; i < 2000; i++ {
		name := Generate(rng)
		if !shape.MatchString(name) {
			t.Fatalf("name %q doesn't match Adjective+Noun+Animal+2 digits", name)
		}
		if len(name) > MaxLength {
			t.Fatalf("name %q is %d chars, max is %d", name, len(name), MaxLength)
		}
		seen[name] = true
	}

	// Sanity check that it isn't returning the same handful of names.
	if len(seen) < 1900 {
		t.Errorf("2000 names produced only %d distinct values", len(seen))
	}
}

func TestGenerate_SameSeedSameNames(t *testing.T) {
	a, b := seeded(7), seeded(7)
	for i := 0; i < 10; i++ {
		if x, y := Generate(a), Generate(b); x != y {
			t.Fatalf("same seed gave %q and %q", x, y)
		}
	}
}

func TestGenerateUnused(t *testing.T) {
	rng := seeded(3)
	used := map[string]bool{}
	for i := 0; i < 4; i++ { // a full table of bots
		name := GenerateUnused(rng, used)
		if used[name] {
			t.Fatalf("GenerateUnused returned %q which was already taken", name)
		}
		used[name] = true
	}
}

func TestCombinations(t *testing.T) {
	// Enough that two players colliding is rare; the unique username check on
	// the server is what actually guarantees no duplicates.
	if got := Combinations(); got < 1_000_000 {
		t.Errorf("Combinations() = %d, want at least 1,000,000", got)
	}
}
