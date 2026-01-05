using UnityEngine;
using System.Collections.Generic;

public class CurvedHandLayout : MonoBehaviour
{
    public float radius = 900f;
    public float maxAngle = 30f;
    public float verticalOffset = -200f;
    public float horizontalOffset = -200f;
    public List<RectTransform> cards;

    private void Start()
    {
        Layout(cards);
    }
    
    
    public void Layout(List<RectTransform> cards)
    {
        int count = cards.Count;
        if (count == 0) return;

        float cardHeight = cards[0].rect.height;
        float verticalOffset = cardHeight * 0.1f;

        float angleStep = count > 1 ? maxAngle / (count - 1) : 0f;
        float startAngle = -maxAngle * 0.5f;

        Vector2 circleCenter = new Vector2(0f, -radius);

        for (int i = 0; i < count; i++)
        {
            float angle = startAngle + angleStep * i;
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

}