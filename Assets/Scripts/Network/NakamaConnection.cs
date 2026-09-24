using System;
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

    private void HandleSocketClosed(string reason)
    {
        Debug.LogWarning($"Nakama socket closed: {reason}");
        OnDisconnected?.Invoke(reason);
    }

    private async void OnDestroy()
    {
        if (_socket != null)
        {
            _socket.Closed -= HandleSocketClosed;
            if (_socket.IsConnected)
                await _socket.CloseAsync();
        }
    }

    // TODO(you), once the server side exists:
    //  - JoinMatchAsync / LeaveMatchAsync helpers plus an OnMatchData event
    //  - reconnect handling after OnDisconnected
    //  - a Boot-scene flow that connects, then loads the Home scene
    // Socket events arrive on the main thread (see NewSocket above), so these
    // handlers can update UI without a dispatcher.
}
