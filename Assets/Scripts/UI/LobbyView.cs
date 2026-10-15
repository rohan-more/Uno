using System;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Home screen: shows who you are and starts matchmaking.
/// Talks only to Services.Connection; it never touches the Nakama client directly.
/// </summary>
public class LobbyView : MonoBehaviour
{
    [Header("Profile")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private Image avatarImage;
    private static AvatarLibrary avatars => Services.Assets.Avatars;

    [Header("Buttons")]
    [SerializeField] private Button playButton;
    [SerializeField] private Button exitButton;

    [Header("Searching")]
    [SerializeField] private GameObject searchingPanel;
    [SerializeField] private Button cancelButton;
    [SerializeField] private TMP_Text statusText;

    [Tooltip("The Play button's label. Shows the reconnect text while a match is in progress. " +
             "Found on the button if left empty.")]
    [SerializeField] private TMP_Text playLabel;
    [SerializeField] private string reconnectText = "REJOIN MATCH";

    [Header("Coins")]
    [Tooltip("The balance; counts up or down when it changes.")]
    [SerializeField] private TMP_Text coinsText;
    [SerializeField] private float coinsCountSeconds = 0.8f;
    [Tooltip("Tint while the balance counts up or down. Alpha 0 keeps the text's colour.")]
    [SerializeField] private Color coinsGainColor = new(0.45f, 1f, 0.45f, 1f);
    [SerializeField] private Color coinsLossColor = new(1f, 0.45f, 0.45f, 1f);

    [Tooltip("Optional: \"+70 free coins!\" after a top-up, or the wait while below the line. Empty otherwise.")]
    [SerializeField] private TMP_Text freeCoinsText;
    [Tooltip("{0} is how long until the top-up, e.g. \"2h 13m\".")]
    [SerializeField] private string freeCoinsWaitFormat = "Free coins in {0}";
    [Tooltip("{0} is how many were added.")]
    [SerializeField] private string freeCoinsAddedFormat = "+{0} free coins!";
    [SerializeField] private float freeCoinsAddedSeconds = 3f;

    private float nextCountdownRefresh;
    private NumberCounter coinsCounter;
    private float freeCoinsAddedUntil;
    private long freeCoinsAdded;
    private float nextDueRefresh;

    /// <summary>Names longer than this are cut short with an ellipsis.</summary>
    [SerializeField] private int maxNameLength = 12;

    private static IConnectionService Connection => Services.Connection;

    private void Awake()
    {
        coinsCounter = new NumberCounter(coinsText, CoinFormat.Short)
        {
            Seconds = coinsCountSeconds,
            GainColor = coinsGainColor,
            LossColor = coinsLossColor,
        };
    }

    private void OnEnable()
    {
        playButton.onClick.AddListener(OnPlayClicked);
        exitButton.onClick.AddListener(OnExitClicked);

        if (cancelButton != null)
            cancelButton.onClick.AddListener(OnCancelClicked);

        if (Connection != null)
        {
            Connection.OnDisconnected += OnDisconnected;
            Connection.OnReconnected += OnReconnected;
        }
        WatchWallet();
    }

    private void OnDisable()
    {
        playButton.onClick.RemoveListener(OnPlayClicked);
        exitButton.onClick.RemoveListener(OnExitClicked);

        if (cancelButton != null)
            cancelButton.onClick.RemoveListener(OnCancelClicked);
        
        if (Services.Wallet != null)
        {
            Services.Wallet.OnChanged -= ShowCoins;
            Services.Wallet.OnFreeCoinsAdded -= ShowFreeCoinsAdded;
        }

        if (Connection != null)
        {
            Connection.OnDisconnected -= OnDisconnected;
            Connection.OnReconnected -= OnReconnected;
        }
    }

    private async void Start()
    {
        ShowSearching(false);
        SetInteractable(false);

        if (Connection == null)
        {
            SetStatus("No NakamaConnection in the scene");
            return;
        }

        // Load the card and avatar art while connecting. Boot may already have
        // connected; only connect if it hasn't.
        var assets = Services.Assets.PreloadAsync();
        var connected = Connection.IsConnected || await Connection.ConnectAsync();
        await assets;
        if (!connected)
        {
            SetStatus("Couldn't reach the server");
            return;
        }

        ShowProfile();
        SetStatus("");
        SetInteractable(true);
        WatchWallet(); // the wallet may not have existed yet in OnEnable

        // Back home: a match may have paid out, or free coins may be due
        _ = Services.Wallet?.RefreshAsync();
        await CheckMatchInProgressAsync();
    }

    // A match we still hold a seat in, if any: Play turns into Reconnect.
    private string matchInProgress;
    private string playText;

    private async Task CheckMatchInProgressAsync()
    {
        matchInProgress = await Connection.MatchInProgressAsync();
        if (playLabel == null)
            playLabel = playButton.GetComponentInChildren<TMP_Text>();
        if (playLabel != null)
        {
            playText ??= playLabel.text;
            playLabel.text = matchInProgress != null ? reconnectText : playText;
        }
        SetStatus(matchInProgress != null ? "You have a match in progress" : "");
    }

    /// <summary>Fills in the name and avatar the server gave this account.</summary>
    private void ShowProfile()
    {
        if (nameText != null)
            nameText.text = "Welcome \n\n" + Shorten(Connection.DisplayName, maxNameLength);

        if (avatarImage != null && avatars != null)
            avatarImage.sprite = avatars.Get(Connection.AvatarIndex);
    }

    /// <summary>"SnappyRobin66" with a limit of 8 becomes "SnappyR…".</summary>
    public static string Shorten(string name, int maxLength)
    {
        if (string.IsNullOrEmpty(name) || name.Length <= maxLength)
            return name;

        return name.Substring(0, Mathf.Max(1, maxLength - 1)) + "…";
    }

    private async void OnPlayClicked()
    {
        if (matchInProgress != null)
        {
            SetInteractable(false);
            SetStatus("Rejoining\u2026");
            // On success the server's GAME_STATE opens the match scene
            if (!await Connection.JoinMatchAsync(matchInProgress))
            {
                await CheckMatchInProgressAsync(); // it ended meanwhile: back to Find Match
                SetInteractable(true);
            }
            return;
        }

        SetInteractable(false);
        ShowSearching(true);
        SetStatus("Finding a match…");

        var matchId = await Connection.FindMatchAsync();
        if (matchId == null || !await Connection.JoinMatchAsync(matchId))
        {
            SetStatus("Couldn't join a match");
            ShowSearching(false);
            SetInteractable(true);
            return;
        }

        SetStatus("Waiting for players…");
        // TODO(you): handle the server's LOBBY_STATE (UnoOpCodes.LobbyState) to show
        // the seats and countdown, then load the game scene on GAME_STATE.
    }

    private async void OnCancelClicked()
    {
        await Connection.LeaveMatchAsync();
        ShowSearching(false);
        SetStatus("");
        SetInteractable(true);
    }

    private void OnExitClicked()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ---- coins ----

    private void WatchWallet()
    {
        if (Services.Wallet == null)
            return;

        Services.Wallet.OnChanged -= ShowCoins; // never twice
        Services.Wallet.OnChanged += ShowCoins;
        Services.Wallet.OnFreeCoinsAdded -= ShowFreeCoinsAdded;
        Services.Wallet.OnFreeCoinsAdded += ShowFreeCoinsAdded;
        ShowCoins();
    }

    private void Update()
    {
        coinsCounter.Tick();

        // The free-coins wait moves on its own; redraw it now and then
        if (Time.unscaledTime >= nextCountdownRefresh)
        {
            nextCountdownRefresh = Time.unscaledTime + 1f;
            ShowCoins();
            RefreshIfFreeCoinsDue();
        }
    }

    // The server adds due free coins when the wallet is read, so ask once the
    // wait is over (and not more than every few seconds if it's still settling).
    private void RefreshIfFreeCoinsDue()
    {
        var wallet = Services.Wallet;
        if (wallet == null || !wallet.FreeCoinsWaiting || wallet.FreeCoinsIn > TimeSpan.Zero)
            return;
        if (Connection == null || !Connection.IsConnected || Time.unscaledTime < nextDueRefresh)
            return;

        nextDueRefresh = Time.unscaledTime + 5f;
        _ = wallet.RefreshAsync();
    }

    private void ShowFreeCoinsAdded(long added)
    {
        freeCoinsAdded = added;
        freeCoinsAddedUntil = Time.unscaledTime + freeCoinsAddedSeconds;
        ShowCoins();
    }

    private void ShowCoins()
    {
        var wallet = Services.Wallet;
        if (wallet == null || !wallet.Loaded)
            return;

        coinsCounter.CountTo(wallet.Coins);

        if (freeCoinsText != null)
        {
            if (Time.unscaledTime < freeCoinsAddedUntil)
                freeCoinsText.text = string.Format(freeCoinsAddedFormat, freeCoinsAdded);
            else if (wallet.FreeCoinsWaiting && wallet.FreeCoinsIn > TimeSpan.Zero)
                freeCoinsText.text = string.Format(freeCoinsWaitFormat, CountdownText(wallet.FreeCoinsIn));
            else
                freeCoinsText.text = "";
        }
    }

    /// <summary>"3h 12m", "12m 05s" or "45s".</summary>
    private static string CountdownText(TimeSpan left)
    {
        if (left.TotalHours >= 1)
            return $"{(int)left.TotalHours}h {left.Minutes:00}m";
        if (left.TotalMinutes >= 1)
            return $"{left.Minutes}m {left.Seconds:00}s";
        return $"{Mathf.CeilToInt((float)left.TotalSeconds)}s";
    }
    
    private void OnDisconnected(string reason)
    {
        ShowSearching(false);
        SetInteractable(false);
        SetStatus("Connection lost. Reconnecting\u2026");
    }

    private async void OnReconnected()
    {
        SetInteractable(true);
        SetStatus("");
        await CheckMatchInProgressAsync();
    }

    private void SetInteractable(bool value)
    {
        playButton.interactable = value;
    }

    private void ShowSearching(bool value)
    {
        if (searchingPanel != null)
            searchingPanel.SetActive(value);
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
    }
}
