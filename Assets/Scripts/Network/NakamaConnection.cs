using System;
using System.IO;
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

    [Header("Testing")]
    [Tooltip("Each window on this PC claims its own profile, so several builds " +
             "run side by side as different players. Turn off for a real build.")]
    [SerializeField] private bool multiInstanceProfiles = true;

    [SerializeField] private int maxProfiles = 8;

    [Tooltip("Skip matchmaking: Find Match deals immediately against three bots, " +
             "with no 12 second countdown. Turn off for a real build.")]
    [SerializeField] private bool instantMatch;

    private IClient _client;
    private ISession _session;
    private ISocket _socket;

    public ISocket Socket => _socket;
    public bool IsConnected => _socket != null && _socket.IsConnected;

    /// <summary>Server-assigned name, e.g. "SpryCrane15".</summary>
    public string DisplayName { get; private set; } = "";

    /// <summary>Index into AvatarLibrary, assigned by the server on first login.</summary>
    public int AvatarIndex { get; private set; }

    /// <summary>This account's Nakama user id, used to spot yourself in a seat list.</summary>
    public string UserId => _session?.UserId;

    /// <summary>Raised after a successful ConnectAsync, on the main thread.</summary>
    public event Action OnConnected;

    /// <summary>Raised when the socket drops, with the reason the server gave. Reconnecting starts at once.</summary>
    public event Action<string> OnDisconnected;

    /// <summary>Raised when a dropped socket is back. Rejoining the match follows on its own.</summary>
    public event Action OnReconnected;

    /// <summary>Raised after reconnecting when the match we dropped out of can't be rejoined: it ended, or our seat is gone.</summary>
    public event Action OnMatchLost;

    /// <summary>
    /// Raised for every match message: the opcode (see UnoOpCodes) and the JSON
    /// body. Fires on the main thread, so handlers may touch the UI.
    /// </summary>
    public event Action<long, string> OnMatchState;

    /// <summary>The match this client is in, or null.</summary>
    public string CurrentMatchId { get; private set; }

    /// <summary>True from a dropped socket until it is connected again.</summary>
    public bool IsReconnecting { get; private set; }

    // The match to go back to after a drop. Cleared by leaving on purpose.
    private string _rejoinMatchId;

    // Set while the app is closing, so the socket closing doesn't trigger a reconnect.
    private bool _shuttingDown;

    // Seconds between reconnect attempts; the last one repeats.
    private static readonly float[] ReconnectDelays = { 1f, 2f, 4f, 8f, 10f };

    private const string DeviceIdKey = "uno.deviceId";
    private const string AuthTokenKey = "uno.authToken";
    private const string RefreshTokenKey = "uno.refreshToken";

    /// <summary>Held open for the app's lifetime; see ClaimProfileDeviceId.</summary>
    private static FileStream _profileLock;

    /// <summary>Worked out once, then reused by every call.</summary>
    private static string _deviceId;

    // Static copies of the inspector settings, so the device id can be resolved
    // from static helpers.
    private static bool _multiInstanceProfiles;
    private static int _maxProfiles;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        _multiInstanceProfiles = multiInstanceProfiles;
        _maxProfiles = Mathf.Max(1, maxProfiles);
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
            await OpenSocketAsync();

            var account = await _client.GetAccountAsync(_session);
            DisplayName = account.User.DisplayName;
            AvatarIndex = ParseAvatarIndex(account.User.Metadata);

            Debug.Log($"Connected as {DisplayName} (avatar {AvatarIndex}, device {GetOrCreateDeviceId()}, user {_session.UserId})");
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
        var deviceId = GetOrCreateDeviceId();

        var authToken = PlayerPrefs.GetString(AuthTokenKey + deviceId, null);
        var refreshToken = PlayerPrefs.GetString(RefreshTokenKey + deviceId, null);

        if (!string.IsNullOrEmpty(authToken))
        {
            var restored = Session.Restore(authToken, refreshToken);
            if (!restored.IsExpired)
                return restored;

            if (!restored.IsRefreshExpired)
                return await _client.SessionRefreshAsync(restored);
        }

        return await _client.AuthenticateDeviceAsync(deviceId);
    }

    /// <summary>
    /// Saved per device id. PlayerPrefs is shared by every window of this build
    /// on one machine, so a single key would make all four windows restore the
    /// first window's session and log in as the same player.
    /// </summary>
    private static void SaveSession(ISession session)
    {
        var deviceId = GetOrCreateDeviceId();
        PlayerPrefs.SetString(AuthTokenKey + deviceId, session.AuthToken);
        PlayerPrefs.SetString(RefreshTokenKey + deviceId, session.RefreshToken);
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Which account this window logs in as. PlayerPrefs is shared by every
    /// window of this build on one machine, so without this they would all be
    /// the same player. In order:
    ///   1. -deviceId=win2 on the command line, when you want a specific account
    ///   2. a free profile slot, claimed automatically (see ClaimProfileDeviceId)
    ///   3. the saved id, the normal single-window case
    /// Nakama requires 10-128 characters, hence the prefix.
    /// </summary>
    private static string GetOrCreateDeviceId()
    {
        if (_deviceId != null)
            return _deviceId;

        const string flag = "-deviceId";
        var args = Environment.GetCommandLineArgs();

        for (int i = 0; i < args.Length; i++)
        {
            // Accepts both "-deviceId=win1" and "-deviceId win1".
            if (args[i].StartsWith(flag + "="))
                return _deviceId = "uno-device-" + args[i].Substring(flag.Length + 1);

            if (args[i] == flag && i + 1 < args.Length)
                return _deviceId = "uno-device-" + args[i + 1];
        }

        if (_multiInstanceProfiles)
        {
            var claimed = ClaimProfileDeviceId(_maxProfiles);
            if (claimed != null)
                return _deviceId = claimed;
        }

        if (!PlayerPrefs.HasKey(DeviceIdKey))
        {
            PlayerPrefs.SetString(DeviceIdKey, "uno-device-" + Guid.NewGuid());
            PlayerPrefs.Save();
        }

        return _deviceId = PlayerPrefs.GetString(DeviceIdKey);
    }

    /// <summary>
    /// Takes the first profile no other window is using, so double-clicking the
    /// build several times gives you several different players with no command
    /// line. The claim is a lock file held open until this process exits; the
    /// next window finds it locked and moves on to the next number. The file
    /// deletes itself on close, so nothing accumulates.
    /// Returns null if every slot is taken.
    /// </summary>
    private static string ClaimProfileDeviceId(int maxProfiles)
    {
        for (int i = 1; i <= maxProfiles; i++)
        {
            var path = Path.Combine(Application.persistentDataPath, $"profile{i}.lock");
            try
            {
                _profileLock = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                    FileShare.None, 8, FileOptions.DeleteOnClose);
                return $"uno-device-profile{i}";
            }
            catch (IOException)
            {
                // Another window holds this one; try the next.
            }
        }

        Debug.LogWarning($"All {maxProfiles} test profiles are in use; falling back to the saved id");
        return null;
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
    ///
    /// With instantMatch on, it asks for a private match that deals straight
    /// away against bots instead, so testing doesn't mean sitting through the
    /// matchmaking countdown.
    /// </summary>
    public async Task<string> FindMatchAsync()
    {
        if (instantMatch)
        {
            Debug.LogWarning("instantMatch is on: skipping matchmaking and playing bots");
            return await MatchIdRpcAsync("quick_match");
        }

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
            _rejoinMatchId = match.Id;
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
        _rejoinMatchId = null; // leaving on purpose: don't come back here after a drop

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

    private async Task OpenSocketAsync()
    {
        if (_socket != null)
        {
            _socket.Closed -= HandleSocketClosed;
            _socket.ReceivedMatchState -= HandleMatchState;
        }

        // useMainThread: true makes the package raise socket events on Unity's
        // main thread, so handlers can touch the UI directly.
        _socket = _client.NewSocket(useMainThread: true);
        _socket.Closed += HandleSocketClosed;
        _socket.ReceivedMatchState += HandleMatchState;
        await _socket.ConnectAsync(_session);
    }

    private void HandleSocketClosed(string reason)
    {
        Debug.LogWarning($"Nakama socket closed: {reason}");
        CurrentMatchId = null; // _rejoinMatchId remembers where we were
        OnDisconnected?.Invoke(reason);

        if (!_shuttingDown && !IsReconnecting)
            ReconnectLoop();
    }

    /// <summary>
    /// Keeps trying to reopen the socket after a drop, backing off a little each
    /// time, then rejoins the match we were in. The server holds our seat until
    /// we miss 3 turns, so there's time.
    /// </summary>
    private async void ReconnectLoop()
    {
        IsReconnecting = true;
        for (int attempt = 0; !_shuttingDown; attempt++)
        {
            float delay = ReconnectDelays[Mathf.Min(attempt, ReconnectDelays.Length - 1)];
            await Task.Delay(TimeSpan.FromSeconds(delay));
            if (_shuttingDown)
                break;

            try
            {
                if (_session == null || _session.HasExpired(DateTime.UtcNow.AddMinutes(1)))
                {
                    _session = await RestoreOrAuthenticateAsync();
                    SaveSession(_session);
                }

                await OpenSocketAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Reconnect attempt {attempt + 1} failed: {e.Message}");
                continue;
            }

            Debug.Log($"Reconnected after {attempt + 1} attempt(s)");
            IsReconnecting = false;
            OnReconnected?.Invoke();

            // Back to the match we dropped out of; if it's gone (or our seat
            // went to a bot), ask the server where we stand.
            var matchId = _rejoinMatchId;
            if (string.IsNullOrEmpty(matchId) || !await JoinMatchAsync(matchId))
            {
                _rejoinMatchId = null;
                if (!await RejoinMatchInProgressAsync() && !string.IsNullOrEmpty(matchId))
                    OnMatchLost?.Invoke();
            }
            return;
        }
        IsReconnecting = false;
    }

    /// <summary>
    /// The match being played that we still hold a seat in, or null. Joining it
    /// gets a fresh GAME_STATE, which opens the match scene (see MatchSceneLoader).
    /// </summary>
    public async Task<string> MatchInProgressAsync()
    {
        try
        {
            var response = await RpcAsync("current_match");
            var current = JsonUtility.FromJson<CurrentMatchResponse>(response.Payload);
            if (current == null || string.IsNullOrEmpty(current.matchId) || current.phase != "playing")
                return null;
            return current.matchId;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Checking for a match in progress failed: {e.Message}");
            return null;
        }
    }

    /// <summary>Rejoins the match in progress, if there is one. Returns true if we're back in it.</summary>
    public async Task<bool> RejoinMatchInProgressAsync()
    {
        var matchId = await MatchInProgressAsync();
        if (matchId == null)
            return false;

        Debug.Log($"Rejoining match in progress {matchId}");
        return await JoinMatchAsync(matchId);
    }

    private void OnApplicationQuit()
    {
        _shuttingDown = true;
    }

    [Serializable]
    private class MatchIdResponse
    {
        public string matchId;
    }

    [Serializable]
    private class CurrentMatchResponse
    {
        public string matchId;
        public string phase; // "lobby" | "playing" | "done"
    }

    private async void OnDestroy()
    {
        _shuttingDown = true;
        if (_socket != null)
        {
            _socket.Closed -= HandleSocketClosed;
            _socket.ReceivedMatchState -= HandleMatchState;
            if (_socket.IsConnected)
                await _socket.CloseAsync();
        }

        // Frees the profile slot for the next window. A killed process releases
        // it too, since Windows closes the handle.
        _profileLock?.Dispose();
        _profileLock = null;
        _deviceId = null;
    }
}
