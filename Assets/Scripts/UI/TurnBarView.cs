using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>
/// The bar across the table that tells you what to do. One bar, three messages:
///   - "Your Turn": once your clock starts; it stays until you tap a card or
///     the deck, or the turn ends.
///   - "No card to play, Draw!": your turn and nothing in hand can be played;
///     it stays until you draw.
///   - "Not your turn": a quick flash when you tap a card or the deck out of turn.
///
/// It only reads the match state and listens to taps; MatchView still decides
/// what a tap does. Put it on the bar (with a CanvasGroup); it hides itself.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class TurnBarView : MonoBehaviour
{
    [SerializeField] private MatchPresenter presenter;
    [SerializeField] private PlayerActionBus actionBus;
    [SerializeField] private TMP_Text label;
    [Tooltip("What moves in and out. The bar itself if empty.")]
    [SerializeField] private RectTransform content;

    [Header("Messages")]
    [SerializeField] private string yourTurnText = "Your Turn";
    [SerializeField] private string mustDrawText = "No card to play, Draw!";
    [SerializeField] private string notYourTurnText = "Not your turn";

    private enum Entrance { SlideFromLeft, Grow }

    [Header("Motion")]
    [Tooltip("Slide in from off the left of the screen, or grow from a thin strip where it sits.")]
    [SerializeField] private Entrance entrance = Entrance.SlideFromLeft;
    [Tooltip("Slide: leave off the right (carrying on across) instead of back off the left.")]
    [SerializeField] private bool exitToRight;
    [SerializeField] private float inSeconds = 0.35f;
    [SerializeField] private float outSeconds = 0.3f;
    [Tooltip("Grow: the bar starts at this height, as a fraction of its own.")]
    [SerializeField] private float startHeightScale = 0.3f;

    [Header("Timing")]
    [Tooltip("\"Your Turn\" and \"Draw!\" wait this long before coming in, so the table settles first.")]
    [SerializeField] private float showDelaySeconds = 1f;
    [SerializeField] private float notYourTurnHoldSeconds = 0.5f;

    private enum Showing { Nothing, YourTurn, MustDraw, NotYourTurn }

    private CanvasGroup group;
    private Sequence playing;
    private Showing showing;
    private bool wasYourTurn;
    private Vector2 restPosition;

    private MatchState State => presenter.State;

    private void Awake()
    {
        group = GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false; // never catches a tap meant for the cards
        if (content == null)
            content = (RectTransform)transform;
        restPosition = content.anchoredPosition;
        HideNow();
    }

    private void OnEnable()
    {
        if (actionBus == null)
            return;
        actionBus.OnCardClicked += HandleCardClicked;
        actionBus.OnActionRequested += HandleActionRequested;
    }

    private void OnDisable()
    {
        if (actionBus != null)
        {
            actionBus.OnCardClicked -= HandleCardClicked;
            actionBus.OnActionRequested -= HandleActionRequested;
        }
        playing?.Kill();
    }

    private void Update()
    {
        if (presenter == null)
            return;

        bool yourTurn = YourClockIsRunning();
        bool mustDraw = yourTurn && MustDraw();

        if (yourTurn && !wasYourTurn)
        {
            // Your turn has just started: straight to "Draw!" if there's nothing to play
            if (mustDraw)
                ShowAndStay(Showing.MustDraw, mustDrawText);
            else
                ShowAndStay(Showing.YourTurn, yourTurnText);
        }
        else if (showing == Showing.MustDraw && !mustDraw)
        {
            Hide(); // they drew (or the turn ended)
        }
        else if (showing == Showing.YourTurn && !yourTurn)
        {
            Hide(); // the turn ended without a tap, e.g. it timed out
        }
        wasYourTurn = yourTurn;
    }

    // Your turn, and the server's pause after the last move is over
    private bool YourClockIsRunning() =>
        State.IsYourTurn && presenter.SecondsLeftInTurn <= presenter.TurnSeconds + 0.05f;

    // Nothing playable, nothing drawn yet, and nothing owed (owed cards are taken for you)
    private bool MustDraw() =>
        State.DrawnCardId < 0 && State.PendingDraw == 0 && presenter.PlayableCards().Count == 0;

    private void HandleCardClicked(CardClickedEvent evt) => OnTap();

    private void HandleActionRequested(PlayerActionRequest request)
    {
        if (request.ActionType == PlayerActionType.DrawCard)
            OnTap();
    }

    // "Your Turn" has done its job once you tap; "Draw!" waits for the draw itself
    private void OnTap()
    {
        if (showing == Showing.YourTurn)
            Hide();
        else
            FlashIfNotYourTurn();
    }

    private void FlashIfNotYourTurn()
    {
        if (presenter == null || !State.HasStarted || State.IsOver || State.IsYourTurn)
            return;
        ShowBriefly(Showing.NotYourTurn, notYourTurnText, notYourTurnHoldSeconds);
    }

    // ---- showing ----

    // A flash answers a tap, so it comes in at once
    private void ShowBriefly(Showing what, string text, float hold)
    {
        Show(what, text, 0f);
        playing.AppendInterval(hold);
        AppendOut(playing);
    }

    private void ShowAndStay(Showing what, string text) => Show(what, text, showDelaySeconds);

    private void Show(Showing what, string text, float delay)
    {
        playing?.Kill();
        showing = what; // set now, so a tap during the delay cancels it
        if (label != null)
            label.text = text;

        bool alreadyUp = group.alpha > 0.5f;
        playing = DOTween.Sequence();
        if (alreadyUp)
        {
            // Already on screen (one message replacing another): just settle in place
            playing.Append(content.DOAnchorPos(restPosition, inSeconds * 0.5f))
                .Join(content.DOScaleY(1f, inSeconds * 0.5f))
                .Join(group.DOFade(1f, inSeconds * 0.5f));
            return;
        }

        if (delay > 0f)
            playing.AppendInterval(delay);

        if (entrance == Entrance.SlideFromLeft)
        {
            playing.AppendCallback(() =>
            {
                content.localScale = Vector3.one;
                content.anchoredPosition = restPosition + Vector2.left * OffscreenDistance();
                group.alpha = 1f;
            });
            playing.Append(content.DOAnchorPos(restPosition, inSeconds).SetEase(Ease.OutCubic));
        }
        else
        {
            playing.AppendCallback(() =>
            {
                content.anchoredPosition = restPosition;
                content.localScale = new Vector3(1f, startHeightScale, 1f);
            });
            playing.Append(group.DOFade(1f, inSeconds * 0.6f))
                .Join(content.DOScaleY(1f, inSeconds).SetEase(Ease.OutBack));
        }
    }

    private void Hide()
    {
        playing?.Kill();
        showing = Showing.Nothing;
        if (group.alpha <= 0f)
            return; // still waiting out its delay: nothing on screen to take away
        playing = DOTween.Sequence();
        AppendOut(playing);
    }

    private void AppendOut(Sequence sequence)
    {
        if (entrance == Entrance.SlideFromLeft)
        {
            var away = (exitToRight ? Vector2.right : Vector2.left) * OffscreenDistance();
            sequence.Append(content.DOAnchorPos(restPosition + away, outSeconds).SetEase(Ease.InCubic));
        }
        else
        {
            sequence.Append(group.DOFade(0f, outSeconds));
        }
        sequence.OnComplete(HideNow);
    }

    // Far enough that the bar is wholly off screen: the canvas's width plus its own
    private float OffscreenDistance()
    {
        var canvas = GetComponentInParent<Canvas>();
        float canvasWidth = canvas != null ? ((RectTransform)canvas.rootCanvas.transform).rect.width : 2000f;
        return canvasWidth + content.rect.width;
    }

    private void HideNow()
    {
        showing = Showing.Nothing;
        group.alpha = 0f;
        content.anchoredPosition = restPosition;
        content.localScale = Vector3.one;
    }
}
