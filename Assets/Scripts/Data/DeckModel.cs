using System.Collections.Generic;

public class DeckModel
{
    private readonly List<CardInstance> cards = new();

    public DeckModel(IEnumerable<CardInstance> initialCards)
    {
        cards.AddRange(initialCards);
    }

    public void Shuffle(System.Random rng)
    {
        for (int i = cards.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (cards[i], cards[j]) = (cards[j], cards[i]);
        }
    }

    public CardInstance Draw()
    {
        var card = cards[^1];
        cards.RemoveAt(cards.Count - 1);
        return card;
    }

    public int Count => cards.Count;

    // -------------------------------
    // ADDITIONS (for testing & rules)
    // -------------------------------

    /// <summary>
    /// Peek at a card without removing it (0 = bottom, Count-1 = top)
    /// </summary>
    public CardInstance Peek(int index)
    {
        return cards[index];
    }

    /// <summary>
    /// Remove the first card matching the given id
    /// Returns true if removed
    /// </summary>
    public bool Remove(string cardId)
    {
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i].CardId == cardId)
            {
                cards.RemoveAt(i);
                return true;
            }
        }
        return false;
    }
}
