using UnityEngine;

public enum CardColor
{
    Red,
    Yellow,
    Green,
    Blue,
    Wild
}

public enum CardType
{
    Number,
    Skip,
    Reverse,
    DrawTwo,
    Wild,
    WildDrawFour
}

[System.Serializable]
public class CardDefinition
{
    [Header("Identity")]
    public string Id;                 // e.g. "RED_3", "WILD_DRAW4"
    public CardColor Color;
    public CardType Type;
    public int Number;                // Only valid for Number cards

    [Header("Visuals")]
    public Sprite FrontSprite; // the back is shared: CardDatabase.CardBack

    [Header("UI Metadata")]
    public string DisplayName;         // Optional: "Red 3"
    public Color AccentColor;          // For glow/highlight/shaders

    [Header("Rules Metadata")]
    public bool IsWild;
    public int DrawAmount;             // 0, 2, 4

    /// <summary>
    /// Points against you if this is still in your hand when the game ends:
    /// a number its face value, Skip / Reverse / Draw Two 20, wilds 50. The
    /// server decides the real ranking; this is for showing and checking it.
    /// </summary>
    public int Penalty => Type switch
    {
        CardType.Number => Number,
        CardType.Skip or CardType.Reverse or CardType.DrawTwo => 20,
        _ => 50,
    };
}
