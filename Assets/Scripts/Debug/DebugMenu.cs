using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// Testing tools behind F1. Put this on the debug canvas in the Boot scene; it
/// keeps the canvas alive across scene loads, and F1 shows the panel only while
/// NakamaConnection's Debug Menu toggle is on.
///
/// Anything that changes the player's account (coins, items) must go through a
/// server debug RPC, which the server refuses unless it runs with
/// UNO_DEBUG_TOOLS=true (set in server/local.yml).
/// </summary>
public class DebugMenu : MonoBehaviour
{
    [Tooltip("What F1 shows and hides. Start it inactive.")]
    [SerializeField] private GameObject panel;

    [Header("Info")]
    [SerializeField] private TMP_Text playerText;
    [SerializeField] private TMP_Text connectionText;
    [SerializeField] private TMP_Text matchText;
    [Tooltip("What the last button did.")]
    [SerializeField] private TMP_Text statusText;

    [Header("Connection")]
    [Tooltip("As if the network died: the client should reconnect and rejoin by itself.")]
    [SerializeField] private Button dropConnectionButton;
    [Tooltip("As if the game crashed: the seat is held, and home should offer REJOIN MATCH.")]
    [SerializeField] private Button goHomeKeepSeatButton;

    [Header("Coins")]
    [SerializeField] private TMP_Text coinsText;
    [Tooltip("Whether free coins are on their way, and when.")]
    [SerializeField] private TMP_Text freeCoinsText;
    [Tooltip("How many coins Grant adds; negative takes them away (not below 0). -100000 to 100000.")]
    [SerializeField] private TMP_InputField grantAmountInput;
    [SerializeField] private Button grantCoinsButton;
    [Tooltip("If below the free-coins line, tops up now instead of after the wait.")]
    [FormerlySerializedAs("resetFreeCoinsButton")]
    [SerializeField] private Button freeCoinsNowButton;

    [Header("Optional")]
    [SerializeField] private Button closeButton;

    private static DebugMenu instance;

    private static IConnectionService Connection => Services.Connection;

    private void Awake()
    {
        // Boot is loaded again when returning home: keep the first menu
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);

        if (panel != null)
            panel.SetActive(false);
    }

    private void OnEnable()
    {
        if (dropConnectionButton != null)
            dropConnectionButton.onClick.AddListener(DropConnection);
        if (goHomeKeepSeatButton != null)
            goHomeKeepSeatButton.onClick.AddListener(GoHomeKeepingSeat);
        if (closeButton != null)
            closeButton.onClick.AddListener(Close);
        if (grantCoinsButton != null)
            grantCoinsButton.onClick.AddListener(GrantCoins);
        if (freeCoinsNowButton != null)
            freeCoinsNowButton.onClick.AddListener(FreeCoinsNow);
    }

    private void OnDisable()
    {
        if (dropConnectionButton != null)
            dropConnectionButton.onClick.RemoveListener(DropConnection);
        if (goHomeKeepSeatButton != null)
            goHomeKeepSeatButton.onClick.RemoveListener(GoHomeKeepingSeat);
        if (closeButton != null)
            closeButton.onClick.RemoveListener(Close);
        if (grantCoinsButton != null)
            grantCoinsButton.onClick.RemoveListener(GrantCoins);
        if (freeCoinsNowButton != null)
            freeCoinsNowButton.onClick.RemoveListener(FreeCoinsNow);
    }

    private void Update()
    {
        // F1 both opens and closes the panel
        if (Input.GetKeyDown(KeyCode.F1) && Connection != null && Connection.DebugMenuEnabled && panel != null)
            panel.SetActive(!panel.activeSelf);
        
        if (panel != null && panel.activeSelf)
            RefreshInfo();
    }

    private void RefreshInfo()
    {
        if (Connection == null)
        {
            SetText(connectionText, "No NakamaConnection");
            return;
        }

        bool inMatch = !string.IsNullOrEmpty(Connection.CurrentMatchId);

        SetText(playerText, Connection.DisplayName);
        SetText(connectionText, Connection.IsReconnecting ? "Reconnecting…"
            : Connection.IsConnected ? "Connected" : "Down");
        SetText(matchText, inMatch ? Connection.CurrentMatchId : "No match");

        if (dropConnectionButton != null)
            dropConnectionButton.interactable = Connection.IsConnected;
        if (goHomeKeepSeatButton != null)
            goHomeKeepSeatButton.interactable = Connection.IsConnected && inMatch;

        var wallet = Services.Wallet;
        if (wallet != null && wallet.Loaded)
        {
            SetText(coinsText, $"Coins: {wallet.Coins:N0}");
            SetText(freeCoinsText, !wallet.FreeCoinsWaiting
                ? $"Free coins: none (only below {wallet.FreeCoinsBelow})"
                : $"Free coins (to {wallet.FreeCoinsUpTo}) in {(int)wallet.FreeCoinsIn.TotalHours}h {wallet.FreeCoinsIn.Minutes:00}m {wallet.FreeCoinsIn.Seconds:00}s");
        }

        if (grantCoinsButton != null)
            grantCoinsButton.interactable = Connection.IsConnected;
        if (freeCoinsNowButton != null)
            freeCoinsNowButton.interactable = Connection.IsConnected;
    }

    private void DropConnection()
    {
        SetText(statusText, "Dropped the socket; watch it reconnect");
        _ = Connection.DebugDropConnectionAsync();
    }

    private async void GoHomeKeepingSeat()
    {
        SetText(statusText, "Left the match without forfeiting");

        // Closing our side of the match looks like a drop to the server: during
        // play it holds the seat. LEAVE_MATCH is the forfeit, and we don't send it.
        await Connection.LeaveMatchAsync();

        var loader = FindObjectOfType<MatchSceneLoader>();
        if (loader != null)
            loader.ReturnHome();
    }

    private async void GrantCoins()
    {
        var text = grantAmountInput != null ? grantAmountInput.text : "";
        if (!int.TryParse(text, out var amount) || amount == 0 || amount < -100000 || amount > 100000)
        {
            SetText(statusText, "Enter a whole number from -100000 to 100000, not 0");
            return;
        }

        string what = amount > 0 ? $"Granted {amount:N0} coins" : $"Took up to {-amount:N0} coins";
        bool ok = await Services.Wallet.DebugGrantAsync(amount);
        SetText(statusText, ok ? what
            : "Grant refused: is the server running with UNO_DEBUG_TOOLS=true? (see the console)");
    }

    private async void FreeCoinsNow()
    {
        var wallet = Services.Wallet;
        long before = wallet.Coins;
        bool ok = await wallet.DebugFreeCoinsNowAsync();
        if (!ok)
            SetText(statusText, "Refused: is the server running with UNO_DEBUG_TOOLS=true? (see the console)");
        else if (wallet.Coins > before)
            SetText(statusText, $"Topped up {wallet.Coins - before:N0} free coins");
        else
            SetText(statusText, $"Nothing to top up: free coins only come below {wallet.FreeCoinsBelow}");
    }

    private void Close() => panel.SetActive(false);

    private static void SetText(TMP_Text text, string value)
    {
        if (text != null)
            text.text = value;
    }
}
