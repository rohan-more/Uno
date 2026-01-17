using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HumanDecisionMaker : IPlayerDecisionMaker
{
    private PlayerActionBus bus;
    private Action<PlayerActionRequest> pendingCallback;
    private int currentPlayerIndex;

    public HumanDecisionMaker(PlayerActionBus bus)
    {
        this.bus = bus;
        bus.OnCardClicked += OnCardClicked;
    }

    public void RequestAction(PlayerState player, GameState gameState, RulesEngine rules, CardDatabase database, Action<PlayerActionRequest> callback)
    {
        pendingCallback = callback;
        currentPlayerIndex = player.PlayerId;
    }

    private void OnCardClicked(CardClickedEvent evt)
    {
        if (pendingCallback == null)
            return;

        if (evt.PlayerIndex != currentPlayerIndex)
            return;

        pendingCallback.Invoke(new PlayerActionRequest
        {
            PlayerIndex = evt.PlayerIndex,
            ActionType = PlayerActionType.PlayCard,
            Card = evt.Card
        });

        pendingCallback = null;
    }
}

public class BotDecisionMaker : IPlayerDecisionMaker
{
    private  CardDatabase database;
    public void RequestAction(PlayerState player, GameState gameState, RulesEngine rules, CardDatabase database, Action<PlayerActionRequest> callback)
    {
        var playableCards = rules.GetPlayableCards(player, gameState);
        this.database = database;
        Debug.Log($"[BOT] Player {player.PlayerId} turn");
        Debug.Log($"[BOT] Hand size: {player.Hand.Count}");
        Debug.Log($"[BOT] Playable cards: {playableCards.Count}");

        if (playableCards.Count > 0)
        {
            var chosen = playableCards[0]; // dumb choice for now

            DebugChosenCard(chosen, gameState);

            callback(new PlayerActionRequest
            {
                PlayerIndex = player.PlayerId,
                ActionType = PlayerActionType.PlayCard,
                Card = chosen
            });
        }
        else
        {
            Debug.Log("[BOT] No valid card → Draw");

            callback(new PlayerActionRequest
            {
                PlayerIndex = player.PlayerId,
                ActionType = PlayerActionType.DrawCard
            });
        }
    }

    private void DebugChosenCard(CardInstance card, GameState gameState)
    {
        var def = card.GetDefinition(database); // see note below

        Debug.Log($"[BOT] Chose card: {def.Color} {def.Type} {def.Number} " + $"(Matches {gameState.CurrentColor} {gameState.CurrentType} {gameState.CurrentNumber})");
    }
}

public class PlayerController
{
    public PlayerState State;
    public IPlayerDecisionMaker DecisionMaker;
}


public interface IPlayerDecisionMaker
{
    void RequestAction(PlayerState player, GameState gameState, RulesEngine rules, CardDatabase database, System.Action<PlayerActionRequest> callback);
}
