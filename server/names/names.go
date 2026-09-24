// Package names generates player and bot display names in the style
// Adjective + Animal + two digits, e.g. "SpryCrane15". Names are kept short so
// they fit under a round avatar without truncation.
package names

import (
	"math/rand/v2"
	"strings"
)

// MaxLength is the longest name the generator will produce, so nameplates in
// the client have a predictable upper bound.
const MaxLength = 14

var (
	adjectives = []string{
		"Brave", "Swift", "Calm", "Bold", "Bright", "Clever", "Quiet", "Lucky",
		"Merry", "Noble", "Proud", "Sly", "Witty", "Eager", "Fair", "Fierce",
		"Jolly", "Keen", "Kind", "Neat", "Sharp", "Spry", "Sunny", "Wise",
		"Zippy", "Cosy", "Dandy", "Deft", "Fleet", "Glad", "Grand", "Hardy",
		"Nifty", "Perky", "Rapid", "Royal", "Sleek", "Snappy", "Steady", "Vivid",
	}
	animals = []string{
		"Falcon", "Otter", "Tiger", "Heron", "Panda", "Lynx", "Raven", "Bison",
		"Koala", "Gecko", "Hawk", "Moose", "Orca", "Puma", "Robin", "Seal",
		"Shark", "Sloth", "Swan", "Wolf", "Yak", "Zebra", "Crane", "Fox",
		"Badger", "Bear", "Cobra", "Crab", "Deer", "Eagle", "Ibis", "Jaguar",
		"Kiwi", "Llama", "Mole", "Newt", "Owl", "Quail", "Stork", "Viper",
	}
)

// Combinations is how many distinct names the word lists can produce.
func Combinations() int {
	return len(adjectives) * len(animals) * 90
}

// Generate returns one name. It retries internally until the name fits in
// MaxLength, which the shorter words always allow.
func Generate(rng *rand.Rand) string {
	for {
		var b strings.Builder
		b.WriteString(adjectives[rng.IntN(len(adjectives))])
		b.WriteString(animals[rng.IntN(len(animals))])
		b.WriteString(digits(rng))

		if name := b.String(); len(name) <= MaxLength {
			return name
		}
	}
}

// GenerateUnused returns a name that is not in `used`, e.g. so two bots at one
// table never share a name. It gives up after a fixed number of tries and
// returns a name anyway rather than looping forever.
func GenerateUnused(rng *rand.Rand, used map[string]bool) string {
	var name string
	for i := 0; i < 20; i++ {
		name = Generate(rng)
		if !used[name] {
			break
		}
	}
	return name
}

// digits returns a two-digit suffix, 10-99.
func digits(rng *rand.Rand) string {
	n := 10 + rng.IntN(90)
	return string(rune('0'+n/10)) + string(rune('0'+n%10))
}
