using System;

/// <summary>
/// Shapes of the match messages from the server, matching server/PROTOCOL.md.
/// JsonUtility fills these in: anything the server omits stays at its default,
/// so check the field that identifies the message rather than assuming presence.
/// </summary>
[Serializable]
public class CardMsg
{
    public int id;       // instance id, 0-107, unique within a match
    public string defId; // e.g. "RED_5", looks up the sprite in CardDatabase

    public bool IsEmpty => string.IsNullOrEmpty(defId);
}

/// <summary>One seat during play. Hands are never included: opponents are counts.</summary>
[Serializable]
public class GameSeatMsg
{
    public int seat;
    public string kind; // "human" | "bot"
    public string name;
    public int cardCount;
    public int place; // 0 while still playing, else finishing position
    public int avatar;
    public bool connected;
}

/// <summary>Opcode 101: the full personalized snapshot.</summary>
[Serializable]
public class GameStateMsg
{
    public int you;
    public GameSeatMsg[] seats;
    public CardMsg[] hand; // your cards only
    public CardMsg topCard;
    public string activeColor;
    public int direction;   // +1 or -1
    public int currentSeat;
    public int pendingDraw;
    public int drawnCardId; // -1 when there is none
    public int deckCount;
    public int turnMsLeft;
    public int startsInMs;  // only before the first turn
    public int[] ranking;
    public int seq;
}

/// <summary>
/// One thing that happened. Which fields are filled depends on `type`; see
/// UnoEventTypes. Cards in CARDS_DRAWN only arrive for the player who drew them.
/// </summary>
[Serializable]
public class MatchEventMsg
{
    public string type;
    public int seat;
    public CardMsg card;
    public CardMsg[] cards;
    public int count;
    public string color;
    public int direction;
    public int turnMs;
    public int pendingDraw;
    public int place;
    public string kind;      // SEAT_CONTROL
    public bool connected;   // PLAYER_CONNECTION
    public int[] ranking;    // GAME_OVER
}

/// <summary>Opcode 102: everything one action caused, in order.</summary>
[Serializable]
public class MatchEventsMsg
{
    public int seq;
    public MatchEventMsg[] events;
}

/// <summary>Opcode 103: your action was rejected and nothing changed.</summary>
[Serializable]
public class MatchErrorMsg
{
    public string code;
    public string message;
}

/// <summary>
/// Opcode 104: you no longer have a seat (a bot took it, or the match closed).
/// Sent just before the server kicks you, so go home rather than wait.
/// </summary>
[Serializable]
public class MatchRemovedMsg
{
    public string reason; // see RemovedReasons
}

public static class RemovedReasons
{
    public const string MissedTurns = "MISSED_TURNS"; // connected or not
    public const string Left = "LEFT";                // we sent LEAVE_MATCH
}

/// <summary>Body of a PLAY_CARD message. Color is required for wild cards.</summary>
[Serializable]
public class PlayCardReq
{
    public int cardId;
    public string color;
}

/// <summary>Event type names, as the server sends them.</summary>
public static class UnoEventTypes
{
    public const string CardPlayed = "CARD_PLAYED";
    public const string CardsDrawn = "CARDS_DRAWN";
    public const string PlayerSkipped = "PLAYER_SKIPPED";
    public const string DirectionChanged = "DIRECTION_CHANGED";
    public const string TurnChanged = "TURN_CHANGED";
    public const string TurnTimedOut = "TURN_TIMED_OUT";
    public const string SeatControl = "SEAT_CONTROL";
    public const string PlayerConnection = "PLAYER_CONNECTION";
    public const string PlayerFinished = "PLAYER_FINISHED";
    public const string GameOver = "GAME_OVER";
    public const string TurnExtended = "TURN_EXTENDED";
}

/// <summary>Error codes. A well-behaved client should never see most of these.</summary>
public static class UnoErrorCodes
{
    public const string NotYourTurn = "NOT_YOUR_TURN";
    public const string CardNotInHand = "CARD_NOT_IN_HAND";
    public const string IllegalCard = "ILLEGAL_CARD";
    public const string ColorRequired = "COLOR_REQUIRED";
    public const string MustPlayDrawnCard = "MUST_PLAY_DRAWN_CARD";
    public const string AlreadyDrew = "ALREADY_DREW";
    public const string CannotPass = "CANNOT_PASS";
    public const string GameNotStarted = "GAME_NOT_STARTED";
    public const string GameOver = "GAME_OVER";
    public const string BadMessage = "BAD_MESSAGE";
    public const string CannotExtend = "CANNOT_EXTEND";
}

/// <summary>Seat kinds.</summary>
public static class SeatKinds
{
    public const string Human = "human";
    public const string Bot = "bot";
    public const string Empty = "empty";
}
