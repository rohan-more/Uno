package game

// Test files end in _test.go and are only compiled by `go test`, never into the
// server. Being in `package game` (same as card.go) lets tests see unexported
// things like Color.valid().

import (
	"os"
	"strings"
	"testing"
)

// Any func named TestXxx(t *testing.T) is a test. `go test` finds and runs them.
// t.Fatal / t.Fatalf stop this test immediately; t.Error / t.Errorf record a
// failure and keep going.
func TestLoadCatalog_RealFile(t *testing.T) {
	// Tests run with the package folder (server/game) as the working directory.
	data, err := os.ReadFile("../data/cards.json")
	if err != nil {
		t.Fatalf("read cards.json: %v", err)
	}

	cat, err := LoadCatalog(data)
	if err != nil {
		t.Fatalf("LoadCatalog: %v", err)
	}

	if got, want := len(cat.Defs()), 54; got != want {
		t.Errorf("definitions = %d, want %d", got, want)
	}

	def, ok := cat.Def(Card{DefID: "RED_0"})
	if !ok {
		t.Fatal("RED_0 should be found")
	}
	if def.Count != 1 {
		t.Errorf("RED_0 count = %d, want 1", def.Count)
	}
	def, ok = cat.Def(Card{DefID: "RED_5"})
	if !ok {
		t.Fatal("RED_5 should be found")
	}
	if def.Count != 2 {
		t.Errorf("RED_5 count = %d, want 2", def.Count)
	}
	def, ok = cat.Def(Card{DefID: "WILD"})
	if !ok {
		t.Fatal("WILD should be found")
	}
	if def.Count != 4 {
		t.Errorf("WILD count = %d, want 4", def.Count)
	}
	if _, ok := cat.Def(Card{DefID: "PURPLE_3"}); ok {
		t.Error("PURPLE_3 should not be found")
	}
}

// Table-driven test: one slice of cases, one loop. Adding a case is one line.
func TestLoadCatalog_Invalid(t *testing.T) {
	tests := []struct {
		name    string
		json    string
		wantErr string // a substring the error message must contain
	}{
		{
			name:    "not json",
			json:    `{`,
			wantErr: "parse cards.json",
		},
		{
			name: "duplicate id",
			json: `{"cards":[
				{"id":"RED_1","color":"RED","type":"NUMBER","number":1,"count":2},
				{"id":"RED_1","color":"RED","type":"NUMBER","number":1,"count":2}
			]}`,
			wantErr: "duplicate",
		},
		{
			name: "unknown color",
			json: `{"cards":[
				{"id":"PURPLE_1","color":"PURPLE","type":"NUMBER","number":1,"count":2}
			]}`,
			wantErr: "invalid color",
		},
		{
			name: "unknown type",
			json: `{"cards":[
				{"id":"RED_1","color":"RED","type":"FOO","number":1,"count":2}
			]}`,
			wantErr: "invalid type",
		},
		{
			name: "wild with normal color",
			json: `{"cards":[
				{"id":"WILD_RED","color":"RED","type":"WILD","count":4}
			]}`,
			wantErr: "has non-wild color",
		},
		{
			name: "number with wild color",
			json: `{"cards":[
				{"id":"WILD_1","color":"WILD","type":"NUMBER","number":1,"count":2}
			]}`,
			wantErr: "non-wild card definition",
		},
		{
			name: "number out of range",
			json: `{"cards":[
				{"id":"RED_10","color":"RED","type":"NUMBER","number":10,"count":2}
			]}`,
			wantErr: "invalid number",
		},
		{
			name: "count 0",
			json: `{"cards":[
				{"id":"RED_1","color":"RED","type":"NUMBER","number":1,"count":0}
			]}`,
			wantErr: "non-positive count",
		},
		{
			name: "empty id",
			json: `{"cards":[
				{"id":"","color":"RED","type":"NUMBER","number":1,"count":2}
			]}`,
			wantErr: "empty ID",
		},
		{
			name: "wrong total",
			json: `{"cards":[
				{"id":"RED_1","color":"RED","type":"NUMBER","number":1,"count":2}
			]}`,
			wantErr: "invalid total",
		},
	}

	for _, tt := range tests {
		// t.Run makes each case a named subtest, so failures read like
		// "TestLoadCatalog_Invalid/duplicate_id".
		t.Run(tt.name, func(t *testing.T) {
			_, err := LoadCatalog([]byte(tt.json))
			if err == nil {
				t.Fatal("expected an error, got nil")
			}
			// Why check the message and not just err != nil? These small inputs
			// are never 108 cards, so they'd ALL fail the total check anyway.
			// Without this, a broken duplicate check would still "pass".
			if !strings.Contains(err.Error(), tt.wantErr) {
				t.Errorf("error = %q, want it to contain %q", err, tt.wantErr)
			}
		})
	}
}
