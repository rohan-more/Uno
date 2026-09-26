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
            _socket = _client.NewSocket(useMainThread: true);
            _socket.Closed += HandleSocketClosed;
            _socket.ReceivedMatchState += HandleMatchState;
            await _socket.ConnectAsync(_session);

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

        // Frees the profile slot for the next window. A killed process releases
        // it too, since Windows closes the handle.
        _profileLock?.Dispose();
        _profileLock = null;
        _deviceId = null;
    }
}
