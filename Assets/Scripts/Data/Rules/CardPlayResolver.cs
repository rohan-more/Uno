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

        if (!rules.CanPlayCard(card, gameState, player, out _))
            return PlayResult.Invalid;

        player.Hand.Remove(card);
        gameState.DiscardPile.Add(card);

        var def = card.GetDefinition(database);

        switch (def.Type)
        {
            case CardType.Wild:
            case CardType.WildDrawFour:
                pendingWildCard = card;
                pendingWildPlayerIndex = playerIndex;
                return PlayResult.AwaitingWildColor;

            case CardType.DrawTwo:
                ResolveDraw2(playerIndex, def);
                return PlayResult.Played;

            default:
                ApplyStandardCard(def);
                return PlayResult.Played;
        }
    }
    
    private void ResolveDraw2(int playerIndex, CardDefinition def)
    {
        gameState.CurrentColor = def.Color;
        gameState.CurrentType = def.Type;
        gameState.CurrentNumber = -1;

        int targetIndex = GetNextPlayerIndex(playerIndex);

        var drawnCards = DrawCards(targetIndex, 2);

        actionBus.RaiseCardDraw(new CardDrawEvent
        {
            PlayerIndex = targetIndex,
            Cards = drawnCards
        });

        SkipNextPlayer();
    }

    private void SkipNextPlayer()
    {
       // currentPlayerIndex = GetNextPlayerIndex(currentPlayerIndex);
    }


    /// <summary>
    /// Called AFTER player selects color in popup
    /// </summary>
    public void ResolveWild(CardColor chosenColor)
    {
        if (pendingWildCard == null)
        {
            Debug.LogError("ResolveWild called with no pending wild card");
            return;
        }

        CardDefinition def = pendingWildCard.GetDefinition(database);

        gameState.CurrentColor = chosenColor;
        gameState.CurrentType = def.Type;
        gameState.CurrentNumber = -1;

        if (def.Type == CardType.WildDrawFour)
        {
            int targetIndex = GetNextPlayerIndex(pendingWildPlayerIndex);
            var drawnCards = DrawCards(targetIndex, 4);
            actionBus.RaiseCardDraw(new CardDrawEvent
            {
                PlayerIndex = targetIndex,
                Cards = drawnCards
            });
        }

        pendingWildCard = null;
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

        // NOTE:
        // Skip / Reverse / Draw2 go here later
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
    private void SkipPlayer()
    {
        // Skip handled by controller via turn increment
        // This is intentionally empty for now
    }

    private int GetNextPlayerIndex(int current)
    {
        return (current + 1) % players.Length;
    }
}

// -------- SUPPORT TYPES --------

public enum PlayResult
{
    Invalid,
    Played,
    AwaitingWildColor
}

