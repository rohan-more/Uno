using UnityEngine;
using System.Collections.Generic;
using UnityEngine.EventSystems;

public class CurvedHandLayout : HandLayout, IDragHandler
{
    [Header("Curve Shape")]
    [SerializeField] private float radius = 700f;
    [SerializeField] private float maxVisibleAngle = 40f;
    [SerializeField] private float minSpacingAngle = 6f;

    [Header("Placement")]
    [SerializeField, Range(-1f, 1f)]
    private float verticalOffsetMultiplier = 0.35f;
    [SerializeField] private float horizontalOffset = 0f;
    [SerializeField] private float eligibleLift = 30f;

    [Header("Card Size")]
    [SerializeField] private float cardScale = 1f;

    [Header("Fitting")]
    [Tooltip("Squeeze the spacing so every card fits inside Max Visible Angle (for opponent hands that can't scroll).")]
    [SerializeField] private bool fitAllCards;

    [Header("Scrolling")]
    [SerializeField] private float scrollSensitivity = 0.05f;

    private float scrollAngleOffset = 0f;
    private int lastCardCount = -1;

    private List<RectTransform> currentCards;

    public override void Layout(List<RectTransform> cards)
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

        float spacing = GetSpacing(count);

        // Total angular span of the hand
        float totalAngle = (count - 1) * spacing;

        // Visible window
        float visibleAngle = Mathf.Min(totalAngle, maxVisibleAngle);

        // Center the window on the hand midpoint
        float centerOffset = (totalAngle - visibleAngle) * 0.5f;

        float startAngle =
            -visibleAngle * 0.5f
            - centerOffset
            + scrollAngleOffset;

        Vector2 circleCenter = new Vector2(0f, -radius);

        float cardHeight = cards[0].rect.height * cardScale;
        float verticalOffset = cardHeight * verticalOffsetMultiplier;

        for (int i = 0; i < count; i++)
        {
            RectTransform card = cards[i];
            var item = card.GetComponent<CardItem>();

            // Position relative to the hand's center, whatever the prefab's anchors are
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
            card.localScale = Vector3.one * cardScale;

            float angle = startAngle + i * spacing;
            float rad = angle * Mathf.Deg2Rad;

            float x = Mathf.Sin(rad) * radius;
            float y = Mathf.Cos(rad) * radius;

            Vector2 finalPos = circleCenter + new Vector2(x, y) + new Vector2(horizontalOffset, verticalOffset);
            if (item != null && item.IsEligible)
            {
                finalPos.y += eligibleLift;
            }
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

    private float GetSpacing(int count)
    {
        if (!fitAllCards || count <= 1)
            return minSpacingAngle;

        return Mathf.Min(minSpacingAngle, maxVisibleAngle / (count - 1));
    }

    private void ClampScroll()
    {
        int count = currentCards.Count;

        float totalAngle = (count - 1) * GetSpacing(count);
        float visibleAngle = Mathf.Min(totalAngle, maxVisibleAngle);

        float maxScroll = Mathf.Max(0f, totalAngle - visibleAngle);

        scrollAngleOffset = Mathf.Clamp(
            scrollAngleOffset,
            -maxScroll * 0.5f,
             maxScroll * 0.5f
        );
    }
}
