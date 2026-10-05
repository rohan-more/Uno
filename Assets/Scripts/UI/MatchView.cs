using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Draws a networked match and sends this player's moves. Everything it shows
/// comes from MatchPresenter.State, which the presenter updates before raising
/// each event, so every handler just redraws from State.
///
/// Every presenter event except the snapshot must end in exactly one
/// presenter.StepComplete(), or the next event waits for the step timeout.
/// </summary>
public class MatchView : MonoBehaviour
{
    [SerializeField] private MatchPresenter presenter;
    [SerializeField] private MatchSeatsView seatsView;
    [SerializeField] private CardDatabase database;
    [SerializeField] private PlayerActionBus actionBus;

    [Header("Table")]
    [Tooltip("Hands in turn order, like MatchSeatsView's panels: 0 is yours, 1 the player after you.")]
    [SerializeField] private HandView[] hands = new HandView[4];
    [SerializeField] private DiscardPileView discardPile;
    [SerializeField] private CardProxyView cardProxy;

    [Header("Optional")]
    [Tooltip("Shown after you draw a card you could play; ends your turn without playing it.")]
    [SerializeField] private Button passButton;

    [Header("Your turn (optional)")]
    [Tooltip("Shown only while it's your turn, e.g. a \"Your turn\" banner.")]
    [SerializeField] private GameObject yourTurnIndicator;
    [Tooltip("Shown on your turn when none of your cards can be played, e.g. a glow on the Draw button.")]
    [SerializeField] private GameObject drawHint;

    [Header("Removed (optional)")]
    [Tooltip("Shown when the server takes your seat, before going back home.")]
    [SerializeField] private GameObject removedPanel;
    [SerializeField] private TMP_Text removedText;
    [SerializeField] private float removedReturnDelay = 3f;

    // A move we sent that the server hasn't answered yet; blocks double sends
    private bool awaitingServer;

    // The server took our seat; ignore everything from here on
    private bool removed;

    // Wild waiting on the colour popup
    private CardInstance pendingWild;
    private CardColor? chosenColor;

    private MatchState State => presenter.State;

    private void OnEnable()
    {
        presenter.OnSnapshot += HandleSnapshot;
        presenter.OnCardPlayed += HandleCardPlayed;
        presenter.OnCardsDrawn += HandleCardsDrawn;
        presenter.OnTurnStarted += HandleTurnStarted;
        presenter.OnPlayerSkipped += CompleteStep;
        presenter.OnDirectionChanged += CompleteStep;
        presenter.OnTurnTimedOut += CompleteStep;
        presenter.OnSeatGivenToBot += CompleteStep;
        presenter.OnPlayerConnection += HandlePlayerConnection;
        presenter.OnPlayerFinished += HandlePlayerFinished;
        presenter.OnGameOver += HandleGameOver;
        presenter.OnError += HandleError;
        presenter.OnRemoved += HandleRemoved;

        actionBus.OnCardClicked += HandleCardClicked;
        actionBus.OnActionRequested += HandleActionRequested;
        actionBus.OnCardColor += HandleColorChosen;

        if (passButton != null)
            passButton.onClick.AddListener(Pass);
    }

    private void OnDisable()
    {
        presenter.OnSnapshot -= HandleSnapshot;
        presenter.OnCardPlayed -= HandleCardPlayed;
        presenter.OnCardsDrawn -= HandleCardsDrawn;
        presenter.OnTurnStarted -= HandleTurnStarted;
        presenter.OnPlayerSkipped -= CompleteStep;
        presenter.OnDirectionChanged -= CompleteStep;
        presenter.OnTurnTimedOut -= CompleteStep;
        presenter.OnSeatGivenToBot -= CompleteStep;
        presenter.OnPlayerConnection -= HandlePlayerConnection;
        presenter.OnPlayerFinished -= HandlePlayerFinished;
        presenter.OnGameOver -= HandleGameOver;
        presenter.OnError -= HandleError;
        presenter.OnRemoved -= HandleRemoved;

        actionBus.OnCardClicked -= HandleCardClicked;
        actionBus.OnActionRequested -= HandleActionRequested;
        actionBus.OnCardColor -= HandleColorChosen;

        if (passButton != null)
            passButton.onClick.RemoveListener(Pass);
    }

    // ---- presenter events ----

    private void HandleSnapshot(MatchState state)
    {
        awaitingServer = false;

        foreach (var seat in state.Seats)
            RefreshHand(seat.Index);

        if (state.TopCard != null)
            discardPile.SetTopCard(state.TopCard, FrontSprite(state.TopCard));

        RefreshControls();
    }

    private void HandleCardPlayed(MatchPresenter.CardPlayed played)
    {
        if (played.Seat == State.YourSeat)
            awaitingServer = false;

        // State already has the card out of the hand, so redraw first and fly the proxy from the seat
        RefreshHand(played.Seat);
        RefreshControls();

        var sprite = FrontSprite(played.Card);
        cardProxy.Show(sprite, seatsView.SeatPositionFor(played.Seat));
        cardProxy.MoveTo(() =>
        {
            discardPile.SetTopCard(played.Card, sprite);
            cardProxy.HideImmediate();
            presenter.StepComplete();
        });
    }

    private void HandleCardsDrawn(MatchPresenter.CardsDrawn drawn)
    {
        if (drawn.Seat == State.YourSeat)
            awaitingServer = false;

        RefreshHand(drawn.Seat);
        RefreshControls();
        presenter.StepComplete();
    }

    private void HandleTurnStarted(MatchPresenter.TurnStarted turn)
    {
        awaitingServer = false;

        // The turn moved on while we were picking a wild colour (timed out)
        if (turn.Seat != State.YourSeat && pendingWild != null)
        {
            pendingWild = null;
            PopupManager.Instance.CloseActive();
        }

        RefreshControls();
        presenter.StepComplete();
    }

    private void HandlePlayerConnection(int seat, bool connected) => presenter.StepComplete();

    private void HandlePlayerFinished(int seat, int place)
    {
        RefreshHand(seat);
        presenter.StepComplete();
    }

    private void HandleGameOver(int[] ranking)
    {
        RefreshControls();
        Debug.Log($"Game over: {string.Join(", ", ranking)}");
        presenter.StepComplete();
    }

    private void HandleError(MatchErrorMsg error)
    {
        // The presenter asks for a fresh snapshot; let the player try again meanwhile
        awaitingServer = false;
    }

    private void CompleteStep(int _) => presenter.StepComplete();

    private void HandleRemoved(string reason)
    {
        if (removed)
            return;
        removed = true;

        pendingWild = null;
        PopupManager.Instance?.CloseActive();
        RefreshControls();

        if (removedPanel != null)
            removedPanel.SetActive(true);
        if (removedText != null)
            removedText.text = reason == RemovedReasons.Disconnected
                ? "You were disconnected for too long. A bot took your seat."
                : "You missed too many turns. A bot took your seat.";

        StartCoroutine(ReturnHomeAfterDelay());
    }

    private IEnumerator ReturnHomeAfterDelay()
    {
        yield return new WaitForSeconds(removedReturnDelay);

        if (NakamaConnection.Instance != null)
            _ = NakamaConnection.Instance.LeaveMatchAsync();

        var loader = FindObjectOfType<MatchSceneLoader>();
        if (loader != null)
            loader.ReturnHome();
        else
            Debug.LogError("No MatchSceneLoader to go home with");
    }

    // ---- input ----

    private void HandleCardClicked(CardClickedEvent evt)
    {
        if (!CanAct() || evt.Card == null || !presenter.CanPlay(evt.Card))
            return;

        var def = database.GetById(evt.Card.CardId);
        bool isWild = def != null && (def.Type == CardType.Wild || def.Type == CardType.WildDrawFour);

        if (!isWild)
        {
            Send(presenter.PlayCardAsync(evt.Card));
            return;
        }

        // A play is one message, so the colour has to be picked first
        pendingWild = evt.Card;
        chosenColor = null;
        PopupManager.Instance.Show(PopupType.ChooseColor, null, OnColorPopupClosed);
    }

    private void HandleColorChosen(CardColor color) => chosenColor = color;

    private void OnColorPopupClosed()
    {
        var card = pendingWild;
        pendingWild = null;

        if (card != null && chosenColor.HasValue && CanAct())
            Send(presenter.PlayCardAsync(card, chosenColor.Value));
    }

    private void HandleActionRequested(PlayerActionRequest request)
    {
        if (request.ActionType != PlayerActionType.DrawCard || !CanAct())
            return;

        // You've already drawn this turn: the only options left are play it or pass
        if (presenter.MustPlayDrawnOrPass)
            return;

        Send(presenter.DrawAsync());
    }

    private void Pass()
    {
        if (CanAct() && presenter.MustPlayDrawnOrPass)
            Send(presenter.PassAsync());
    }

    private bool CanAct() => !removed && State.IsYourTurn && !awaitingServer && pendingWild == null;

    private async void Send(System.Threading.Tasks.Task send)
    {
        awaitingServer = true;
        RefreshControls();
        try
        {
            await send;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Couldn't send move: {e.Message}");
            awaitingServer = false;
            RefreshControls();
        }
    }

    // ---- drawing ----

    private void RefreshHand(int seat)
    {
        var hand = HandFor(seat);
        if (hand == null)
            return;

        if (seat == State.YourSeat)
            hand.BuildHand(State.Hand);
        else
            hand.SetHiddenCount(State.SeatAt(seat)?.CardCount ?? 0);
    }

    private void RefreshControls()
    {
        var yours = HandFor(State.YourSeat);
        if (yours != null)
        {
            bool canAct = CanAct();
            yours.HighlightPlayable(card => canAct && presenter.CanPlay(card));
        }

        bool yourTurn = !removed && State.IsYourTurn;

        if (passButton != null)
            passButton.gameObject.SetActive(CanAct() && presenter.MustPlayDrawnOrPass);

        if (yourTurnIndicator != null)
            yourTurnIndicator.SetActive(yourTurn);

        // Nothing to play and you haven't drawn yet: point at the Draw button
        if (drawHint != null)
            drawHint.SetActive(yourTurn && !presenter.MustPlayDrawnOrPass && presenter.PlayableCards().Count == 0);
    }

    private HandView HandFor(int seat)
    {
        int offset = State.OffsetFromYou(seat);
        return offset >= 0 && offset < hands.Length ? hands[offset] : null;
    }

    private Sprite FrontSprite(CardInstance card)
    {
        return card != null ? database.GetById(card.CardId)?.FrontSprite : null;
    }
}
