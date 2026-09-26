using System;

/// <summary>
/// Shapes of the server messages the lobby needs, matching server/PROTOCOL.md.
/// JsonUtility fills these: fields the server omits stay at their default.
/// </summary>
[Serializable]
public class LobbySeatMsg
{
    public int seat;
    public string kind;   // "empty" | "human" | "bot"
    public string userId; // humans only
    public string name;
    public int avatar;
    public bool connected;

    public bool IsEmpty => kind == "empty" || string.IsNullOrEmpty(kind);
}

/// <summary>Opcode 100: broadcast whenever the lobby changes.</summary>
[Serializable]
public class LobbyStateMsg
{
    public int countdownMsLeft;
    public LobbySeatMsg[] seats;
}
