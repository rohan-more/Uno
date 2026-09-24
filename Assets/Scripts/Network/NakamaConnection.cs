using System;
using System.Text;
using System.Threading.Tasks;
using Nakama;
using UnityEngine;

/// <summary>
/// Owns the Nakama client, session and socket for the whole app.
/// Lives in the Boot scene and survives scene loads. UI scripts talk to this;
/// nothing else should create a Client.
/// </summary>
public class NakamaConnection : MonoBehaviour
{
    public static NakamaConnection Instance { get; private set; }

    [Header("Server")]
    [SerializeField] private string scheme = "http";
    [SerializeField] private string host = "127.0.0.1";
    [SerializeField] private int port = 7450;          // local stack; 7350 is the Nakama default
    [SerializeField] private string serverKey = "defaultkey";

    private IClient _client;
    private ISession _session;
    private ISocket _socket;

    public ISocket Socket => _socket;
    public bool IsConnected => _socket != null && _socket.IsConnected;

    /// <summary>Server-assigned name, e.g. "SpryCrane15".</summary>
    public string DisplayName { get; private set; } = "";

    /// <summary>Index into AvatarLibrary, assigned by the server on first login.</summary>
    public int AvatarIndex { get; private set; }

    /// <summary>Raised after a successful ConnectAsync, on the main thread.</summary>
    public event Action OnConnected;

    /// <summary>Raised when the socket drops, with the reason the server gave.</summary>
    public event Action<string> OnDisconnected;

    /// <summary>
    /// Raised for every match message: the opcode (see UnoOpCodes) and the JSON
    /// body. Fires on the main thread, so handlers may touch the UI.
    /// </summary>
    public event Action<long, string> OnMatchState;

    /// <summary>The match this client is in, or null.</summary>
    public string CurrentMatchId { get; private set; }

    private const string DeviceIdKey = "uno.deviceId";
    private const string AuthTokenKey = "uno.authToken";
    private const string RefreshTokenKey = "uno.refreshToken";

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// Authenticates by device id, opens the socket and reads the account.
    /// Returns false if the server can't be reached; the caller shows the error.
    /// </summary>
    public async Task<bool> ConnectAsync()
    {
        try
        {
            _client = new Client(scheme, host, port, serverKey, UnityWebRequestAdapter.Instance);

            _session = await RestoreOrAuthenticateAsync();
            SaveSession(_session);

            // useMainThread: true makes the package raise socket events on Unity's
            // main thread, so handlers can touch the UI directly.
            _socket = _client.NewSocket(useMainThread: true);
            _socket.Closed += HandleSocketClosed;
            _socket.ReceivedMatchState += HandleMatchState;
            await _socket.ConnectAsync(_session);

            var account = await _client.GetAccountAsync(_session);
            DisplayName = account.User.DisplayName;
            AvatarIndex = ParseAvatarIndex(account.User.Metadata);

            Debug.Log($"Connected as {DisplayName} (avatar {AvatarIndex}, user {_session.UserId})");
            OnConnected?.Invoke();
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Nakama connect failed: {e.Message}");
            return false;
        }
    }

    /// <summary>Restores the saved session, refreshes it, or logs in again.</summary>
    private async Task<ISession> RestoreOrAuthenticateAsync()
    {
        var authToken = PlayerPrefs.GetString(AuthTokenKey, null);
        var refreshToken = PlayerPrefs.GetString(RefreshTokenKey, null);

        if (!string.IsNullOrEmpty(authToken))
        {
            var restored = Session.Restore(authToken, refreshToken);
            if (!restored.IsExpired)
                return restored;

            if (!restored.IsRefreshExpired)
                return await _client.SessionRefreshAsync(restored);
        }

        return await _client.AuthenticateDeviceAsync(GetOrCreateDeviceId());
    }

    private static void SaveSession(ISession session)
    {
        PlayerPrefs.SetString(AuthTokenKey, session.AuthToken);
        PlayerPrefs.SetString(RefreshTokenKey, session.RefreshToken);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// PlayerPrefs is shared by every window of this build on one machine, so all
    /// of them would log in as the same account. Pass -deviceId=win2 to give a
    /// window its own identity (Uno.exe -deviceId=win2).
    /// Nakama requires 10-128 characters, hence the prefix.
    /// </summary>
    private static string GetOrCreateDeviceId()
    {
        foreach (var arg in Environment.GetCommandLineArgs())
        {
            const string prefix = "-deviceId=";
            if (arg.StartsWith(prefix))
                return "uno-device-" + arg.Substring(prefix.Length);
        }

        if (!PlayerPrefs.HasKey(DeviceIdKey))
        {
            PlayerPrefs.SetString(DeviceIdKey, "uno-device-" + Guid.NewGuid());
            PlayerPrefs.Save();
        }

        return PlayerPrefs.GetString(DeviceIdKey);
    }

    /// <summary>Account metadata is JSON: {"avatar":7}. Missing or bad data means 0.</summary>
    private static int ParseAvatarIndex(string metadata)
    {
        if (string.IsNullOrEmpty(metadata))
            return 0;

        try
        {
            return JsonUtility.FromJson<AccountMetadata>(metadata).avatar;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    [Serializable]
    private class AccountMetadata
    {
        public int avatar;
    }

    /// <summary>Calls a server RPC so views don't need the client or session.</summary>
    public async Task<IApiRpc> RpcAsync(string rpcId, string payload = "{}")
    {
        return await _client.RpcAsync(_session, rpcId, payload);
    }

    // ---------- matches ----------

    /// <summary>
    /// Asks the server for a match with a free seat, creating one if needed.
    /// Returns the match id, or null if the call failed.
    /// </summary>
    public async Task<string> FindMatchAsync()
    {
        return await MatchIdRpcAsync("find_match");
    }

    /// <summary>
    /// Returns the match this account still holds a seat in, or null. Used after
    /// a relaunch: no match means go to the home screen.
    /// </summary>
    public async Task<string> CurrentMatchAsync()
    {
        return await MatchIdRpcAsync("current_match");
    }

    private async Task<string> MatchIdRpcAsync(string rpcId)
    {
        try
        {
            var response = await RpcAsync(rpcId);
            var payload = JsonUtility.FromJson<MatchIdResponse>(response.Payload);
            return string.IsNullOrEmpty(payload?.matchId) ? null : payload.matchId;
        }
        catch (Exception e)
        {
            Debug.LogError($"{rpcId} failed: {e.Message}");
            return null;
        }
    }

    /// <summary>Joins a match. The server replies with the current state.</summary>
    public async Task<bool> JoinMatchAsync(string matchId)
    {
        try
        {
            var match = await _socket.JoinMatchAsync(matchId);
            CurrentMatchId = match.Id;
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Join match failed: {e.Message}");
            return false;
        }
    }

    /// <summary>Leaves the current match, if any. Safe to call twice.</summary>
    public async Task LeaveMatchAsync()
    {
        if (string.IsNullOrEmpty(CurrentMatchId))
            return;

        var matchId = CurrentMatchId;
        CurrentMatchId = null;

        try
        {
            await _socket.LeaveMatchAsync(matchId);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Leave match failed: {e.Message}");
        }
    }

    /// <summary>
    /// Sends one action to the match, e.g.
    /// SendMatchStateAsync(UnoOpCodes.PlayCard, "{\"cardId\":57}").
    /// </summary>
    public async Task SendMatchStateAsync(long opCode, string json = "{}")
    {
        if (string.IsNullOrEmpty(CurrentMatchId))
        {
            Debug.LogWarning($"Dropped opcode {opCode}: not in a match");
            return;
        }

        await _socket.SendMatchStateAsync(CurrentMatchId, opCode, json);
    }

    private void HandleMatchState(IMatchState state)
    {
        var json = state.State == null ? "{}" : Encoding.UTF8.GetString(state.State);
        OnMatchState?.Invoke(state.OpCode, json);
    }

    // ---------- connection lifecycle ----------

    private void HandleSocketClosed(string reason)
    {
        Debug.LogWarning($"Nakama socket closed: {reason}");
        CurrentMatchId = null;
        OnDisconnected?.Invoke(reason);
    }

    [Serializable]
    private class MatchIdResponse
    {
        public string matchId;
    }

    private async void OnDestroy()
    {
        if (_socket != null)
        {
            _socket.Closed -= HandleSocketClosed;
            _socket.ReceivedMatchState -= HandleMatchState;
            if (_socket.IsConnected)
                await _socket.CloseAsync();
        }
    }
}
