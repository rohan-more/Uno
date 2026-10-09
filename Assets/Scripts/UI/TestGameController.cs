using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

[Serializable]
public struct OpponentSeat
{
    public HandView hand;
    public PlayerSeat seat;
}

public class TestGameController : MonoBehaviour
{
    [SerializeField] private PlayerActionBus actionBus;
    [SerializeField] private HandView handView;
    [Tooltip("Bots in turn order, clockwise from you. One entry per bot.")]
    [SerializeField] private OpponentSeat[] opponents;
    [SerializeField] private DiscardPileView discardPileView;
    [SerializeField] private CardDatabase database;
    [SerializeField] private CardProxyView cardProxy;
    [SerializeField] private GameConfig gameConfig;
    [SerializeField] private CardColor ChosenCardColor;
    private TurnPhase turnPhase = TurnPhase.AwaitingAction;
    [Header("Testing")] [SerializeField] private bool useTestHand;
    [SerializeField] private int testHandSize = 7;
    [SerializeField] private List<string> testCardIds;
    private PlayerController[] players;
    private HandView[] handViews;   // indexed by player index
    private PlayerSeat[] seats;     // indexed by player index
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
        actionBus.OnCardDraw -= HandleCardsDrawn;
    }

    void Start()
    {
        InitializeSystems();
        SetupPlayers();
        InitializeDeckAndGameState();
        DealInitialHands();
        resolver = new CardPlayResolver(rulesEngine, gameState, players, database, deck, actionBus);
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
        foreach (var player in players)
            player.State.Hand = new List<CardInstance>();

        if (useTestHand)
        {
            GiveTestHand(players[0].State);
        }
        else
        {
            DealCards(players[0].State, testHandSize);
        }

        // Bots are always random for now
        for (int i = 1; i < players.Length; i++)
            DealCards(players[i].State, testHandSize);

        for (int i = 0; i < players.Length; i++)
            handViews[i].BuildHand(players[i].State.Hand);

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
        {
            if (deck.Count == 0)
            {
                Debug.LogError($"Deck ran out while dealing to player {player.PlayerId}: " +
                               $"{database.Cards.Count} cards in CardDatabase isn't enough for {players.Length} players x {count}");
                return;
            }

            player.Hand.Add(deck.Draw());
        }
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

    private void ChooseWildColor(CardColor color)
    {
        ChosenCardColor = color;
    }

    private void HandleAction(PlayerActionRequest request)
    {
        if (turnPhase != TurnPhase.AwaitingAction)
            return;

        switch (request.ActionType)
        {
            case PlayerActionType.PlayCard:
                TryPlayCard(request);
                break;

            case PlayerActionType.DrawCard:
                HandleDraw(request.PlayerIndex);
                break;
        }
    }

    private void HandleDraw(int playerIndex)
    {
        if (gameState.PendingDrawCount > 0)
        {
            var turn = resolver.ResolvePendingDraw(playerIndex);
            EndTurn(turn);
            return;
        }

        var drawn = resolver.DrawCards(playerIndex, 1);

        HandleCardsDrawn(new CardDrawEvent
        {
            PlayerIndex = playerIndex,
            Cards = drawn
        });

        // Bots don't play the drawn card yet, so they pass
        if (players[playerIndex].DecisionMaker is BotDecisionMaker)
            EndTurn(resolver.PassTurn(playerIndex));
    }

    private void HandleCardsDrawn(CardDrawEvent evt)
    {
        foreach (var card in evt.Cards)
            handViews[evt.PlayerIndex].AddCard(card);
    }

    private void StartTurn(int playerIndex)
    {
        currentPlayerIndex = playerIndex;
        var player = players[playerIndex].State;

        // Forced draw if stack exists and cannot respond
        if (gameState.PendingDrawCount > 0 &&
            !resolver.HasValidStackCard(player))
        {
            var turn = resolver.ResolvePendingDraw(playerIndex);
            EndTurn(turn);
            return;
        }

        Debug.Log($"TURN START: Player {playerIndex}");

        // Set correct phase
        if (players[playerIndex].DecisionMaker is HumanDecisionMaker)
        {
            turnPhase = TurnPhase.AwaitingAction;
        }
   
        players[playerIndex].DecisionMaker.RequestAction(player,
            gameState,
            rulesEngine,
            database,
            HandleAction);
    }

    private void SetupPlayers()
    {
        handViews = new HandView[opponents.Length + 1];
        seats = new PlayerSeat[opponents.Length + 1];

        handViews[0] = handView;
        seats[0] = PlayerSeat.BottomPlayer;
        for (int i = 0; i < opponents.Length; i++)
        {
            handViews[i + 1] = opponents[i].hand;
            seats[i + 1] = opponents[i].seat;
        }

        players = new PlayerController[handViews.Length];

        // Player 0 = YOU
        players[0] = new PlayerController
        {
            State = playerState, // your existing PlayerState
            DecisionMaker = new HumanDecisionMaker(actionBus)
        };

        // Everyone else = BOT (test stub)
        for (int i = 1; i < players.Length; i++)
        {
            players[i] = new PlayerController
            {
                State = new PlayerState { PlayerId = i },
                DecisionMaker = new BotDecisionMaker()
            };
        }
    }

    private void TryPlayCard(PlayerActionRequest request)
    {
        CardItem card = handViews[currentPlayerIndex].GetCardItem(request.Card);

        if (card == null)
        {
            return;
        }

        if (selectedCard == null)
        {
            SelectCard(card);
            ConfirmPlay(card);
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
        selectedCard = card;
        card.SetVisible(false);
    }

    private void ConfirmPlay(CardItem card)
    {
        var result = resolver.TryPlayCard(currentPlayerIndex, card.Instance);

        if (result.Type == PlayResultType.Invalid)
        {
            Deselect();
            return;
        }

        turnPhase = TurnPhase.Animating;
        PlayerSeat seat = seats[currentPlayerIndex];
        Debug.Log("Card Played: " + card.Instance.CardId);
        cardProxy.Show(card.Instance.GetDefinition(database).FrontSprite, seat);
        cardProxy.MoveTo(() =>
        {
            gameState.DiscardPile.Add(card.Instance);
            discardPileView.SetTopCard(card.Instance, card.Instance.GetDefinition(database).FrontSprite);

            RemoveFromHandView(card);

            if (result.Type == PlayResultType.AwaitingWildColor &&
                players[currentPlayerIndex].DecisionMaker is BotDecisionMaker)
            {
                EndTurn(resolver.ResolveWild(PickBotWildColor(players[currentPlayerIndex].State)));
            }
            else if (result.Type == PlayResultType.AwaitingWildColor)
            {
                PopupManager.Instance.Show(PopupType.ChooseColor, null, () =>
                {
                    var turn = resolver.ResolveWild(ChosenCardColor);
                    EndTurn(turn);
                });
            }
            else
            {
                EndTurn(result.Turn);
            }

            cardProxy.HideImmediate();
        });
    }


    private void RemoveFromHandView(CardItem card)
    {
        players[currentPlayerIndex].State.Hand.Remove(card.Instance);
        handViews[currentPlayerIndex].RemoveCard(card.Instance);
    }

    // Bot picks the color it holds most of
    private CardColor PickBotWildColor(PlayerState player)
    {
        var counts = new Dictionary<CardColor, int>();
        foreach (var instance in player.Hand)
        {
            var color = instance.GetDefinition(database).Color;
            if (color == CardColor.Wild) continue;
            counts[color] = counts.TryGetValue(color, out int n) ? n + 1 : 1;
        }

        var best = CardColor.Red;
        int bestCount = -1;
        foreach (var pair in counts)
        {
            if (pair.Value > bestCount)
            {
                best = pair.Key;
                bestCount = pair.Value;
            }
        }
        return best;
    }

    private void EndTurn(TurnAdvanceResult turn)
    {
        currentPlayerIndex = turn.NextPlayerIndex;

        if (players[currentPlayerIndex].DecisionMaker is BotDecisionMaker)
        {
            turnPhase = TurnPhase.BotThinking;
            StartCoroutine(SimulateBotTurn());
        }
        else
        {
            turnPhase = TurnPhase.AwaitingAction;
            StartTurn(currentPlayerIndex);
        }
    }


    private IEnumerator SimulateBotTurn()
    {
        Debug.Log("BOT THINKING...");
        yield return new WaitForSeconds(1f);
        turnPhase = TurnPhase.AwaitingAction;
        StartTurn(currentPlayerIndex);
        Debug.Log("BOT DONE");
    }

    private void Deselect()
    {
        if (selectedCard == null) return;

        selectedCard.SetVisible(true);
        selectedCard = null;
    }
}