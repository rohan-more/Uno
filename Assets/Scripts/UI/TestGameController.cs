using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TestGameController : MonoBehaviour
{
    [SerializeField] private PlayerActionBus actionBus;
    [SerializeField] private HandView handView;
    [SerializeField] private HandView botHandView;
    [SerializeField] private DiscardPileView discardPileView;
    [SerializeField] private CardDatabase database;
    [SerializeField] private CardProxyView cardProxy;
    [SerializeField] private GameConfig gameConfig;
    
    private PlayerController[] players;
    private int currentPlayerIndex;
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
        // VALIDITY CHECK (authoritative)
        if (!rulesEngine.CanPlayCard(card.Instance, gameState, playerState, out _))
        {
            var def = card.Instance.GetDefinition(database);
            Debug.Log($"INVALID PLAY: {def.Color} {def.Type} {def.Number}");

            // Roll back preview
            Deselect();
            return;
        }
        
        if(currentPlayerIndex == 0)
        {
            cardProxy.Show(card.Sprite, PlayerSeat.BottomPlayer);
            cardProxy.MoveTo(() =>
            {
                CommitPlay(card);
                cardProxy.HideImmediate();
            });
        }
        else
        {
            cardProxy.Show(card.Sprite, PlayerSeat.TopPlayer);
            cardProxy.MoveTo(() =>
            {
                CommitPlay(card);
                cardProxy.HideImmediate();
            });
        }
    }
    
    private void CommitPlay(CardItem card)
    {
        var def = card.Instance.GetDefinition(database);

        // 1. Update game state
        gameState.DiscardPile.Add(card.Instance);
        gameState.CurrentColor = def.Color;
        gameState.CurrentType = def.Type;
        gameState.CurrentNumber = def.Number;

        players[currentPlayerIndex].State.Hand.Remove(card.Instance);
        // 2. Update views
        discardPileView.SetTopCard(card.Instance, def.FrontSprite);
        if (currentPlayerIndex == 0)
        {
            handView.RemoveCard(card.Instance);
        }
        else
        {
            botHandView.RemoveCard(card.Instance);
        }

        // 3. Cleanup
        selectedCard = null;
        if (currentPlayerIndex == 0)
        {
            handView.CheckValidCards(rulesEngine, gameState, players[0].State);
        }
        Debug.Log($"VALID PLAY: {def.Color} {def.Type} {def.Number}");
        EndTurn();
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