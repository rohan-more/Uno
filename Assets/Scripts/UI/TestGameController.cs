using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public class TestGameController : MonoBehaviour
{
    [SerializeField] private PlayerActionBus actionBus;
    [SerializeField] private HandView handView;
    [SerializeField] private HandView botHandView;
    [SerializeField] private DiscardPileView discardPileView;
    [SerializeField] private CardDatabase database;
    [SerializeField] private CardProxyView cardProxy;
    [SerializeField] private GameConfig gameConfig;
    [SerializeField] private CardColor ChosenCardColor;
    
    [Header("Testing")]
    [SerializeField] private bool useTestHand;
    [SerializeField] private int testHandSize = 7;
    [SerializeField] private List<string> testCardIds;
    private PlayerController[] players;
    private DeckModel deck;
    private int currentPlayerIndex;
    private RulesEngine rulesEngine;
    private GameState gameState;
    private PlayerState playerState;
    private CardItem selectedCard;
    private CardPlayResolver resolver;

    private void OnEnable()
    {
        actionBus.OnActionRequested += HandleAction;
        actionBus.OnCardColor += ChooseWildColor;
        actionBus.OnCardDraw += HandleCardsDrawn;
    }

    private void OnDestroy()
    {
        actionBus.OnActionRequested -= HandleAction;
        actionBus.OnCardColor -= ChooseWildColor;
        actionBus.OnCardDraw  -= HandleCardsDrawn;
    }
    
    void Start()
    {
        InitializeSystems();
        SetupPlayers();
        InitializeDeckAndGameState();
        DealInitialHands();
        resolver = new CardPlayResolver(rulesEngine, gameState, players, database, deck,actionBus);
        StartFirstTurn();
    }
    
    
    private void InitializeDeckAndGameState()
    {
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

        int startIndex = UnityEngine.Random.Range(0, numberCards.Count);
        CardInstance startCard = numberCards[startIndex];
        numberCards.RemoveAt(startIndex);

        var remainingCards = new List<CardInstance>();
        remainingCards.AddRange(numberCards);
        remainingCards.AddRange(otherCards);

        deck = new DeckModel(remainingCards);
        deck.Shuffle(new System.Random());

        var startDef = startCard.GetDefinition(database);

        gameState.DiscardPile.Add(startCard);
        gameState.CurrentColor = startDef.Color;
        gameState.CurrentType = startDef.Type;
        gameState.CurrentNumber = startDef.Number;

        discardPileView.SetTopCard(startCard, startDef.FrontSprite);
    }
    
    private void DealInitialHands()
    {
        players[0].State.Hand = new List<CardInstance>();
        players[1].State.Hand = new List<CardInstance>();

        if (useTestHand)
        {
            GiveTestHand(players[0].State);
        }
        else
        {
            DealCards(players[0].State, testHandSize);
        }

        // Bot is always random for now
        DealCards(players[1].State, testHandSize);

        handView.BuildHand(players[0].State.Hand);
        botHandView.BuildHand(players[1].State.Hand);

        handView.CheckValidCards(rulesEngine, gameState, players[0].State);
    }
    
    private void GiveTestHand(PlayerState player)
    {
        player.Hand.Clear();

        // 1. Add forced test cards
        foreach (var id in testCardIds)
        {
            if (player.Hand.Count >= testHandSize)
                break;

            player.Hand.Add(new CardInstance(id));
            RemoveFromDeck(id);
        }

        // 2. Fill remaining slots randomly
        int remaining = testHandSize - player.Hand.Count;

        for (int i = 0; i < remaining; i++)
        {
            player.Hand.Add(deck.Draw());
        }

        // 3. Rebuild UI
        handView.BuildHand(player.Hand);
    }

    private void RemoveFromDeck(string cardId)
    {
        for (int i = 0; i < deck.Count; i++)
        {
            if (deck.Peek(i).CardId == cardId)
            {
                deck.Remove(cardId);
                return;
            }
        }

        Debug.LogWarning($"Test card {cardId} not found in deck");
    }
    
    private void DealCards(PlayerState player, int count)
    {
        for (int i = 0; i < count; i++)
            player.Hand.Add(deck.Draw());
    }
    
    private void StartFirstTurn()
    {
        StartTurn(0);
    }
    
    private void InitializeSystems()
    {
        database.Initialize();

        gameState = new GameState();
        playerState = new PlayerState { PlayerId = 0 };

        rulesEngine = new RulesEngine(gameConfig.rules, database);
    }
    
    /*void Start()
    {
        database.Initialize();
        
        gameState = new GameState();
        playerState = new PlayerState { PlayerId = 0 };
        rulesEngine = new RulesEngine(gameConfig.rules, database);

        SetupPlayers();
        StartTurn(0); 
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

        // 4. Deal remaining cards to player
        var playerHand = new List<CardInstance>();
        var botHand = new List<CardInstance>();
        
        int playerDeckMax = 7;
        while (deck.Count > 0 && playerHand.Count < playerDeckMax)
        {
            playerHand.Add(deck.Draw());
            botHand.Add(deck.Draw());
        }
        
        players[0].State.Hand = playerHand;
        players[1].State.Hand = botHand;
        
        // 5. HandView ONLY receives data
        handView.BuildHand(playerHand);
        botHandView.BuildHand(botHand);
        handView.CheckValidCards(rulesEngine, gameState, playerState);
        
        resolver = new CardPlayResolver(rulesEngine, gameState, players, database);
    }*/

    private void ChooseWildColor(CardColor color)
    {
        ChosenCardColor = color;
    }
    private void HandleAction(PlayerActionRequest request)
    {
        if (request.ActionType == PlayerActionType.PlayCard)
        {
            TryPlayCard(request);
        }
        else if (request.ActionType == PlayerActionType.DrawCard)
        {
            HandleDraw(request.PlayerIndex);
        }
        
    }
    
    private void HandleDraw(int playerIndex)
    {
        var drawn = resolver.DrawCards(playerIndex, 1);

        HandleCardsDrawn(new CardDrawEvent
        {
            PlayerIndex = playerIndex,
            Cards = drawn
        });
    }
    
    /*
    private void SkipNextPlayer()
    {
         currentPlayerIndex = GetNextPlayerIndex(currentPlayerIndex);
    }
    */

    
    private void HandleCardsDrawn(CardDrawEvent evt)
    {
        if (evt.PlayerIndex == 0)
        {
            foreach (var card in evt.Cards)
                handView.AddCard(card);
        }
        else
        {
            foreach (var card in evt.Cards)
                botHandView.AddCard(card);
        }
    }
    
    private void StartTurn(int playerIndex)
    {
        currentPlayerIndex = playerIndex;

        Debug.Log($"TURN START: Player {playerIndex}");

        players[playerIndex].DecisionMaker.RequestAction(players[playerIndex].State, gameState, rulesEngine, database, HandleAction);
    }
    
    private void StartBotTurn(int playerIndex)
    {
        currentPlayerIndex = playerIndex;

        Debug.Log($"TURN START: Player {playerIndex}");

        players[playerIndex].DecisionMaker.RequestAction(players[playerIndex].State, gameState, rulesEngine, database, HandleAction);
    }
    
    private void SetupPlayers()
    {
        players = new PlayerController[2];

        // Player 0 = YOU
        players[0] = new PlayerController
        {
            State = playerState, // your existing PlayerState
            DecisionMaker = new HumanDecisionMaker(actionBus)
        };

        // Player 1 = BOT (test stub)
        players[1] = new PlayerController
        {
            State = new PlayerState { PlayerId = 1 },
            DecisionMaker = new BotDecisionMaker()
        };
    }
    
    private void TryPlayCard(PlayerActionRequest request)
    {
        CardItem card = currentPlayerIndex == 0 ? handView.GetCardItem(request.Card) :  botHandView.GetCardItem(request.Card);
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
        card.SetVisible(false);
        if(currentPlayerIndex == 0)
        {
            cardProxy.Show(card.Sprite, PlayerSeat.BottomPlayer);
        }
        else
        {
            cardProxy.Show(card.Sprite, PlayerSeat.TopPlayer);
        }
    }
    
    private void ConfirmPlay(CardItem card)
    {
        var result = resolver.TryPlayCard(currentPlayerIndex, card.Instance);

        if (result == PlayResult.Invalid)
        {
            Deselect();
            return;
        }

        PlayerSeat seat = currentPlayerIndex == 0 ? PlayerSeat.BottomPlayer : PlayerSeat.TopPlayer;

        cardProxy.Show(card.Sprite, seat);
        cardProxy.MoveTo(() =>
        {
            discardPileView.SetTopCard(card.Instance, card.Instance.GetDefinition(database).FrontSprite);

            RemoveFromHandView(card);

            if (result == PlayResult.AwaitingWildColor)
            {
                //ShowWildColorPopup(); // UI only
                PopupManager.Instance.Show(PopupType.ChooseColor, null, () =>
                {
                    Debug.Log("Chosen Color: " + ChosenCardColor);
                    resolver.ResolveWild(ChosenCardColor);
                    EndTurn();
                });
            }
            else
            {
                EndTurn();
            }

            cardProxy.HideImmediate();
        });
    }
    
    private void RemoveFromHandView(CardItem card)
    {
        if (currentPlayerIndex == 0)
            handView.RemoveCard(card.Instance);
        else
            botHandView.RemoveCard(card.Instance);
    }
    
    private void EndTurn()
    {
        int nextPlayer = (currentPlayerIndex + 1) % players.Length;

        if (nextPlayer == 1)
        {
            // BOT TURN (TEST)
            StartCoroutine(SimulateBotTurn());
        }
        else
        {
            // BACK TO YOU
            StartTurn(0);
        }
    }
    
    private IEnumerator SimulateBotTurn()
    {
        Debug.Log("BOT THINKING...");
        yield return new WaitForSeconds(2f);
        StartBotTurn(1);
        // TEST behavior: do nothing / draw / auto-end
        Debug.Log("BOT DONE");

        //StartTurn(0);
    }
    
    private void Deselect()
    {
        if (selectedCard == null) return;

        selectedCard.SetVisible(true);
        selectedCard = null;
    }
    
}