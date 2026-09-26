package main

import "github.com/heroiclabs/nakama-common/runtime"

// testLogger is a runtime.Logger that throws everything away, so functions
// which log can be unit-tested without a Nakama server.
type testLogger struct{}

func (testLogger) Debug(format string, v ...interface{}) {}
func (testLogger) Info(format string, v ...interface{})  {}
func (testLogger) Warn(format string, v ...interface{})  {}
func (testLogger) Error(format string, v ...interface{}) {}

func (l testLogger) WithField(key string, v interface{}) runtime.Logger      { return l }
func (l testLogger) WithFields(fields map[string]interface{}) runtime.Logger { return l }
func (l testLogger) Fields() map[string]interface{}                          { return nil }
