using TMPro;
using UnityEngine;
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
    [Tooltip("\"Free coins: ready\" or how long until they are.")]
    [SerializeField] private TMP_Text freeCoinsText;
    [Tooltip("How many coins Grant adds. Whole numbers, 1-100000.")]
    [SerializeField] private TMP_InputField grantAmountInput;
    [SerializeField] private Button grantCoinsButton;
    [Tooltip("Makes free coins claimable right now.")]
    [SerializeField] private Button resetFreeCoinsButton;

    [Header("Optional")]
    [SerializeField] private Button closeButton;

    private static DebugMenu instance;

    private static NakamaConnection Connection => NakamaConnection.Instance;

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
        if (resetFreeCoinsButton != null)
            resetFreeCoinsButton.onClick.AddListener(ResetFreeCoins);
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
        if (resetFreeCoinsButton != null)
            resetFreeCoinsButton.onClick.RemoveListener(ResetFreeCoins);
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

        var wallet = Connection.Wallet;
        if (wallet != null && wallet.Loaded)
        {
            SetText(coinsText, $"Coins: {wallet.Coins:N0}");
            SetText(freeCoinsText, wallet.FreeCoinsReady
                ? "Free coins: ready"
                : $"Free coins in {(int)wallet.FreeCoinsIn.TotalHours}h {wallet.FreeCoinsIn.Minutes:00}m {wallet.FreeCoinsIn.Seconds:00}s");
        }

        if (grantCoinsButton != null)
            grantCoinsButton.interactable = Connection.IsConnected;
        if (resetFreeCoinsButton != null)
            resetFreeCoinsButton.interactable = Connection.IsConnected;
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
        if (!int.TryParse(text, out var amount) || amount < 1 || amount > 100000)
        {
            SetText(statusText, "Enter a whole number from 1 to 100000");
            return;
        }

        SetText(statusText, $"Granting {amount:N0} coins\u2026");
        bool ok = await Connection.Wallet.DebugGrantAsync(amount);
        SetText(statusText, ok ? $"Granted {amount:N0} coins"
            : "Grant refused: is the server running with UNO_DEBUG_TOOLS=true? (see the console)");
    }

    private async void ResetFreeCoins()
    {
        bool ok = await Connection.Wallet.DebugResetFreeCoinsAsync();
        SetText(statusText, ok ? "Free coins are claimable now"
            : "Reset refused: is the server running with UNO_DEBUG_TOOLS=true? (see the console)");
    }

    private void Close() => panel.SetActive(false);

    private static void SetText(TMP_Text text, string value)
    {
        if (text != null)
            text.text = value;
    }
}
