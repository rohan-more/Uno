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
    public bool CanPlayCard(CardInstance card, GameState state, PlayerState player, out RuleDefinitionSO matchedRule)
    {
        foreach (var rule in rules)
        {
            if (rule.trigger != RuleTrigger.OnValidatePlay)
                continue;

            if (Matches(rule.match, card, state, player))
            {
                //Debug.Log($"VALID via rule: {rule.name}");
                matchedRule = rule;
                return true;
            }
        }

        matchedRule = null;
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

    // ----- EXECUTION -----
    public void ApplyCard(CardInstance card, GameState state, PlayerState player)
    {
        foreach (var rule in rules)
        {
            if (rule.trigger != RuleTrigger.OnCardPlayed)
                continue;

            if (Matches(rule.match, card, state, player))
            {
                ApplyEffects(rule.effects, state, player);
            }
        }
    }
    
    
    private bool Matches(CardMatchCriteria match, CardInstance card, GameState state, PlayerState player)
    {
        var def = card.GetDefinition(cardDatabase);

        if (match.restrictToType && def.Type != match.appliesToType)
            return false;
        
        if (match.isWild && !def.IsWild)
            return false;

        if (match.matchColor && def.Color != state.CurrentColor)
            return false;

        if (match.matchNumber && def.Number != state.CurrentNumber)
            return false;

        if (match.matchType && def.Type != state.CurrentType)
            return false;

        return true;
    }
    
    private void ApplyEffects(List<RuleEffect> effects, GameState state, PlayerState currentPlayer)
    {
        foreach (var effect in effects)
        {
            switch (effect.type)
            {
                case RuleEffectType.SkipNextPlayer:
                    state.SkipCount += effect.amount;
                    break;

                case RuleEffectType.ReverseTurnOrder:
                    state.IsClockwise = !state.IsClockwise;
                    break;

                case RuleEffectType.DrawCards:
                    state.PendingDrawCount += effect.amount;
                    break;

                case RuleEffectType.ChooseColor:
                    state.RequiresColorChoice = true;
                    break;

                case RuleEffectType.SetCurrentColor:
                    // color is set later by player choice (wild)
                    break;
            }
        }
    }
}