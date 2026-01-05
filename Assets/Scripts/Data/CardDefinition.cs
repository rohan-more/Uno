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
    public Sprite FrontSprite;
    public Sprite BackSprite;

    [Header("UI Metadata")]
    public string DisplayName;         // Optional: "Red 3"
    public Color AccentColor;          // For glow/highlight/shaders

    [Header("Rules Metadata")]
    public bool IsWild;
    public int DrawAmount;             // 0, 2, 4
}
