using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The face-down draw deck: a small stack of card backs that thins out as the
/// deck runs low. Clicking it draws, the same as the Draw button. Drawn cards
/// fly from here.
///
/// Put it on a RectTransform sized like one card; the layers are built as
/// children at runtime.
/// </summary>
public class DrawPileView : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private Sprite cardBackSprite;
    [SerializeField] private PlayerActionBus actionBus;

    [Header("Stack")]
    [Tooltip("Most card backs drawn on top of each other.")]
    [SerializeField, Min(1)] private int maxLayers = 5;
    [Tooltip("One layer is drawn for every this many cards in the deck.")]
    [SerializeField, Min(1)] private int cardsPerLayer = 10;
    [Tooltip("Shift of each layer from the one below it.")]
    [SerializeField] private Vector2 layerOffset = new Vector2(2f, 3f);

    [Header("Optional")]
    [SerializeField] private TMP_Text countText;

    private readonly List<Image> layers = new();

    /// <summary>Where drawn cards fly from.</summary>
    public RectTransform Point => (RectTransform)transform;

    private void Awake()
    {
        for (int i = 0; i < maxLayers; i++)
        {
            var go = new GameObject($"Layer{i}", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(transform, false);
            rect.SetSiblingIndex(i); // under anything placed on the deck, like the count
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            rect.anchoredPosition = layerOffset * i;

            var image = go.GetComponent<Image>();
            image.sprite = cardBackSprite;
            image.preserveAspect = true;
            layers.Add(image);
        }
    }

    /// <summary>Shows how many cards are left to draw.</summary>
    public void SetCount(int count)
    {
        int visible = count <= 0 ? 0 : Mathf.Clamp(Mathf.CeilToInt(count / (float)cardsPerLayer), 1, maxLayers);
        for (int i = 0; i < layers.Count; i++)
            layers[i].enabled = i < visible;

        if (countText != null)
            countText.text = count.ToString();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // MatchView decides whether a draw is allowed right now
        actionBus.RaiseAction(new PlayerActionRequest
        {
            ActionType = PlayerActionType.DrawCard,
        });
    }
}
