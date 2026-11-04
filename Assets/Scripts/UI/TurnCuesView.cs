using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The table's cues for whose turn it is and which way play goes:
///   - circling arrows behind the piles, turning in the direction of play and
///     flipping on a Reverse (their colour and opacity are the Image's own);
///   - the avatar of whoever is on turn growing a little (never yours: your
///     cards light up instead).
///
/// It only reads the presenter's state each frame. MatchView owns the match
/// events (and their StepComplete); this just decorates. Put it on any object
/// in MatchScene and fill in the parts you want; each is optional.
/// </summary>
public class TurnCuesView : MonoBehaviour
{
    [SerializeField] private MatchPresenter presenter;

    [Header("Direction arrows")]
    [Tooltip("The arrows Image. Its sprite should point clockwise; it's mirrored for anticlockwise. " +
             "On screen, play direction +1 is clockwise (you, top-left, top-right, right).")]
    [SerializeField] private Image arrows;
    [Tooltip("Degrees per second.")]
    [SerializeField] private float spinSpeed = 25f;
    [Tooltip("How long the flip on a Reverse takes.")]
    [SerializeField] private float flipSeconds = 0.35f;

    [Header("Avatar on turn")]
    [Tooltip("What grows on each seat, e.g. the avatar's parent, in table order like MatchSeatsView's " +
             "panels: 0 is you (never scaled), then the players after you.")]
    [SerializeField] private RectTransform[] seatScaleRoots = new RectTransform[4];
    [SerializeField] private float turnScale = 1.15f;
    [Tooltip("How quickly it grows and shrinks back.")]
    [SerializeField] private float turnScaleSpeed = 8f;

    private int shownDirection;
    private Tween flip;

    private void OnDisable() => flip?.Kill();

    private void Update()
    {
        var state = presenter != null ? presenter.State : null;
        if (state == null || state.Seats.Length == 0)
            return;

        UpdateArrows(state);
        UpdateSeatScale(state);
    }

    // ---- direction arrows ----

    private void UpdateArrows(MatchState state)
    {
        if (arrows == null)
            return;
        var rect = arrows.rectTransform;

        int direction = state.Direction >= 0 ? 1 : -1;
        if (direction != shownDirection)
            ShowDirection(rect, direction, animate: shownDirection != 0);

        // Clockwise on screen is a negative rotation in Unity
        if (!state.IsOver)
            rect.Rotate(0f, 0f, -direction * spinSpeed * Time.deltaTime);

    }

    private void ShowDirection(RectTransform rect, int direction, bool animate)
    {
        shownDirection = direction;
        flip?.Kill();

        // Mirroring the sprite reverses its arrows; scaling through 0 reads as a flip
        var scale = rect.localScale;
        float target = Mathf.Abs(scale.x) * direction;
        if (!animate || flipSeconds <= 0f)
        {
            rect.localScale = new Vector3(target, scale.y, scale.z);
            return;
        }
        flip = rect.DOScaleX(target, flipSeconds).SetEase(Ease.InOutBack);
    }

    // ---- avatar on turn ----

    // Eases toward the target each frame, so a turn passing on grows one avatar
    // as the last one shrinks. Nobody grows before the first turn or after the end.
    private void UpdateSeatScale(MatchState state)
    {
        int onTurn = state.HasStarted && !state.IsOver ? state.OffsetFromYou(state.CurrentSeat) : -1;
        float ease = 1f - Mathf.Exp(-turnScaleSpeed * Time.deltaTime);

        for (int i = 0; i < seatScaleRoots.Length; i++)
        {
            var root = seatScaleRoots[i];
            if (root == null)
                continue;
            float target = i != 0 && i == onTurn ? turnScale : 1f;
            float s = Mathf.Lerp(root.localScale.x, target, ease);
            root.localScale = new Vector3(s, s, 1f);
        }
    }
}
