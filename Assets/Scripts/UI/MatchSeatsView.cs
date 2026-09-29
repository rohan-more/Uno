using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Fills the nameplate at each seat: avatar, name and card count, plus whose
/// turn it is. It only reads MatchPresenter.State, so it never decides anything.
///
/// Panels are listed in turn order relative to you: entry 0 is your own seat,
/// entry 1 the player after you, and so on. That way every player sees
/// themselves at the bottom.
/// </summary>
public class MatchSeatsView : MonoBehaviour
{
    [Serializable]
    public class SeatPanel
    {
        [Tooltip("Where this panel sits at the table. Card animations use it to " +
                 "fly cards to the right place.")]
        public PlayerSeat seat;

        public Image avatar;
        public TMP_Text nameText;

        [Tooltip("Optional: how many cards this player holds.")]
        public TMP_Text cardCountText;

        [Tooltip("Optional: shown while it is this player's turn.")]
        public GameObject turnHighlight;

        [Tooltip("Optional: shown when they finish, e.g. \"1st\".")]
        public TMP_Text placeText;

        [Tooltip("Optional: dimmed while they are disconnected.")]
        public CanvasGroup group;
    }

    [SerializeField] private MatchPresenter presenter;
    [SerializeField] private AvatarLibrary avatars;

    [Tooltip("Seat panels in turn order: 0 is you, 1 is the player after you.")]
    [SerializeField] private SeatPanel[] panels = new SeatPanel[4];

    [SerializeField] private int maxNameLength = 12;
    [SerializeField] private float disconnectedAlpha = 0.5f;

    private void OnEnable()
    {
        if (presenter == null)
            return;

        presenter.OnSnapshot += HandleSnapshot;
        presenter.OnCardPlayed += _ => Refresh();
        presenter.OnCardsDrawn += _ => Refresh();
        presenter.OnTurnStarted += _ => Refresh();
        presenter.OnPlayerFinished += (_, __) => Refresh();
        presenter.OnPlayerConnection += (_, __) => Refresh();
        presenter.OnSeatGivenToBot += _ => Refresh();
    }

    private void OnDisable()
    {
        if (presenter != null)
            presenter.OnSnapshot -= HandleSnapshot;
        // The lambdas above live as long as this object; disabling stops Refresh
        // from doing anything meaningful because the panels are inactive.
    }

    private void HandleSnapshot(MatchState state) => Refresh();

    /// <summary>Redraws every panel from the current state.</summary>
    public void Refresh()
    {
        var state = presenter != null ? presenter.State : null;
        if (state?.Seats == null || state.Seats.Length == 0)
            return;

        foreach (var seat in state.Seats)
        {
            var panel = PanelFor(state, seat.Index);
            if (panel == null)
                continue;

            if (panel.nameText != null)
                panel.nameText.text = LobbyView.Shorten(seat.Name, maxNameLength);

            if (panel.avatar != null && avatars != null)
                panel.avatar.sprite = avatars.Get(seat.Avatar);

            if (panel.cardCountText != null)
                panel.cardCountText.text = seat.CardCount.ToString();

            if (panel.turnHighlight != null)
                panel.turnHighlight.SetActive(seat.Index == state.CurrentSeat && !seat.HasFinished);

            if (panel.placeText != null)
            {
                panel.placeText.gameObject.SetActive(seat.HasFinished);
                if (seat.HasFinished)
                    panel.placeText.text = Ordinal(seat.Place);
            }

            if (panel.group != null)
                panel.group.alpha = seat.Connected ? 1f : disconnectedAlpha;
        }
    }

    /// <summary>The panel showing a server seat, using its distance from yours.</summary>
    private SeatPanel PanelFor(MatchState state, int seat)
    {
        var offset = state.OffsetFromYou(seat);
        return offset >= 0 && offset < panels.Length ? panels[offset] : null;
    }

    /// <summary>
    /// Where a server seat sits at this player's table, e.g. for flying a played
    /// card from the right position. Falls back to the bottom seat.
    /// </summary>
    public PlayerSeat SeatPositionFor(int serverSeat)
    {
        var state = presenter != null ? presenter.State : null;
        if (state == null)
            return PlayerSeat.BottomPlayer;

        var panel = PanelFor(state, serverSeat);
        return panel != null ? panel.seat : PlayerSeat.BottomPlayer;
    }

    /// <summary>The panel for a server seat, so other views can reuse the mapping.</summary>
    public SeatPanel PanelForSeat(int serverSeat)
    {
        var state = presenter != null ? presenter.State : null;
        return state != null ? PanelFor(state, serverSeat) : null;
    }

    private static string Ordinal(int place)
    {
        switch (place)
        {
            case 1: return "1st";
            case 2: return "2nd";
            case 3: return "3rd";
            default: return place + "th";
        }
    }
}
