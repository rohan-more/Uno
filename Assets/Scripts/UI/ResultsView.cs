using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The results screen, Card Party style: one RankView row per player in place
/// order, with place, avatar, name, cards left and coins won. No points: the
/// penalty only ever breaks ties between equal card counts, and is never shown.
///
/// Opens a moment after GAME_OVER (so the last card lands first), or at once
/// when rejoining a match that's already over. Close, or the countdown running
/// out, leaves the match and goes home, where the balance counts up the payout.
///
/// Put it on an object in MatchScene that stays active (e.g. the canvas), not on
/// the panel itself: an inactive object never hears the game end. Point Panel
/// at the results panel, which starts inactive.
/// </summary>
public class ResultsView : MonoBehaviour
{
    [SerializeField] private MatchPresenter presenter;
    [Tooltip("What opens and closes. Start it inactive.")]
    [SerializeField] private GameObject panel;

    [Header("Rows")]
    [Tooltip("The RankRow prefab (or a row in the scene to copy).")]
    [SerializeField] private RankView rankPrefab;
    [Tooltip("Where the rows go, top to bottom, e.g. a Vertical Layout Group.")]
    [SerializeField] private Transform rowContainer;

    [Header("Header (optional)")]
    [Tooltip("\"You won!\", \"You came 2nd\"...")]
    [SerializeField] private TMP_Text titleText;
    [Tooltip("What you won, e.g. \"+120\"; empty in a free match.")]
    [SerializeField] private TMP_Text yourCoinsText;
    [Tooltip("e.g. \"Pot 200\"; empty in a free match.")]
    [SerializeField] private TMP_Text potText;

    [Header("Leaving")]
    [SerializeField] private Button closeButton;
    [Tooltip("Optional: \"Back to lobby in 12\".")]
    [SerializeField] private TMP_Text countdownText;
    [Tooltip("Goes home by itself after this long; keep it under the server's postGameCloseSeconds (30).")]
    [SerializeField] private float autoCloseSeconds = 20f;

    [Header("Timing")]
    [Tooltip("Lets the winning card land before the screen opens.")]
    [SerializeField] private float openDelay = 1.2f;
    [Tooltip("Rows appear one after another, this far apart.")]
    [SerializeField] private float rowInterval = 0.15f;

    private static AvatarLibrary avatars => Services.Assets.Avatars;

    private readonly List<RankView> rows = new();
    private float closeAt = -1f;
    private bool shown;
    private bool closing;

    private void Awake()
    {
        if (panel != null)
            panel.SetActive(false);
        // A row left in the scene as the template stays out of sight
        if (rankPrefab != null && rankPrefab.gameObject.scene.IsValid())
            rankPrefab.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        if (presenter != null)
        {
            presenter.OnGameOver += HandleGameOver;
            presenter.OnSnapshot += HandleSnapshot;
        }
        if (closeButton != null)
            closeButton.onClick.AddListener(Close);
    }

    private void OnDisable()
    {
        if (presenter != null)
        {
            presenter.OnGameOver -= HandleGameOver;
            presenter.OnSnapshot -= HandleSnapshot;
        }
        if (closeButton != null)
            closeButton.onClick.RemoveListener(Close);
    }

    private void HandleGameOver(int[] ranking) => StartCoroutine(OpenAfter(openDelay));

    // Rejoining a match that already ended: the snapshot has the results.
    private void HandleSnapshot(MatchState state)
    {
        if (state.IsOver && state.Results.Length > 0)
            StartCoroutine(OpenAfter(0f));
    }

    private IEnumerator OpenAfter(float delay)
    {
        if (shown)
            yield break;
        shown = true;

        if (delay > 0f)
            yield return new WaitForSeconds(delay);
        Show(presenter.State);
    }

    private void Show(MatchState state)
    {
        if (panel != null)
            panel.SetActive(true);
        closeAt = Time.unscaledTime + autoCloseSeconds;

        var results = state.Results;
        bool staked = state.Stake > 0;

        foreach (var old in rows)
            Destroy(old.gameObject);
        rows.Clear();

        if (rankPrefab == null || rowContainer == null)
            Debug.LogError("ResultsView needs a Rank Prefab and a Row Container");
        else
        {
            for (int i = 0; i < results.Length; i++)
            {
                var r = results[i];
                var seat = state.SeatAt(r.seat);
                bool you = r.seat == state.YourSeat;

                var row = Instantiate(rankPrefab, rowContainer);
                row.gameObject.SetActive(false); // revealed in turn below
                row.Bind(
                    place: r.place,
                    rank: (SharesPlace(results, i) ? "=" : "") + r.place,
                    playerName: seat?.Name ?? "",
                    avatarSprite: seat != null && avatars != null ? avatars.Get(seat.Avatar) : null,
                    cardsLeft: r.cardsLeft,
                    coinsWon: r.coins,
                    staked: staked,
                    isYou: you);
                rows.Add(row);
            }
        }

        var mine = Array.Find(results, r => r.seat == state.YourSeat);
        SetText(titleText, mine == null ? "Game over"
            : mine.place == 1 ? "You won!"
            : $"You came {Ordinal(mine.place)}");
        SetText(yourCoinsText, staked && mine != null ? $"+{CoinFormat.Short(mine.coins)}" : "");
        SetText(potText, staked ? $"Pot {CoinFormat.Short(state.Pot)}" : "");

        StartCoroutine(RevealRows());
    }

    // Rows pop in top to bottom, each counting up its coins as it arrives.
    private IEnumerator RevealRows()
    {
        foreach (var row in rows)
        {
            row.gameObject.SetActive(true);
            row.CountCoins();
            if (rowInterval > 0f)
                yield return new WaitForSecondsRealtime(rowInterval);
        }
    }

    private void Update()
    {
        if (closeAt < 0f || closing)
            return;

        float left = closeAt - Time.unscaledTime;
        SetText(countdownText, $"Back to lobby in {Mathf.CeilToInt(Mathf.Max(0f, left))}");
        if (left <= 0f)
            Close();
    }

    private void Close()
    {
        if (closing)
            return;
        closing = true;

        if (Services.Connection != null)
            _ = Services.Connection.LeaveMatchAsync();

        var loader = FindObjectOfType<MatchSceneLoader>();
        if (loader != null)
            loader.ReturnHome();
        else
            Debug.LogError("No MatchSceneLoader to go home with");
    }

    private static bool SharesPlace(ResultMsg[] results, int i) =>
        (i > 0 && results[i - 1].place == results[i].place) ||
        (i + 1 < results.Length && results[i + 1].place == results[i].place);

    /// <summary>1st, 2nd, 3rd, 4th.</summary>
    private static string Ordinal(int n) => n switch
    {
        1 => "1st",
        2 => "2nd",
        3 => "3rd",
        _ => $"{n}th",
    };

    private static void SetText(TMP_Text text, string value)
    {
        if (text != null)
            text.text = value;
    }
}
