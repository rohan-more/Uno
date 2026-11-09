using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// Testing tools behind F1. Put this on the debug canvas in the Startup scene; it
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

    [Header("Match")]
    [Tooltip("The players in table order (you, then clockwise). Filled in during a match.")]
    [SerializeField] private TMP_Dropdown playerDropdown;
    [Tooltip("The chosen player wins now; the others are ranked by their real hands.")]
    [SerializeField] private Button instantWinButton;
    [Tooltip("The chosen player is left holding one Wild, and wins on their next turn.")]
    [SerializeField] private Button oneCardButton;
    [Tooltip("Which special card Give Card hands the chosen player. Filled in at start.")]
    [SerializeField] private TMP_Dropdown cardDropdown;
    [Tooltip("Swaps one of the chosen player's cards for that special card, playable now if it can be.")]
    [SerializeField] private Button giveCardButton;

    [Header("Optional")]
    [SerializeField] private Button closeButton;

    private static DebugMenu instance;

    private static IConnectionService Connection => Services.Connection;

    // The dropdown's options, as server seats, and what they were built from
    private readonly List<int> dropdownSeats = new();
    private string dropdownKey = "";
    private MatchPresenter presenter;
    private MatchSeatsView seatsView;

    private void Awake()
    {
        // A second copy (e.g. a scene loaded again) keeps the first menu
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);

        if (panel != null)
            panel.SetActive(false);

        if (cardDropdown != null)
        {
            cardDropdown.ClearOptions();
            cardDropdown.AddOptions(new List<string>(System.Array.ConvertAll(GiftCards, g => g.label)));
        }
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
        if (instantWinButton != null)
            instantWinButton.onClick.AddListener(InstantWin);
        if (oneCardButton != null)
            oneCardButton.onClick.AddListener(DownToOneCard);
        if (giveCardButton != null)
            giveCardButton.onClick.AddListener(GiveCard);
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
        if (instantWinButton != null)
            instantWinButton.onClick.RemoveListener(InstantWin);
        if (oneCardButton != null)
            oneCardButton.onClick.RemoveListener(DownToOneCard);
        if (giveCardButton != null)
            giveCardButton.onClick.RemoveListener(GiveCard);
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

        RefreshPlayers(inMatch);
    }

    // ---- match ----

    /// <summary>The match being played, if this scene has one.</summary>
    private MatchState PlayingState()
    {
        if (presenter == null)
            presenter = FindObjectOfType<MatchPresenter>();
        var state = presenter != null ? presenter.State : null;
        return state != null && state.Seats.Length > 0 && state.HasStarted && !state.IsOver ? state : null;
    }

    private void RefreshPlayers(bool inMatch)
    {
        var state = inMatch && Connection.IsConnected ? PlayingState() : null;

        if (instantWinButton != null)
            instantWinButton.interactable = state != null;
        if (oneCardButton != null)
            oneCardButton.interactable = state != null;
        if (giveCardButton != null)
            giveCardButton.interactable = state != null;
        if (cardDropdown != null)
            cardDropdown.interactable = state != null;
        if (playerDropdown == null)
            return;
        playerDropdown.interactable = state != null;

        // Table order from you, clockwise, each with where they sit on screen,
        // and the player just before you marked: what they play lands on you.
        // Only rebuilt when that changes, so a choice isn't reset every frame.
        var labels = new List<string>();
        var seats = new List<int>();
        if (state != null)
        {
            int n = state.Seats.Length;
            int beforeYou = ((state.YourSeat - state.Direction) % n + n) % n;
            for (int offset = 0; offset < n; offset++)
            {
                int seat = (state.YourSeat + offset) % n;
                var name = state.SeatAt(seat)?.Name ?? $"Seat {seat}";
                var label = offset == 0 ? $"You ({name})" : $"{Where(seat)}: {name}";
                if (seat == beforeYou && offset != 0)
                    label += " (plays before you)";
                labels.Add(label);
                seats.Add(seat);
            }
        }

        var key = string.Join("|", labels);
        if (key == dropdownKey)
            return;

        // Keep the same player chosen if they're still there
        int chosen = playerDropdown.value < dropdownSeats.Count ? dropdownSeats[playerDropdown.value] : -1;
        dropdownKey = key;
        dropdownSeats.Clear();
        dropdownSeats.AddRange(seats);
        playerDropdown.ClearOptions();
        playerDropdown.AddOptions(labels.Count > 0 ? labels : new List<string> { "Not in a match" });
        playerDropdown.SetValueWithoutNotify(Mathf.Max(0, seats.IndexOf(chosen)));
    }

    // Where a server seat sits on this screen, e.g. "Top-left", from the match's seat panels
    private string Where(int serverSeat)
    {
        if (seatsView == null)
            seatsView = FindObjectOfType<MatchSeatsView>();
        if (seatsView == null)
            return $"Seat {serverSeat}";

        return seatsView.SeatPositionFor(serverSeat) switch
        {
            PlayerSeat.BottomPlayer => "Bottom",
            PlayerSeat.BottomLeftPlayer => "Bottom-left",
            PlayerSeat.BottomRightPlayer => "Bottom-right",
            PlayerSeat.TopPlayer => "Top",
            PlayerSeat.TopLeftPlayer => "Top-left",
            PlayerSeat.TopRightPlayer => "Top-right",
            PlayerSeat.RightPlayer => "Right",
            var other => other.ToString(),
        };
    }

    private bool TryChosenSeat(out int seat, out string name)
    {
        seat = -1;
        name = "";
        if (playerDropdown == null || playerDropdown.value >= dropdownSeats.Count)
            return false;
        seat = dropdownSeats[playerDropdown.value];
        name = playerDropdown.options[playerDropdown.value].text;
        return true;
    }

    private void InstantWin() => SendRig(UnoOpCodes.DebugWin, "wins now");

    private void DownToOneCard() => SendRig(UnoOpCodes.DebugOneCard, "is down to one Wild");

    // The special cards Give Card offers, as the dropdown shows them and the server names them
    private static readonly (string label, string card)[] GiftCards =
    {
        ("Draw Two", "DRAW_TWO"),
        ("Wild Draw Four", "WILD_DRAW_FOUR"),
        ("Skip", "SKIP"),
        ("Reverse", "REVERSE"),
        ("Wild", "WILD"),
    };

    private void GiveCard()
    {
        int i = cardDropdown != null ? cardDropdown.value : 0;
        var gift = GiftCards[Mathf.Clamp(i, 0, GiftCards.Length - 1)];
        SendRig(UnoOpCodes.DebugGiveCard, $"gets a {gift.label}", gift.card);
    }

    // Problems (debug tools off, game already over) come back as match errors
    // and show in the console.
    private async void SendRig(long opCode, string what, string card = null)
    {
        if (!TryChosenSeat(out var seat, out var name))
        {
            SetText(statusText, "Pick a player first");
            return;
        }

        await Connection.SendMatchStateAsync(opCode, JsonUtility.ToJson(new DebugSeatReq { seat = seat, card = card }));
        SetText(statusText, $"Asked: {name} {what}");
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
