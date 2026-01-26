using System.Collections.Generic;
using System.Linq;

public class DiscardPileModel
{
    private readonly Stack<CardInstance> pile = new();

    public CardInstance TopCard => pile.Count > 0 ? pile.Peek() : null;

    public void Add(CardInstance card)
    {
        pile.Push(card);
    }

    public List<CardInstance> TakeAllExceptTop()
    {
        var top = pile.Pop();
        var rest = pile.ToList();
        pile.Clear();
        pile.Push(top);
        return rest;
    }
}

public class GameState
{
    public CardColor CurrentColor;
    public int CurrentNumber;
    public CardType CurrentType;
    public TurnDirection Direction = TurnDirection.Clockwise;

    // Deferred effects
    public int SkipCount = 0;
    public int PendingDrawCount = 0;
    public CardType PendingDrawType;
    public bool RequiresColorChoice = false;

    public DiscardPileModel DiscardPile = new();
}

public class PlayerState
{
    public int PlayerId;
    public List<CardInstance> Hand = new();
    public PlayerSeat PlayerSeat;
}