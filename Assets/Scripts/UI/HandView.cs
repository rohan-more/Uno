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

    public void BuildHand(List<CardInstance> newHand)
    {
        hand.Clear();
        hand.AddRange(newHand);
        Rebuild();
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

public static class TestDeckBuilder
{
    public static List<CardInstance> BuildRandomHand(
        CardDatabase database,
        int count)
    {
        var result = new List<CardInstance>();

        for (int i = 0; i < count; i++)
        {
            var def = database.Cards[Random.Range(0, database.Cards.Count)];
            result.Add(new CardInstance(def.Id));
        }

        return result;
    }
}
