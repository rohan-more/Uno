using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// Authoritative card play & resolution engine.
/// Owns ALL card effects and mutations.
/// </summary>
public class CardPlayResolver
{
    private readonly RulesEngine rules;
    private readonly GameState gameState;
    private readonly PlayerController[] players;
    private readonly CardDatabase database;
    private readonly DeckModel deck;
    private readonly PlayerActionBus actionBus;
    private CardInstance pendingWildCard;
    private int pendingWildPlayerIndex;
   
    public CardPlayResolver(RulesEngine rules, GameState gameState, PlayerController[] players, CardDatabase database, DeckModel deckModel, PlayerActionBus actionBus)
    {
        this.rules = rules;
        this.gameState = gameState;
        this.players = players;
        this.database = database;
        this.deck = deckModel;
        this.actionBus = actionBus;
    }

    // -------- PUBLIC API --------

    public PlayResult TryPlayCard(int playerIndex, CardInstance card)
    {
        var player = players[playerIndex].State;
        var def = card.GetDefinition(database);

        // 1️⃣ STACKING GATE (FIRST)
        if (gameState.PendingDrawCount > 0)
        {
            bool canStack =
                (gameState.PendingDrawType == CardType.DrawTwo && def.Type == CardType.DrawTwo) ||
                (gameState.PendingDrawType == CardType.WildDrawFour && def.Type == CardType.WildDrawFour);

            if (!canStack)
            {
                return new PlayResult
                {
                    Type = PlayResultType.Invalid
                };
            }
        }

        switch (def.Type)
        {
            case CardType.Wild:
            case CardType.WildDrawFour:
                pendingWildCard = card;
                pendingWildPlayerIndex = playerIndex;
                return new PlayResult
                {
                    Type = PlayResultType.AwaitingWildColor
                };

            case CardType.DrawTwo:
                return new PlayResult
                {
                    Type = PlayResultType.Played,
                    Turn = ResolveDraw2(playerIndex, def)
                };

            case CardType.Skip:
                return new PlayResult
                {
                    Type = PlayResultType.Played,
                    Turn = ResolveSkip(playerIndex, def)
                };

            case CardType.Reverse:
                return new PlayResult
                {
                    Type = PlayResultType.Played,
                    Turn = ResolveReverse(playerIndex, def)
                };

            default:
                ApplyStandardCard(def);
                return new PlayResult
                {
                    Type = PlayResultType.Played,
                    Turn = new TurnAdvanceResult
                    {
                        NextPlayerIndex = AdvanceIndex(playerIndex)
                    }
                };
        }

    }
    
    private TurnAdvanceResult ResolveSkip(int playerIndex, CardDefinition def)
    {
        gameState.CurrentColor = def.Color;
        gameState.CurrentType = def.Type;
        gameState.CurrentNumber = -1;

        // Skip exactly one player
        return new TurnAdvanceResult
        {
            NextPlayerIndex = AdvanceIndex(playerIndex, 2)
        };
    }
    
    private TurnAdvanceResult ResolveReverse(int playerIndex, CardDefinition def)
    {
        gameState.CurrentColor = def.Color;
        gameState.CurrentType = def.Type;
        gameState.CurrentNumber = -1;

        // Flip direction
        gameState.Direction =
            gameState.Direction == TurnDirection.Clockwise
                ? TurnDirection.CounterClockwise
                : TurnDirection.Clockwise;

        // UNO rule: with 2 players, Reverse == Skip
        if (players.Length == 2)
        {
            return new TurnAdvanceResult
            {
                NextPlayerIndex = AdvanceIndex(playerIndex, 2)
            };
        }

        // Normal reverse: advance once in new direction
        return new TurnAdvanceResult
        {
            NextPlayerIndex = AdvanceIndex(playerIndex)
        };
    }
    
    private TurnAdvanceResult ResolveDraw2(int playerIndex, CardDefinition def)
    {
        gameState.CurrentColor = def.Color;
        gameState.CurrentType = def.Type;
        gameState.CurrentNumber = -1;

        gameState.PendingDrawCount += 2;
        gameState.PendingDrawType = CardType.DrawTwo;

        // Move to next player, they must stack or draw
        return new TurnAdvanceResult
        {
            NextPlayerIndex = AdvanceIndex(playerIndex)
        };
    }



    /// <summary>
    /// Called AFTER player selects color in popup
    /// </summary>
    public TurnAdvanceResult ResolveWild(CardColor chosenColor)
    {
        if (pendingWildCard == null)
        {
            Debug.LogError("ResolveWild called with no pending wild card");
            return new TurnAdvanceResult();
        }

        CardDefinition def = pendingWildCard.GetDefinition(database);

        gameState.CurrentColor = chosenColor;
        gameState.CurrentType = def.Type;
        gameState.CurrentNumber = -1;

        if (def.Type == CardType.WildDrawFour)
        {
            gameState.PendingDrawCount += 4;
            gameState.PendingDrawType = CardType.WildDrawFour;

            pendingWildCard = null;

            return new TurnAdvanceResult
            {
                NextPlayerIndex = AdvanceIndex(pendingWildPlayerIndex)
            };
        }

        pendingWildCard = null;

        return new TurnAdvanceResult
        {
            NextPlayerIndex = AdvanceIndex(pendingWildPlayerIndex)
        };
    }

    public TurnAdvanceResult ResolvePendingDraw(int playerIndex)
    {
        int count = gameState.PendingDrawCount;

        gameState.PendingDrawCount = 0;
        gameState.PendingDrawType = CardType.Number;

        var drawn = DrawCards(playerIndex, count);

        actionBus.RaiseCardDraw(new CardDrawEvent
        {
            PlayerIndex = playerIndex,
            Cards = drawn
        });

        // Player loses turn after drawing
        return new TurnAdvanceResult
        {
            NextPlayerIndex = AdvanceIndex(playerIndex)
        };
    }

    public void DrawCardForPlayer(int playerIndex)
    {
        var random = database.Cards[Random.Range(0, database.Cards.Count)];

        players[playerIndex].State.Hand.Add(new CardInstance(random.Id));
    }

    // -------- INTERNAL LOGIC --------

    private void ApplyStandardCard(CardDefinition def)
    {
        gameState.CurrentColor = def.Color;
        gameState.CurrentType = def.Type;
        gameState.CurrentNumber = def.Number;
    }

    public List<CardInstance> DrawCards(int playerIndex, int count)
    {
        var drawn = new List<CardInstance>();

        for (int i = 0; i < count; i++)
        {
            CardInstance card = deck.Draw(); // MUST come from deck
            players[playerIndex].State.Hand.Add(card);
            drawn.Add(card);
        }

        return drawn;
    }
    
    public bool HasValidStackCard(PlayerState player)
    {
        if (gameState.PendingDrawCount == 0)
            return false;

        foreach (var card in player.Hand)
        {
            var def = card.GetDefinition(database);

            if (gameState.PendingDrawType == CardType.DrawTwo &&
                def.Type == CardType.DrawTwo)
                return true;

            if (gameState.PendingDrawType == CardType.WildDrawFour &&
                def.Type == CardType.WildDrawFour)
                return true;
        }

        return false;
    }

    
    private int AdvanceIndex(int current, int step = 1)
    {
        int dir = (int)gameState.Direction; // +1 or -1
        int count = players.Length;

        int next = (current + dir * step) % count;
        if (next < 0)
            next += count;

        return next;
    }
}

// -------- SUPPORT TYPES --------

public struct PlayResult
{
    public PlayResultType Type;
    public TurnAdvanceResult Turn;
}

public enum PlayResultType
{
    Invalid,
    Played,
    AwaitingWildColor
}

public enum TurnPhase
{
    AwaitingAction,      // Player can click cards / draw
    AwaitingWildColor,   // Popup open
    ResolvingDraw,       // Cards being drawn / animated
    BotThinking,         // Bot delay
    Animating            // Card proxy moving
}

public struct TurnAdvanceResult
{
    public int NextPlayerIndex;
    public bool SkipOccurred;
    public bool DirectionChanged;
    public int CardsDrawn;
}

public enum TurnDirection
{
    Clockwise = 1,
    CounterClockwise = -1
}

