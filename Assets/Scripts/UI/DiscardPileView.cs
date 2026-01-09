using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class DiscardPileView : MonoBehaviour
{
    [SerializeField] private Image topCardImage;
    [SerializeField] private CanvasGroup canvasGroup;
    private CardInstance topCard;

    public void SetTopCard(CardInstance card, Sprite sprite)
    {
        topCard = card;
        topCardImage.sprite = sprite;
        topCardImage.enabled = true;
        canvasGroup.alpha = 1;
    }
}

