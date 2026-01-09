using System.Collections.Generic;
using UnityEngine;

public class TestGameController : MonoBehaviour
{
    [SerializeField] private PlayerActionBus actionBus;
    [SerializeField] private HandView handView;
    [SerializeField] private DiscardPileView discardPileView;
    [SerializeField] private CardDatabase database;
    [SerializeField] private CardProxyView cardProxy;
    [SerializeField] private RectTransform centerAnchor;
    [SerializeField] private RectTransform discardAnchor;
    [SerializeField] private GameConfig gameConfig;
    private RulesEngine rulesEngine;
    private GameState gameState;
    private PlayerState playerState;
    private CardItem selectedCard;
    private void Awake()
    {
        actionBus.OnActionRequested += HandleAction;
    }

    private void OnDestroy()
    {
        actionBus.OnActionRequested -= HandleAction;
    }
    
    void Start()
    {
        database.Initialize();
        
        gameState = new GameState();
        playerState = new PlayerState { PlayerId = 0 };
        rulesEngine = new RulesEngine(gameConfig.rules, database);
        
        var numberCards = new List<CardInstance>();
        var otherCards = new List<CardInstance>();

        foreach (var def in database.Cards)
        {
            var instance = new CardInstance(def.Id);

            if (def.Type == CardType.Number)
                numberCards.Add(instance);
            else
                otherCards.Add(instance);
        }
        
        int startIndex = Random.Range(0, numberCards.Count);
        CardInstance startCard = numberCards[startIndex];
        numberCards.RemoveAt(startIndex);
        
        var remainingCards = new List<CardInstance>();
        remainingCards.AddRange(numberCards);
        remainingCards.AddRange(otherCards);

        var deck = new DeckModel(remainingCards);
        deck.Shuffle(new System.Random());
        var startDef = startCard.GetDefinition(database);
        // 3. Initialize discard pile + game state
        gameState.DiscardPile.Add(startCard);
        gameState.CurrentColor = startDef.Color;
        gameState.CurrentType = startDef.Type;
        gameState.CurrentNumber = startDef.Number;
        discardPileView.SetTopCard(startCard, startDef.FrontSprite);
        Debug.Log("startCard: " + startCard.CardId);
        Debug.Log("First Card: " + gameState.CurrentNumber + " - " + gameState.CurrentColor);
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

    private void HandleAction(PlayerActionRequest request)
    {
        if (request.ActionType == PlayerActionType.PlayCard)
        {
            TryPlayCard(request);
        }
        else if (request.ActionType == PlayerActionType.DrawCard)
        {
            var random = database.Cards[
                Random.Range(0, database.Cards.Count)];

            handView.AddCard(new CardInstance(random.Id));
        }
    }
    
    private void TryPlayCard(PlayerActionRequest request)
    {
        CardItem card = handView.GetCardItem(request.Card);
        selectedCard = card;
        if (selectedCard == null)
        {
            SelectCard(card);
        }
        else if (selectedCard == card)
        {
            ConfirmPlay(card);
        }
        else
        {
            Deselect();
            SelectCard(card);
        }
    }
    
    private void SelectCard(CardItem card)
    {
        RectTransform proxyParent = cardProxy.transform.parent as RectTransform;
        selectedCard = card;
        Vector2 startPos = RectTransformUtil.WorldToAnchored(card.RectTransform, proxyParent);
        
        Vector2 centerPos = RectTransformUtil.WorldToAnchored(centerAnchor, proxyParent);

        card.SetVisible(false);

        cardProxy.Show(card.Sprite, startPos);
        
        cardProxy.AnimateToAndVanish(centerPos, onComplete: () => 
            {
         
            }
        );
    }
    
    private void ConfirmPlay(CardItem card)
    {
        // VALIDITY CHECK (authoritative)
        if (!rulesEngine.CanPlayCard(card.Instance, gameState, playerState, out _))
        {
            var def = card.Instance.GetDefinition(database);
            Debug.Log($"INVALID PLAY: {def.Color} {def.Type} {def.Number}");

            // Roll back preview
            Deselect();
            return;
        }

        // VALID → animate to discard
        cardProxy.AnimateToDiscard(discardAnchor.anchoredPosition, onComplete: () =>
        {
            CommitPlay(card);
        });
    }
    
    private void CommitPlay(CardItem card)
    {
        var def = card.Instance.GetDefinition(database);

        // 1. Update game state
        gameState.DiscardPile.Add(card.Instance);
        gameState.CurrentColor = def.Color;
        gameState.CurrentType = def.Type;
        gameState.CurrentNumber = def.Number;

        // 2. Update views
        discardPileView.SetTopCard(card.Instance, def.FrontSprite);
        handView.RemoveCard(card.Instance);

        // 3. Cleanup
        cardProxy.Hide();
        selectedCard = null;

        Debug.Log($"VALID PLAY: {def.Color} {def.Type} {def.Number}");
    }
    
    private void Deselect()
    {
        if (selectedCard == null) return;

        selectedCard.SetVisible(true);
        cardProxy.Hide();
        selectedCard = null;
    }
    
}