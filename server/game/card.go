// Package game holds the pure Uno rules. It must never import Nakama, so it can
// be tested with plain `go test` and reused by bots or tools.
package game

import (
	"encoding/json"
	"fmt"
)

// Color is a "defined type": a new type whose underlying type is string.
// It converts to/from JSON like a string, but the compiler won't let you pass
// a plain string or a CardType where a Color is expected.
type Color string

// A const block declares the allowed values. Names starting with a capital
// letter are exported (public outside the package); lowercase is package-private.
const (
	Red    Color = "RED"
	Yellow Color = "YELLOW"
	Green  Color = "GREEN"
	Blue   Color = "BLUE"
	Wild   Color = "WILD"
)

// valid is a method on Color: the (c Color) before the name is the "receiver",
// Go's equivalent of `this`. Lowercase, so only this package can call it.
func (c Color) valid() bool {
	switch c {
	case Red, Yellow, Green, Blue, Wild:
		return true
	}
	return false
}

const totalCardCount = 108

type CardType string

const (
	Number       CardType = "NUMBER"
	Skip         CardType = "SKIP"
	Reverse      CardType = "REVERSE"
	DrawTwo      CardType = "DRAW_TWO"
	WildCard     CardType = "WILD"
	WildDrawFour CardType = "WILD_DRAW_FOUR"
)

func (t CardType) valid() bool {
	switch t {
	case Number, Skip, Reverse, DrawTwo, WildCard, WildDrawFour:
		return true
	}
	return false
}

// IsWild reports whether this type is played with a chosen color.
func (t CardType) IsWild() bool {
	return t == WildCard || t == WildDrawFour
}

// CardDef is one entry in cards.json. The `json:"..."` strings are struct tags:
// they tell encoding/json which JSON key maps to which field. Unknown keys
// (like "_comment") are ignored, and missing keys leave the zero value.
type CardDef struct {
	ID     string   `json:"id"`
	Color  Color    `json:"color"`
	Type   CardType `json:"type"`
	Number int      `json:"number"` // only meaningful when Type == Number; 0 is a real card
	Count  int      `json:"count"`  // copies in a full deck
}

// Card is one physical card in a game. ID is unique per game (0..107), so two
// RED_5s are distinguishable. Small struct, passed and compared by value.
type Card struct {
	ID    int
	DefID string
}

// Catalog is the loaded, validated set of card definitions.
type Catalog struct {
	defs []CardDef          // file order; deck building iterates this, never the map
	byID map[string]CardDef // fast lookup by definition id
}

// Def returns the definition for a card. The second return value follows Go's
// "comma ok" idiom, like reading a map: ok is false if the id is unknown.
func (c *Catalog) Def(card Card) (CardDef, bool) {
	def, ok := c.byID[card.DefID]
	return def, ok
}

// Defs returns the definitions in file order.
func (c *Catalog) Defs() []CardDef {
	return c.defs
}

// LoadCatalog parses and validates cards.json. It takes bytes, not a path, so
// the server can pass in go:embed data and tests can pass os.ReadFile output.
//
// Go has no exceptions: functions that can fail return an error as their last
// value, and callers check `if err != nil` right away.
func LoadCatalog(data []byte) (*Catalog, error) {
	// An anonymous struct matching the top-level shape of the file.
	var file struct {
		Cards []CardDef `json:"cards"`
	}
	if err := json.Unmarshal(data, &file); err != nil {
		// %w wraps the original error so callers can still inspect it with errors.Is/As.
		return nil, fmt.Errorf("parse cards.json: %w", err)
	}

	byID := make(map[string]CardDef, len(file.Cards))
	total := 0

	// `for i, def := range slice` loops with index and a COPY of each element.
	for _, def := range file.Cards {
		defID := def.ID
		if defID == "" {
			return nil, fmt.Errorf("card definition has empty ID")
		}
		if _, exists := byID[defID]; exists {
			return nil, fmt.Errorf("duplicate card definition ID: %s", defID)
		}
		if !def.Color.valid() {
			return nil, fmt.Errorf("invalid color for card definition ID %s: %s", defID, def.Color)
		}
		if !def.Type.valid() {
			return nil, fmt.Errorf("invalid type for card definition ID %s: %s", defID, def.Type)
		}
		if def.Type.IsWild() && def.Color != Wild {
			return nil, fmt.Errorf("wild card definition ID %s has non-wild color: %s", defID, def.Color)
		}
		if !def.Type.IsWild() && def.Color == Wild {
			return nil, fmt.Errorf("non-wild card definition ID %s has wild color", defID)
		}
		if def.Type == Number && (def.Number < 0 || def.Number > 9) {
			return nil, fmt.Errorf("number card definition ID %s has invalid number: %d", defID, def.Number)
		}
		if def.Count <= 0 {
			return nil, fmt.Errorf("card definition ID %s has non-positive count: %d", defID, def.Count)
		}
		total += def.Count
		byID[defID] = def
	}

	if total != totalCardCount {
		return nil, fmt.Errorf("invalid total card count: %d (want %d)", total, totalCardCount)
	}

	return &Catalog{defs: file.Cards, byID: byID}, nil
}
