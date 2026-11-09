using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Small, tight fan for an opponent's face-down hand: one back per card, so it
/// grows and shrinks as they draw and play, closing up as it fills so a big
/// hand stays a tidy fan. Past Max Visible Cards only the newest are drawn
/// (newest on top); the count label carries the real number.
/// Cards pivot around their bottom edge, like a hand being held.
/// </summary>
public class OpponentFanLayout : HandLayout
{
    [Header("Cards")]
    [Tooltip("The most card backs drawn, however many they hold.")]
    [SerializeField, Min(1)] private int maxVisibleCards = 9;
    [SerializeField] private float cardScale = 0.35f;

    [Header("Fan")]
    [Tooltip("Degrees between neighbouring cards.")]
    [SerializeField] private float spreadAngle = 10f;
    [Tooltip("Sideways shift between neighbouring cards.")]
    [SerializeField] private float cardOffset = 12f;
    [Tooltip("The widest the fan may get, outermost card to outermost card; it closes up to fit. " +
             "0 = no limit.")]
    [SerializeField] private float maxFanWidth = 60f;
    [Tooltip("The widest the fan may open, first card to last, in degrees; it closes up to fit. " +
             "0 = no limit.")]
    [SerializeField] private float maxFanAngle = 60f;
    [Tooltip("Flip which way the fan opens, e.g. for seats on the other side of the table.")]
    [SerializeField] private bool mirror;

    public override void Layout(List<RectTransform> cards)
    {
        int firstVisible = Mathf.Max(0, cards.Count - maxVisibleCards);
        int visibleCount = cards.Count - firstVisible;
        float middle = (visibleCount - 1) * 0.5f;
        float side = mirror ? -1f : 1f;

        // A fuller hand closes up rather than spreading across the table
        float squeeze = 1f;
        if (visibleCount > 1)
        {
            if (maxFanWidth > 0f && cardOffset > 0f)
                squeeze = Mathf.Min(squeeze, maxFanWidth / ((visibleCount - 1) * cardOffset));
            if (maxFanAngle > 0f && spreadAngle > 0f)
                squeeze = Mathf.Min(squeeze, maxFanAngle / ((visibleCount - 1) * spreadAngle));
        }
        float offset = cardOffset * squeeze;
        float angle = spreadAngle * squeeze;

        for (int i = 0; i < cards.Count; i++)
        {
            RectTransform card = cards[i];
            bool visible = i >= firstVisible;
            card.gameObject.SetActive(visible);
            if (!visible)
                continue;

            // Later siblings draw on top, and HandView adds new cards last
            float t = (i - firstVisible) - middle;

            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0f);
            card.localScale = Vector3.one * cardScale;
            card.anchoredPosition = new Vector2(t * offset * side, 0f);
            card.localRotation = Quaternion.Euler(0f, 0f, -t * angle * side);
        }
    }
}
