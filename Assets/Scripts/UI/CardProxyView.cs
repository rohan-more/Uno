using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

[System.Serializable]
public struct SeatEntry
{
    public PlayerSeat seat;
    public RectTransform point;
}
public class CardProxyView : MonoBehaviour
{
    [Header("Refs")] 
    [SerializeField] private SeatEntry[] seatEntries;
    private Dictionary<PlayerSeat, RectTransform> seatLookup;
    [SerializeField] private Image cardImage;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private RectTransform centerTransform;
    [SerializeField] private Vector2  proxyIdlePosition;
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 1500f; // units per second

    private RectTransform rt;
    private Coroutine moveRoutine;
    private Tween flipTween;

    private void Awake()
    {
        rt = GetComponent<RectTransform>();
        proxyIdlePosition = rt.anchoredPosition;
        seatLookup = new Dictionary<PlayerSeat, RectTransform>(seatEntries.Length);
        foreach (var e in seatEntries)
        {
            seatLookup[e.seat] = e.point;
        }
        HideImmediate();
    }

    /// <summary>
    /// Initializes the proxy at a given anchored position.
    /// </summary>
    public void Show(Sprite sprite, PlayerSeat seat)
    {
        StopMove();

        if (!seatLookup.TryGetValue(seat, out RectTransform start))
        {
            Debug.LogError($"Missing SeatEntry for {seat}");
            return;
        }

        cardImage.sprite = sprite;
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;

        rt.anchoredPosition = start.anchoredPosition;
        canvasGroup.alpha = 1f;
    }

    /// <summary>
    /// Moves the proxy to the target RectTransform at constant speed.
    /// </summary>
    public void MoveTo(System.Action onComplete = null)
    {
        StopMove();
        moveRoutine = StartCoroutine(MoveAtConstantSpeed(centerTransform.anchoredPosition, 1f, onComplete));
    }

    /// <summary>
    /// Flies a card from one seat point to another, e.g. Deck to a player who
    /// drew. Completes straight away if either point is missing.
    /// </summary>
    public void Fly(Sprite sprite, PlayerSeat from, PlayerSeat to, float speedMultiplier, System.Action onComplete)
    {
        if (!seatLookup.TryGetValue(from, out var start))
        {
            Debug.LogError($"Missing SeatEntry for {from}");
            onComplete?.Invoke();
            return;
        }

        Fly(sprite, start.anchoredPosition, to, speedMultiplier, onComplete);
    }

    /// <summary>
    /// Flies a card from any object on screen, e.g. the draw pile, to a seat.
    /// The object can live anywhere in the hierarchy.
    /// </summary>
    public void Fly(Sprite sprite, RectTransform from, PlayerSeat to, float speedMultiplier, System.Action onComplete)
    {
        Fly(sprite, AnchoredPositionOf(from), to, speedMultiplier, onComplete);
    }

    private void Fly(Sprite sprite, Vector2 startPos, PlayerSeat to, float speedMultiplier, System.Action onComplete)
    {
        StopMove();

        if (!seatLookup.TryGetValue(to, out var end))
        {
            Debug.LogError($"Missing SeatEntry for {to}");
            onComplete?.Invoke();
            return;
        }

        cardImage.sprite = sprite;
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
        rt.anchoredPosition = startPos;
        canvasGroup.alpha = 1f;

        moveRoutine = StartCoroutine(MoveAtConstantSpeed(end.anchoredPosition, speedMultiplier, onComplete));
    }

    /// <summary>Flies a card from one object on screen to another, e.g. the deck to a hover point.</summary>
    public void Fly(Sprite sprite, RectTransform from, RectTransform to, float speedMultiplier, System.Action onComplete)
    {
        StopMove();

        cardImage.sprite = sprite;
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
        rt.anchoredPosition = AnchoredPositionOf(from);
        canvasGroup.alpha = 1f;

        moveRoutine = StartCoroutine(MoveAtConstantSpeed(AnchoredPositionOf(to), speedMultiplier, onComplete));
    }

    /// <summary>Flies the card already showing, from wherever it is now, to a seat.</summary>
    public void FlyFromHere(PlayerSeat to, float speedMultiplier, System.Action onComplete)
    {
        StopMove();

        if (!seatLookup.TryGetValue(to, out var end))
        {
            Debug.LogError($"Missing SeatEntry for {to}");
            onComplete?.Invoke();
            return;
        }

        moveRoutine = StartCoroutine(MoveAtConstantSpeed(end.anchoredPosition, speedMultiplier, onComplete));
    }

    /// <summary>Turns the card over in place: squeezes it flat, swaps the face, opens it again.</summary>
    public void Flip(Sprite newFace, float duration, System.Action onComplete)
    {
        KillFlip();
        flipTween = DOTween.Sequence()
            .Append(rt.DOScaleX(0f, duration * 0.5f).SetEase(Ease.InQuad))
            .AppendCallback(() => cardImage.sprite = newFace)
            .Append(rt.DOScaleX(1f, duration * 0.5f).SetEase(Ease.OutQuad))
            .OnComplete(() => onComplete?.Invoke());
    }

    // Where the proxy would sit, in its own anchored space, to cover the given object
    private Vector2 AnchoredPositionOf(RectTransform target)
    {
        var saved = rt.position;
        rt.position = target.TransformPoint(target.rect.center);
        var anchored = rt.anchoredPosition;
        rt.position = saved;
        return anchored;
    }

    private IEnumerator MoveAtConstantSpeed(Vector2 targetAnchoredPos, float speedMultiplier, System.Action onComplete)
    {
        while (Vector2.Distance(rt.anchoredPosition, targetAnchoredPos) > 0.5f)
        {
            rt.anchoredPosition = Vector2.MoveTowards(rt.anchoredPosition, targetAnchoredPos, moveSpeed * speedMultiplier * Time.deltaTime);

            yield return null;
        }

        rt.anchoredPosition = targetAnchoredPos;
        onComplete?.Invoke();
    }

    private void ResetProxy()
    {
        rt.anchoredPosition = proxyIdlePosition;
        canvasGroup.alpha = 0f;
    }

    public void HideImmediate()
    {
        cardImage.raycastTarget = false;
        StopMove();
        ResetProxy();
    }
    private void StopMove()
    {
        if (moveRoutine != null)
        {
            StopCoroutine(moveRoutine);
            moveRoutine = null;
        }
        KillFlip();
    }

    private void KillFlip()
    {
        if (flipTween != null && flipTween.IsActive())
            flipTween.Kill();
        flipTween = null;
        if (rt != null)
            rt.localScale = Vector3.one;
    }
}
