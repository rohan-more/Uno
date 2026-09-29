using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class CardInstance
{
    public string CardId;

    /// <summary>
    /// The server's id for this physical card (0-107, unique within a match).
    /// It is what PLAY_CARD sends, and it tells two RED_5s apart. -1 for cards
    /// that never came from the server.
    /// </summary>
    public int InstanceId = -1;

    public CardInstance(string cardId)
    {
        CardId = cardId;
    }

    public CardInstance(int instanceId, string cardId)
    {
        InstanceId = instanceId;
        CardId = cardId;
    }

    public CardDefinition GetDefinition(CardDatabase db)
    {
        return db.GetById(CardId);
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

    /// <summary>
    /// The definition for a card id, or null if this build doesn't know it.
    /// Returning null keeps one missing card from killing a whole match; callers
    /// show a blank card instead.
    /// </summary>
    public CardDefinition GetById(string id)
    {
        if (lookup == null)
            Initialize();

        return lookup.TryGetValue(id, out var definition) ? definition : null;
    }
}