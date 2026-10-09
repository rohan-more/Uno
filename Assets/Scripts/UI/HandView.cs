using System.Collections.Generic;
using TMPro;
using UnityEngine;

public enum HandViewMode
{
    Human,
    BotDebug,
    Opponent
}

public class HandView : MonoBehaviour
{
    [SerializeField] private HandViewMode mode;
    [SerializeField] private int ownerPlayerId;
    [SerializeField] private CardItem cardPrefab;
    [SerializeField] private HandLayout layout;
    [SerializeField] private CardDatabase database;
    [SerializeField] private PlayerActionBus actionBus;

    [Header("Opponent")]
    [SerializeField] private Sprite cardBackSprite;
    [SerializeField] private TMP_Text cardCountText;

    public int CardCount => hand.Count;

    private readonly List<CardInstance> hand = new();
    private readonly List<CardItem> items = new();
    private List<RectTransform> cardTransforms = new();
    public CardItem GetCardItem(CardInstance instance)
    {
        foreach (var t in items)
        {
            if (t.Instance.Equals(instance))
            {
                return t;
            }
        }

        Debug.LogError($"CardItem not found for {instance.CardId}");
        return null;
    }
    public void BuildHand(List<CardInstance> newHand)
    {
        hand.Clear();
        hand.AddRange(newHand);
        Rebuild();
        /*bool interactable = mode == HandViewMode.Human;

        foreach (var cardItem in items)
        {
            cardItem.SetInteractable(interactable);
        }*/
    }

    public void CheckValidCards(RulesEngine rulesEngine, GameState gameState, PlayerState playerState)
    {
        cardTransforms.Clear();
        for (int i = 0; i < hand.Count; i++)
        {
            var instance = hand[i];
            var item = items[i];
            cardTransforms.Add(item.RectTransform);
            bool canPlay = rulesEngine.CanPlayCard(instance, gameState, playerState, out var matchedRule);

            item.SetEligible(canPlay);

            if (canPlay)
            {
                //Debug.Log($"[HAND] Card {instance.CardId} is VALID (rule: {matchedRule.name})");
            }
        }
        
        layout.Layout(cardTransforms);
    }


    /// <summary>
    /// Opponent hands: we only know how many cards they hold, so show that many backs.
    /// </summary>
    public void SetHiddenCount(int count)
    {
        hand.Clear();
        for (int i = 0; i < count; i++)
            hand.Add(new CardInstance(string.Empty));
        Rebuild();
    }

    /// <summary>Outlines and lifts the cards the predicate allows, e.g. what the server would accept.</summary>
    public void HighlightPlayable(System.Predicate<CardInstance> canPlay)
    {
        foreach (var item in items)
            item.SetEligible(canPlay(item.Instance));

        Layout();
    }

    public void RemoveCard(CardInstance card)
    {
        hand.Remove(card);

        Rebuild();
    }

    public void AddCard(CardInstance card)
    {
        hand.Add(card);
        Rebuild();
    }

    private void Rebuild()
    {
        foreach (var item in items)
            Destroy(item.gameObject);

        items.Clear();

        foreach (var instance in hand)
        {
            var def = database.GetById(instance.CardId);
            var item = Instantiate(cardPrefab, transform);

            bool faceDown = mode == HandViewMode.Opponent;
            item.Bind(instance, faceDown ? cardBackSprite : def?.FrontSprite, actionBus, playerIndex: 0);
            item.SetClickable(mode == HandViewMode.Human);
            items.Add(item);
        }

        if (cardCountText != null)
            cardCountText.text = hand.Count.ToString();

        Layout();
    }

    private void Layout()
    {
        var rects = new List<RectTransform>();
        foreach (var item in items)
            rects.Add(item.GetComponent<RectTransform>());

        layout.Layout(rects);
    }
}
