using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class RulesEngine
{
    private readonly List<RuleDefinitionSO> rules;
    private readonly CardDatabase cardDatabase;
 
    public RulesEngine(List<RuleDefinitionSO> rules, CardDatabase cardDatabase)
    {
        this.rules = rules.Where(r => r.enabled).ToList();
        this.cardDatabase = cardDatabase;
    }

    // ----- VALIDATION -----
    public bool CanPlayCard(CardInstance card, GameState gameState, PlayerState player, out string reason)
    {
        var def = card.GetDefinition(cardDatabase);

        // Wilds are always playable
        if (def.Type == CardType.Wild || def.Type == CardType.WildDrawFour)
        {
            reason = null;
            return true;
        }

        // Color match ALWAYS works
        if (def.Color == gameState.CurrentColor)
        {
            reason = null;
            return true;
        }

        // Number match (numbers only)
        if (def.Type == CardType.Number && gameState.CurrentType == CardType.Number && def.Number == gameState.CurrentNumber)
        {
            reason = null;
            return true;
        }

        // Symbol match (Reverse / Skip / Draw2)
        if (def.Type == gameState.CurrentType && def.Type != CardType.Number)
        {
            reason = null;
            return true;
        }

        reason = "No color, number, or symbol match";
        return false;
    }

    public List<CardInstance> GetPlayableCards(PlayerState player, GameState state)
    {
        var playable = new List<CardInstance>();

        foreach (var card in player.Hand)
        {
            if (CanPlayCard(card, state, player, out _))
                playable.Add(card);
        }

        return playable;
    }
}