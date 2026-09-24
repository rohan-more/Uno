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

    // server -> client
    public const long LobbyState = 100;  // seats + countdown
    public const long GameState = 101;   // full personalized snapshot
    public const long Events = 102;      // what one action caused, in order
    public const long Error = 103;       // rejected action, sender only
}
