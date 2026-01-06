using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public struct CardInstance
{
    public string CardId;

    public CardInstance(string cardId)
    {
        CardId = cardId;
    }
}


[CreateAssetMenu(fileName = "CardDatabase", menuName = "UNO/Card Database")]
public class CardDatabase : ScriptableObject
{
    public List<CardDefinition> Cards;

    private Dictionary<string, CardDefinition> lookup;

    public void Initialize()
    {
        lookup = new Dictionary<string, CardDefinition>();
        foreach (var card in Cards)
        {
            lookup[card.Id] = card;
        }
    }

    public CardDefinition GetById(string id)
    {
        return lookup[id];
    }
}