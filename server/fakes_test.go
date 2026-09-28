package main

import (
	"encoding/json"

	"github.com/heroiclabs/nakama-common/runtime"
)

// fakePresence stands in for a connected client in tests.
type fakePresence struct {
	userID    string
	sessionID string
}

func (p fakePresence) GetUserId() string                 { return p.userID }
func (p fakePresence) GetSessionId() string              { return p.sessionID }
func (p fakePresence) GetNodeId() string                 { return "test" }
func (p fakePresence) GetHidden() bool                   { return false }
func (p fakePresence) GetPersistence() bool              { return false }
func (p fakePresence) GetUsername() string               { return p.userID }
func (p fakePresence) GetStatus() string                 { return "" }
func (p fakePresence) GetReason() runtime.PresenceReason { return runtime.PresenceReasonUnknown }

// fakeMatchData is one message from a client.
type fakeMatchData struct {
	fakePresence
	opCode int64
	data   []byte
}

func (m fakeMatchData) GetOpCode() int64      { return m.opCode }
func (m fakeMatchData) GetData() []byte       { return m.data }
func (m fakeMatchData) GetReliable() bool     { return true }
func (m fakeMatchData) GetReceiveTime() int64 { return 0 }

// sentMessage is one broadcast the handler made.
type sentMessage struct {
	opCode int64
	data   []byte
	to     []runtime.Presence
}

// fakeDispatcher records broadcasts instead of sending them.
type fakeDispatcher struct {
	sent   []sentMessage
	kicked []runtime.Presence
	label  string
}

func (d *fakeDispatcher) BroadcastMessage(opCode int64, data []byte, presences []runtime.Presence, sender runtime.Presence, reliable bool) error {
	d.sent = append(d.sent, sentMessage{opCode: opCode, data: data, to: presences})
	return nil
}

func (d *fakeDispatcher) BroadcastMessageDeferred(opCode int64, data []byte, presences []runtime.Presence, sender runtime.Presence, reliable bool) error {
	return d.BroadcastMessage(opCode, data, presences, sender, reliable)
}

func (d *fakeDispatcher) MatchKick(presences []runtime.Presence) error {
	d.kicked = append(d.kicked, presences...)
	return nil
}

func (d *fakeDispatcher) MatchLabelUpdate(label string) error {
	d.label = label
	return nil
}

// events returns every event of this type the handler broadcast.
func (d *fakeDispatcher) events(eventType string) []EventMsg {
	var found []EventMsg
	for _, msg := range d.sent {
		if msg.opCode != OpEvents {
			continue
		}
		var batch EventsMsg
		if json.Unmarshal(msg.data, &batch) != nil {
			continue
		}
		for _, e := range batch.Events {
			if e.Type == eventType {
				found = append(found, e)
			}
		}
	}
	return found
}

// errors returns every ERROR message sent.
func (d *fakeDispatcher) errors() []ErrorMsg {
	var found []ErrorMsg
	for _, msg := range d.sent {
		if msg.opCode != OpError {
			continue
		}
		var e ErrorMsg
		if json.Unmarshal(msg.data, &e) == nil {
			found = append(found, e)
		}
	}
	return found
}

func (d *fakeDispatcher) count(opCode int64) int {
	n := 0
	for _, msg := range d.sent {
		if msg.opCode == opCode {
			n++
		}
	}
	return n
}
