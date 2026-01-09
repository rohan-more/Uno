using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "UNO/Rules/Rule")]
public class RuleDefinitionSO : ScriptableObject
{
    public RuleTrigger trigger;
    public CardMatchCriteria match;
    public List<RuleEffect> effects;

    public bool enabled = true;
}

public enum RuleTrigger
{
    OnValidatePlay,
    OnCardPlayed
}

[System.Serializable]
public class CardMatchCriteria
{
    public bool matchColor;
    public bool matchNumber;
    public bool matchType;
    public bool isWild;
    public bool restrictToType;
    public CardType appliesToType;
}

public enum RuleEffectType
{
    SetCurrentColor,
    SkipNextPlayer,
    ReverseTurnOrder,
    DrawCards,
    ChooseColor
}

[System.Serializable]
public class RuleEffect
{
    public RuleEffectType type;
    public int amount; // draw count, skip count, etc.
}