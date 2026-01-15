using System.Collections.Generic;
using UnityEngine;


public class HandView : MonoBehaviour
{
    [SerializeField] private CardItem cardPrefab;
    [SerializeField] private CurvedHandLayout layout;
    [SerializeField] private CardDatabase database;
    [SerializeField] private PlayerActionBus actionBus;

    private readonly List<CardInstance> hand = new();
    private readonly List<CardItem> items = new();
    private List<RectTransform> cardTransforms = new();
    public CardItem GetCardItem(CardInstance instance)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Instance.Equals(instance))
                return items[i];
        }

        Debug.LogError($"CardItem not found for {instance.CardId}");
        return null;
    }
    public void BuildHand(List<CardInstance> newHand)
    {
        hand.Clear();
        hand.AddRange(newHand);
        Rebuild();
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
                Debug.Log($"[HAND] Card {instance.CardId} is VALID (rule: {matchedRule.name})");
            }
        }
        
        layout.Layout(cardTransforms);
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

            item.Bind(instance, def.FrontSprite, actionBus, playerIndex: 0);
            items.Add(item);
        }

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
