using UnityEngine;
using System.Collections.Generic;
using UnityEngine.EventSystems;

public class CurvedHandLayout : MonoBehaviour, IDragHandler
{
    [Header("Curve Shape")]
    [SerializeField] private float radius = 700f;
    [SerializeField] private float maxVisibleAngle = 40f;
    [SerializeField] private float minSpacingAngle = 6f;

    [Header("Placement")]
    [SerializeField, Range(-1f, 1f)]
    private float verticalOffsetMultiplier = 0.35f;
    [SerializeField] private float horizontalOffset = 0f;

    [Header("Scrolling")]
    [SerializeField] private float scrollSensitivity = 0.05f;

    private float scrollAngleOffset = 0f;
    private int lastCardCount = -1;

    private List<RectTransform> currentCards;

    public void Layout(List<RectTransform> cards)
    {
        int count = cards.Count;
        if (count == 0) return;

        currentCards = cards;

        // Re-center when card count changes
        if (count != lastCardCount)
        {
            scrollAngleOffset = 0f;
            lastCardCount = count;
        }

        // Total angular span of the hand
        float totalAngle = (count - 1) * minSpacingAngle;

        // Visible window
        float visibleAngle = Mathf.Min(totalAngle, maxVisibleAngle);

        // Center the window on the hand midpoint
        float centerOffset = (totalAngle - visibleAngle) * 0.5f;

        float startAngle =
            -visibleAngle * 0.5f
            - centerOffset
            + scrollAngleOffset;

        Vector2 circleCenter = new Vector2(0f, -radius);

        float cardHeight = cards[0].rect.height;
        float verticalOffset = cardHeight * verticalOffsetMultiplier;

        for (int i = 0; i < count; i++)
        {
            float angle = startAngle + i * minSpacingAngle;
            float rad = angle * Mathf.Deg2Rad;

            float x = Mathf.Sin(rad) * radius;
            float y = Mathf.Cos(rad) * radius;

            Vector2 finalPos =
                circleCenter +
                new Vector2(x, y) +
                new Vector2(horizontalOffset, verticalOffset);

            RectTransform card = cards[i];
            card.anchoredPosition = finalPos;
            card.localRotation = Quaternion.Euler(0, 0, -angle);
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (currentCards == null || currentCards.Count <= 1)
            return;

        scrollAngleOffset += eventData.delta.x * scrollSensitivity;

        ClampScroll();
        Layout(currentCards);
    }

    private void ClampScroll()
    {
        int count = currentCards.Count;

        float totalAngle = (count - 1) * minSpacingAngle;
        float visibleAngle = Mathf.Min(totalAngle, maxVisibleAngle);

        float maxScroll = Mathf.Max(0f, totalAngle - visibleAngle);

        scrollAngleOffset = Mathf.Clamp(
            scrollAngleOffset,
            -maxScroll * 0.5f,
             maxScroll * 0.5f
        );
    }
}
