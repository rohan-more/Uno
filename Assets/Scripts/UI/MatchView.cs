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

    [Header("Draw deck")]
    [Tooltip("Optional: the face-down deck. Draws fly from it (or from CardProxyView's Deck seat point if unset).")]
    [SerializeField] private DrawPileView drawPile;
    [Tooltip("Back shown on cards flying from the deck.")]
    [SerializeField] private Sprite cardBackSprite;
    [Tooltip("Roughly how long a whole draw takes to animate, however many cards.")]
    [SerializeField] private float drawDuration = 0.9f;

    [Header("Drawn card choice")]
    [Tooltip("Optional: where a card you drew and could play hovers, face up, while you choose " +
             "Play or Keep. Usually just above the discard pile. Needs Draw Pile. Unset: it goes " +
             "straight to your hand.")]
    [SerializeField] private RectTransform drawnCardHover;
    [SerializeField] private float flipDuration = 0.25f;

    [Header("Choice countdowns")]
    [Tooltip("Popups time out this much before the server's clock does, so our Keep or random colour " +
             "reaches it first.")]
    [SerializeField] private float popupSafetyMargin = 0.75f;

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

    [Header("Leaving (optional)")]
    [Tooltip("Quits the match after asking with the ConfirmAction popup. A bot takes your seat at once.")]
    [SerializeField] private Button leaveButton;

    [Header("Connection (optional)")]
    [Tooltip("Shown while the connection is lost and the client is reconnecting.")]
    [SerializeField] private GameObject reconnectingIndicator;

    // A move we sent that the server hasn't answered yet; blocks double sends
    private bool awaitingServer;

    // The server took our seat; ignore everything from here on
    private bool removed;

    // Wild waiting on the colour popup
    private CardInstance pendingWild;
    private CardColor? chosenColor;

    // While a draw is still flying in, so the play-or-keep choice waits for it
    private bool drawAnimating;

    // The play-or-keep popup is up
    private bool choiceOpen;

    // The drawn card hovering at drawnCardHover, not yet in the hand or on the pile
    private CardInstance hoveringCard;

    // Bumped by every snapshot, so an animation that started before it stops
    // instead of finishing a step the presenter has already dropped
    private int animationVersion;

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
        presenter.OnTurnExtended += HandleTurnExtended;

        actionBus.OnCardClicked += HandleCardClicked;
        actionBus.OnActionRequested += HandleActionRequested;
        actionBus.OnCardColor += HandleColorChosen;

        if (passButton != null)
            passButton.onClick.AddListener(Pass);

        if (leaveButton != null)
            leaveButton.onClick.AddListener(LeavePressed);

        if (NakamaConnection.Instance != null)
        {
            NakamaConnection.Instance.OnDisconnected += HandleConnectionLost;
            NakamaConnection.Instance.OnReconnected += HandleReconnected;
            NakamaConnection.Instance.OnMatchLost += HandleMatchLost;
        }
        ShowReconnecting(NakamaConnection.Instance != null && NakamaConnection.Instance.IsReconnecting);
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
        presenter.OnTurnExtended -= HandleTurnExtended;

        actionBus.OnCardClicked -= HandleCardClicked;
        actionBus.OnActionRequested -= HandleActionRequested;
        actionBus.OnCardColor -= HandleColorChosen;

        if (passButton != null)
            passButton.onClick.RemoveListener(Pass);

        if (leaveButton != null)
            leaveButton.onClick.RemoveListener(LeavePressed);

        if (NakamaConnection.Instance != null)
        {
            NakamaConnection.Instance.OnDisconnected -= HandleConnectionLost;
            NakamaConnection.Instance.OnReconnected -= HandleReconnected;
            NakamaConnection.Instance.OnMatchLost -= HandleMatchLost;
        }
    }

    // ---- presenter events ----

    private void HandleSnapshot(MatchState state)
    {
        awaitingServer = false;
        animationVersion++;
        drawAnimating = false;
        hoveringCard = null;
        StopAllCoroutines();
        cardProxy.HideImmediate();

        foreach (var seat in state.Seats)
            RefreshHand(seat.Index);

        if (state.TopCard != null)
            discardPile.SetTopCard(state.TopCard, FrontSprite(state.TopCard));

        if (drawPile != null)
            drawPile.SetCount(state.DeckCount);

        RefreshControls();
    }

    private void HandleCardPlayed(MatchPresenter.CardPlayed played)
    {
        if (played.Seat == State.YourSeat)
            awaitingServer = false;

        // You chose Play: the card drops from where it hovers, not from your hand
        if (hoveringCard != null && played.Seat == State.YourSeat && played.Card?.InstanceId == hoveringCard.InstanceId)
        {
            hoveringCard = null;
            RefreshControls();

            var face = FrontSprite(played.Card);
            int hoverVersion = animationVersion;
            cardProxy.MoveTo(() =>
            {
                if (hoverVersion != animationVersion)
                    return;

                discardPile.SetTopCard(played.Card, face);
                cardProxy.HideImmediate();
                presenter.StepComplete();
            });
            return;
        }

        // State already has the card out of the hand, so redraw first and fly the proxy from the seat
        RefreshHand(played.Seat);
        RefreshControls();

        var sprite = FrontSprite(played.Card);
        int version = animationVersion;
        cardProxy.Show(sprite, seatsView.SeatPositionFor(played.Seat));
        cardProxy.MoveTo(() =>
        {
            if (version != animationVersion)
                return;

            discardPile.SetTopCard(played.Card, sprite);
            cardProxy.HideImmediate();
            presenter.StepComplete();
        });
    }

    private void HandleCardsDrawn(MatchPresenter.CardsDrawn drawn)
    {
        if (drawn.Seat == State.YourSeat)
            awaitingServer = false;

        StartCoroutine(AnimateDraw(drawn));
    }

    /// <summary>
    /// State already holds the new cards, so the hand is first shown without
    /// them, then grows by one as each card lands from the deck.
    /// </summary>
    private IEnumerator AnimateDraw(MatchPresenter.CardsDrawn drawn)
    {
        // One card you could play: it waits face up in the middle for Play or Keep
        if (drawnCardHover != null && drawPile != null && drawn.Seat == State.YourSeat
            && drawn.Count == 1 && State.DrawnCardId >= 0)
        {
            yield return HoverDrawnCard();
            yield break;
        }

        int version = animationVersion;
        int n = Mathf.Max(0, drawn.Count);
        int finalCount = drawn.Seat == State.YourSeat
            ? State.Hand.Count
            : State.SeatAt(drawn.Seat)?.CardCount ?? 0;
        int startCount = Mathf.Max(0, finalCount - n);
        int deckBefore = State.DeckCount + n;

        drawAnimating = true;
        ShowHandCount(drawn.Seat, startCount);
        RefreshControls();

        // Split the time between the cards, so a +4 takes about as long as one card
        var to = seatsView.SeatPositionFor(drawn.Seat);
        float speed = Mathf.Max(1f, n * (0.6f / Mathf.Max(0.1f, drawDuration)));

        for (int i = 0; i < n; i++)
        {
            // Your own cards fly face up; everyone else's stay hidden
            var sprite = drawn.Seat == State.YourSeat && startCount + i < State.Hand.Count
                ? FrontSprite(State.Hand[startCount + i])
                : cardBackSprite;

            bool landed = false;
            if (drawPile != null)
                cardProxy.Fly(sprite, drawPile.Point, to, speed, () => landed = true);
            else
                cardProxy.Fly(sprite, PlayerSeat.Deck, to, speed, () => landed = true);
            if (drawPile != null)
                drawPile.SetCount(Mathf.Max(0, deckBefore - i - 1));

            while (!landed)
                yield return null;
            if (version != animationVersion)
                yield break;

            ShowHandCount(drawn.Seat, startCount + i + 1);
        }

        cardProxy.HideImmediate();
        if (drawPile != null)
            drawPile.SetCount(State.DeckCount);

        drawAnimating = false;
        RefreshControls();
        presenter.StepComplete();
    }

    /// <summary>Deck to the hover point face down, then turned face up.</summary>
    private IEnumerator HoverDrawnCard()
    {
        int version = animationVersion;
        var card = State.FindInHand(State.DrawnCardId);

        drawAnimating = true;
        ShowHandCount(State.YourSeat, State.Hand.Count - 1); // everything but the drawn card
        RefreshControls();

        bool done = false;
        cardProxy.Fly(cardBackSprite, drawPile.Point, drawnCardHover, 1f, () => done = true);
        drawPile.SetCount(State.DeckCount);
        while (!done)
            yield return null;
        if (version != animationVersion)
            yield break;

        done = false;
        cardProxy.Flip(FrontSprite(card), flipDuration, () => done = true);
        while (!done)
            yield return null;
        if (version != animationVersion)
            yield break;

        hoveringCard = card;
        drawAnimating = false;
        RefreshControls(); // opens Play / Keep
        presenter.StepComplete();
    }

    /// <summary>You kept the card (or ran out of time): it flies down into your hand.</summary>
    private IEnumerator ReturnHoveringCard(MatchPresenter.TurnStarted turn)
    {
        int version = animationVersion;
        hoveringCard = null;
        RefreshControls(); // no longer your turn: closes Play / Keep

        bool landed = false;
        cardProxy.FlyFromHere(seatsView.SeatPositionFor(State.YourSeat), 1f, () => landed = true);
        while (!landed)
            yield return null;
        if (version != animationVersion)
            yield break;

        cardProxy.HideImmediate();
        RefreshHand(State.YourSeat);
        FinishTurnStarted(turn);
    }

    /// <summary>Shows the first `count` cards of a hand, for growing it card by card.</summary>
    private void ShowHandCount(int seat, int count)
    {
        var hand = HandFor(seat);
        if (hand == null)
            return;

        if (seat == State.YourSeat)
            hand.BuildHand(State.Hand.GetRange(0, Mathf.Clamp(count, 0, State.Hand.Count)));
        else
            hand.SetHiddenCount(count);
    }

    private void HandleTurnStarted(MatchPresenter.TurnStarted turn)
    {
        // The turn moved on with the drawn card still hovering: it was kept
        if (hoveringCard != null)
        {
            StartCoroutine(ReturnHoveringCard(turn));
            return;
        }

        FinishTurnStarted(turn);
    }

    private void FinishTurnStarted(MatchPresenter.TurnStarted turn)
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
        hoveringCard = null;
        StopAllCoroutines();
        cardProxy.HideImmediate();
        PopupManager.Instance?.CloseActive();
        RefreshControls();

        if (removedPanel != null)
            removedPanel.SetActive(true);
        if (removedText != null)
            removedText.text = reason == RemovedReasons.Left
                ? "You left the match."
                : "You missed too many turns. A bot took your seat.";

        StartCoroutine(ReturnHomeAfterDelay());
    }

    private IEnumerator ReturnHomeAfterDelay()
    {
        yield return new WaitForSeconds(removedReturnDelay);
        ReturnHome();
    }

    private void ReturnHome()
    {
        if (NakamaConnection.Instance != null)
            _ = NakamaConnection.Instance.LeaveMatchAsync();

        var loader = FindObjectOfType<MatchSceneLoader>();
        if (loader != null)
            loader.ReturnHome();
        else
            Debug.LogError("No MatchSceneLoader to go home with");
    }

    // ---- leaving ----

    private void LeavePressed()
    {
        if (removed || PopupManager.Instance == null)
            return;

        // Replaces any choice popup that's up; the turn timer keeps running meanwhile
        PopupManager.Instance.Show(PopupType.ConfirmAction, new ConfirmChoice
        {
            Message = "You'll lose the coins you wagered on it.",
            ConfirmLabel = "Leave",
            OnConfirm = LeaveConfirmed,
            OnCancel = RefreshControls, // reopens Play / Keep if Leave replaced it
        });
    }

    private void LeaveConfirmed()
    {
        if (removed)
            return;

        _ = presenter.LeaveAsync();
        StartCoroutine(LeaveFallback());
    }

    // REMOVED (LEFT) normally comes straight back and sends us home. If it
    // doesn't, e.g. the connection is down, go home anyway; the seat then times
    // out to a bot like any abandoned seat.
    private IEnumerator LeaveFallback()
    {
        yield return new WaitForSeconds(3f);
        if (!removed)
            ReturnHome();
    }

    // ---- connection ----

    private void HandleConnectionLost(string reason)
    {
        ShowReconnecting(true);
        RefreshControls();
    }

    // The server sends a fresh GAME_STATE once we've rejoined, which redraws everything.
    private void HandleReconnected() => ShowReconnecting(false);

    // Back online but the match is gone (it closed while we were away, or the
    // server restarted): nothing more will arrive, so go home.
    private void HandleMatchLost()
    {
        if (removed)
            return;
        removed = true;
        Debug.LogWarning("The match ended while we were disconnected; going home");
        ReturnHome();
    }

    private void ShowReconnecting(bool show)
    {
        if (reconnectingIndicator != null)
            reconnectingIndicator.SetActive(show);
    }

    // ---- input ----

    private void HandleCardClicked(CardClickedEvent evt) => TryPlay(evt.Card);

    // Choosing the colour is a second popup in the same turn, so it gets a fresh clock
    private void PlayDrawnCard() => TryPlay(State.FindInHand(State.DrawnCardId), extendTurnForColor: true);

    private void TryPlay(CardInstance card) => TryPlay(card, extendTurnForColor: false);

    private void TryPlay(CardInstance card, bool extendTurnForColor)
    {
        if (!CanAct() || card == null || !presenter.CanPlay(card))
            return;

        var def = database.GetById(card.CardId);
        bool isWild = def != null && (def.Type == CardType.Wild || def.Type == CardType.WildDrawFour);

        if (!isWild)
        {
            Send(presenter.PlayCardAsync(card));
            return;
        }

        // A play is one message, so the colour has to be picked first
        pendingWild = card;
        chosenColor = null;

        float seconds = presenter.SecondsLeftInTurn;
        if (extendTurnForColor)
        {
            _ = presenter.ExtendTurnAsync();
            seconds = presenter.TurnSeconds;
        }

        PopupManager.Instance.Show(PopupType.ChooseColor, new PopupCountdown
        {
            TimeoutSeconds = PopupSeconds(seconds),
            OnTimeout = PickRandomColor,
        }, OnColorPopupClosed);
    }

    // Out of time on the colour: play the wild anyway, in a random colour
    private void PickRandomColor()
    {
        chosenColor = (CardColor)Random.Range((int)CardColor.Red, (int)CardColor.Blue + 1);
        PopupManager.Instance.CloseActive(); // sends the play, see OnColorPopupClosed
    }

    // Out of time on play-or-keep: keep it
    private void KeepOnTimeout()
    {
        PopupManager.Instance.CloseActive();
        Pass();
    }

    private float PopupSeconds(float serverSecondsLeft) => Mathf.Max(0.5f, serverSecondsLeft - popupSafetyMargin);

    private void HandleTurnExtended(int seat, int turnMs) => presenter.StepComplete();

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

        // Owing cards with nothing to stack: the server takes them for you
        if (OwesCardsWithNoAnswer())
            return;

        Send(presenter.DrawAsync());
    }

    private void Pass()
    {
        if (CanAct() && presenter.MustPlayDrawnOrPass)
            Send(presenter.PassAsync());
    }

    private bool OwesCardsWithNoAnswer() => State.PendingDraw > 0 && presenter.PlayableCards().Count == 0;

    // No moves while reconnecting: they'd be dropped, and the snapshot on rejoin redraws everything
    private bool CanAct() => !removed && (NakamaConnection.Instance == null || NakamaConnection.Instance.IsConnected)
                             && State.IsYourTurn && !awaitingServer && pendingWild == null;

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

        bool choosing = CanAct() && !drawAnimating && presenter.MustPlayDrawnOrPass;

        if (passButton != null)
            passButton.gameObject.SetActive(choosing);

        if (choosing && !choiceOpen)
            OpenDrawnChoice();
        else if (!choosing && choiceOpen)
            PopupManager.Instance?.CloseActive(); // the turn moved on without an answer

        if (yourTurnIndicator != null)
            yourTurnIndicator.SetActive(yourTurn);

        // Nothing to play and you haven't drawn yet: point at the Draw button
        if (drawHint != null)
            drawHint.SetActive(yourTurn && !presenter.MustPlayDrawnOrPass && presenter.PlayableCards().Count == 0
                               && !OwesCardsWithNoAnswer());
    }

    private void OpenDrawnChoice()
    {
        if (PopupManager.Instance == null)
            return;

        choiceOpen = true;
        PopupManager.Instance.Show(PopupType.DrawnCardChoice, new DrawnCardChoice
        {
            OnPlay = PlayDrawnCard,
            OnKeep = Pass,
            TimeoutSeconds = PopupSeconds(presenter.SecondsLeftInTurn),
            OnTimeout = KeepOnTimeout,
        }, () => choiceOpen = false);
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
