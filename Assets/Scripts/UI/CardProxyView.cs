using System.Collections;
using System.Collections.Generic;
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
        moveRoutine = StartCoroutine(MoveAtConstantSpeed(centerTransform.anchoredPosition, onComplete));
    }

    private IEnumerator MoveAtConstantSpeed(Vector2 targetAnchoredPos, System.Action onComplete)
    {
        while (Vector2.Distance(rt.anchoredPosition, targetAnchoredPos) > 0.5f)
        {
            rt.anchoredPosition = Vector2.MoveTowards(rt.anchoredPosition, targetAnchoredPos, moveSpeed * Time.deltaTime);

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
    }
}
