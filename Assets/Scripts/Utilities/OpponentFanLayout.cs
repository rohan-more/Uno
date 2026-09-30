using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Small, tight fan for an opponent's face-down hand. Only the newest few cards
/// are shown (newest on top); a separate count label carries the real number.
/// Cards pivot around their bottom edge, like a hand being held.
/// </summary>
public class OpponentFanLayout : HandLayout
{
    [Header("Cards")]
    [Tooltip("How many card backs to draw, whatever the real count.")]
    [SerializeField, Min(1)] private int maxVisibleCards = 4;
    [SerializeField] private float cardScale = 0.35f;

    [Header("Fan")]
    [Tooltip("Degrees between neighbouring cards.")]
    [SerializeField] private float spreadAngle = 10f;
    [Tooltip("Sideways shift between neighbouring cards.")]
    [SerializeField] private float cardOffset = 12f;
    [Tooltip("Flip which way the fan opens, e.g. for seats on the other side of the table.")]
    [SerializeField] private bool mirror;

    public override void Layout(List<RectTransform> cards)
    {
        int firstVisible = Mathf.Max(0, cards.Count - maxVisibleCards);
        int visibleCount = cards.Count - firstVisible;
        float middle = (visibleCount - 1) * 0.5f;
        float side = mirror ? -1f : 1f;

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
            card.anchoredPosition = new Vector2(t * cardOffset * side, 0f);
            card.localRotation = Quaternion.Euler(0f, 0f, -t * spreadAngle * side);
        }
    }
}
