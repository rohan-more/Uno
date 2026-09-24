package main

import (
	"context"
	"database/sql"
	"math/rand/v2"
	"strings"
	"time"

	"github.com/heroiclabs/nakama-common/api"
	"github.com/heroiclabs/nakama-common/runtime"

	"github.com/rohan-more/Uno/server/names"
)

const (
	// nameAttempts is how many names we try before giving up. A clash means the
	// username is already taken by another account, which is rare.
	nameAttempts = 5

	// avatarCount must match the number of sprites in the client's
	// AvatarLibrary. The server only ever stores the index.
	avatarCount = 35
)

// InitModule is the entry point Nakama calls when it loads the plugin.
func InitModule(ctx context.Context, logger runtime.Logger, db *sql.DB, nk runtime.NakamaModule, initializer runtime.Initializer) error {
	if err := initializer.RegisterAfterAuthenticateDevice(afterAuthenticateDevice); err != nil {
		return err
	}

	logger.Info("Uno module loaded")
	return nil
}

// afterAuthenticateDevice runs after every device login. On a brand new account
// it picks a display name such as "BraveStormFalcon42" and stores it as both
// the Nakama username and the display name. Usernames are unique server-wide,
// so a successful update also guarantees no two players share a name.
func afterAuthenticateDevice(ctx context.Context, logger runtime.Logger, db *sql.DB, nk runtime.NakamaModule, out *api.Session, in *api.AuthenticateDeviceRequest) error {
	userID, ok := ctx.Value(runtime.RUNTIME_CTX_USER_ID).(string)
	if !ok || userID == "" {
		logger.Error("after authenticate: no user id in context")
		return nil // never block a login over a name
	}

	account, err := nk.AccountGetId(ctx, userID)
	if err != nil {
		logger.WithField("error", err.Error()).Error("after authenticate: get account")
		return nil
	}
	if account.GetUser().GetDisplayName() != "" {
		return nil // returning player, keep their name
	}

	rng := rand.New(rand.NewPCG(uint64(time.Now().UnixNano()), 0))
	metadata := map[string]interface{}{"avatar": rng.IntN(avatarCount)}

	for i := 0; i < nameAttempts; i++ {
		name := names.Generate(rng)

		err = nk.AccountUpdateId(ctx, userID, name, metadata, name, "", "", "", "")
		if err == nil {
			logger.WithField("user_id", userID).
				WithField("name", name).
				WithField("avatar", metadata["avatar"]).
				Info("assigned name and avatar")
			return nil
		}
		if !isUsernameTaken(err) {
			break
		}
	}

	logger.WithField("user_id", userID).WithField("error", err.Error()).Warn("could not assign a name")
	return nil // the client falls back to the auto-generated username
}

// isUsernameTaken reports whether an AccountUpdateId error is the unique
// username conflict, which is worth retrying with a different name.
func isUsernameTaken(err error) bool {
	return err != nil && strings.Contains(strings.ToLower(err.Error()), "username")
}
