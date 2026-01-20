using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DebugHandBootstrap : MonoBehaviour
{
    [SerializeField] private HandView handView;
    [SerializeField] private DiscardPileView discardPileView;
    [SerializeField] private CardDatabase database;
    [SerializeField] private GameConfig gameConfig;
    private RulesEngine rulesEngine;
    private GameState gameState;
    void Start()
    {
        database.Initialize();
        
        gameState = new GameState();

        rulesEngine = new RulesEngine(gameConfig.rules, database);
        
        // 1. Build deck from database
        var allCards = new List<CardInstance>();
        foreach (var def in database.Cards)
        {
            allCards.Add(new CardInstance(def.Id));
        }

        var deck = new DeckModel(allCards);
        deck.Shuffle(new System.Random());

        // 2. Draw starting card (must be Number)
        CardInstance startCard = deck.Draw();
        var startDef = startCard.GetDefinition(database);

        if (startDef.Type != CardType.Number)
        {
            Debug.LogError("Start card must be a Number card");
            return;
        }

        // 3. Initialize discard pile + game state
        gameState.DiscardPile.Add(startCard);
        gameState.CurrentColor = startDef.Color;
        gameState.CurrentType = startDef.Type;
        gameState.CurrentNumber = startDef.Number;
        discardPileView.SetTopCard(startCard, startDef.FrontSprite);
        // 4. Deal remaining cards to player
        var playerHand = new List<CardInstance>();
        int playerDeckMax = 5;
        while (deck.Count > 0 && playerHand.Count < playerDeckMax)
        {
            playerHand.Add(deck.Draw());
        }
        
        // 5. HandView ONLY receives data
        handView.BuildHand(playerHand);
    }
}

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
