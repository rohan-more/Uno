using System;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Home screen: shows who you are and starts matchmaking.
/// Talks only to NakamaConnection; it never touches the Nakama client directly.
/// </summary>
public class LobbyView : MonoBehaviour
{
    [Header("Profile")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private Image avatarImage;
    [SerializeField] private AvatarLibrary avatars;

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

    /// <summary>Names longer than this are cut short with an ellipsis.</summary>
    [SerializeField] private int maxNameLength = 12;

    private NakamaConnection Connection => NakamaConnection.Instance;

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
    }

    private void OnDisable()
    {
        playButton.onClick.RemoveListener(OnPlayClicked);
        exitButton.onClick.RemoveListener(OnExitClicked);

        if (cancelButton != null)
            cancelButton.onClick.RemoveListener(OnCancelClicked);

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

        // Boot may already have connected; only connect if it hasn't.
        var connected = Connection.IsConnected || await Connection.ConnectAsync();
        if (!connected)
        {
            SetStatus("Couldn't reach the server");
            return;
        }

        ShowProfile();
        SetStatus("");
        SetInteractable(true);
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
