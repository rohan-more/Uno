/// <summary>
/// Match message opcodes. Must match server/PROTOCOL.md and the Go match
/// handler: 1-49 are sent by the client, 100+ are sent by the server.
/// </summary>
public static class UnoOpCodes
{
    // client -> server
    public const long PlayCard = 2;      // {"cardId":57,"color":"BLUE"}
    public const long DrawCard = 3;      // {}
    public const long Pass = 4;          // {}
    public const long RequestState = 5;  // {}
    public const long LeaveMatch = 7;    // {} quit now: a bot takes the seat
    public const long ExtendTurn = 6;    // {} once a turn, before choosing a drawn wild's colour

    // server -> client
    public const long LobbyState = 100;  // seats + countdown
    public const long GameState = 101;   // full personalized snapshot
    public const long Events = 102;      // what one action caused, in order
    public const long Error = 103;       // rejected action, sender only
    public const long Removed = 104;     // you lost your seat, sender only
}
