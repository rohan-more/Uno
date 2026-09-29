using System.Collections.Generic;

/// <summary>
/// What this client believes the table looks like. Built from a GAME_STATE
/// snapshot and then kept up to date by the presenter as events arrive.
///
/// The server is the authority; this is only what we draw. If the two ever
/// disagree, ask for a fresh snapshot rather than trying to repair it.
/// </summary>
public class MatchState
{
    /// <summary>One seat as this client sees it. Only YourSeat has real cards.</summary>
    public class Seat
    {
        public int Index;
        public string Kind = SeatKinds.Human;
        public string Name = "";
        public int Avatar;
        public int CardCount;
        public int Place;        // 0 while playing, else finishing position
        public bool Connected = true;

        public bool IsBot => Kind == SeatKinds.Bot;
        public bool HasFinished => Place > 0;
    }

    public int YourSeat { get; private set; }
    public Seat[] Seats { get; private set; } = new Seat[0];

    /// <summary>Your own hand, in the order the server sent it.</summary>
    public List<CardInstance> Hand { get; } = new List<CardInstance>();

    public CardInstance TopCard { get; set; }
    public CardColor ActiveColor { get; set; }
    public int Direction { get; set; } = 1;
    public int CurrentSeat { get; set; }
    public int PendingDraw { get; set; }
    public int DeckCount { get; set; }
    public int[] Ranking { get; set; } = new int[0];

    /// <summary>The card you drew this turn and may still play, or -1.</summary>
    public int DrawnCardId { get; set; } = -1;

    /// <summary>Milliseconds left in the current turn when the last message arrived.</summary>
    public int TurnMsLeft { get; set; }

    /// <summary>Counts up with every EVENTS message; a gap means we missed something.</summary>
    public int Seq { get; set; } = -1;

    public bool IsYourTurn => CurrentSeat == YourSeat && !IsOver;
    public bool IsOver => Ranking != null && Seats != null && Ranking.Length == Seats.Length && Seats.Length > 0;

    /// <summary>Replaces everything with a fresh snapshot from the server.</summary>
    public void ApplySnapshot(GameStateMsg msg)
    {
        YourSeat = msg.you;
        CurrentSeat = msg.currentSeat;
        Direction = msg.direction == 0 ? 1 : msg.direction;
        PendingDraw = msg.pendingDraw;
        DeckCount = msg.deckCount;
        DrawnCardId = msg.drawnCardId;
        TurnMsLeft = msg.turnMsLeft;
        Ranking = msg.ranking ?? new int[0];
        Seq = msg.seq;
        ActiveColor = ParseColor(msg.activeColor);
        TopCard = ToCard(msg.topCard);

        Hand.Clear();
        if (msg.hand != null)
        {
            foreach (var card in msg.hand)
                Hand.Add(ToCard(card));
        }

        Seats = new Seat[msg.seats?.Length ?? 0];
        for (int i = 0; i < Seats.Length; i++)
        {
            var s = msg.seats[i];
            Seats[i] = new Seat
            {
                Index = s.seat,
                Kind = s.kind,
                Name = s.name,
                Avatar = s.avatar,
                CardCount = s.cardCount,
                Place = s.place,
                Connected = s.connected,
            };
        }
    }

    /// <summary>The seat this client is sitting in, or null before the first snapshot.</summary>
    public Seat You => SeatAt(YourSeat);

    public Seat SeatAt(int index)
    {
        if (Seats == null || index < 0 || index >= Seats.Length)
            return null;

        return Seats[index];
    }

    /// <summary>
    /// How far a seat is from yours in turn order: 0 is you, 1 the player after
    /// you, and so on. Screen positions are chosen from this, so every player
    /// sees themselves in the same place.
    /// </summary>
    public int OffsetFromYou(int seat)
    {
        if (Seats == null || Seats.Length == 0)
            return 0;

        var n = Seats.Length;
        return ((seat - YourSeat) % n + n) % n;
    }

    /// <summary>Finds one of your cards by the server's instance id.</summary>
    public CardInstance FindInHand(int instanceId)
    {
        foreach (var card in Hand)
        {
            if (card.InstanceId == instanceId)
                return card;
        }
        return null;
    }

    /// <summary>
    /// Whether one of your cards may be played right now. This mirrors the
    /// server's rules so the UI can highlight cards without asking; the server
    /// still decides, and a mismatch just means a rejected play.
    /// </summary>
    public bool CanPlay(CardInstance card, CardDatabase database)
    {
        if (card == null || database == null || TopCard == null)
            return false;

        var def = database.GetById(card.CardId);
        var top = database.GetById(TopCard.CardId);
        if (def == null || top == null)
            return false;

        // After drawing, only the drawn card may be played.
        if (DrawnCardId >= 0 && card.InstanceId != DrawnCardId)
            return false;

        // Cards are owed: only the same draw card answers them.
        if (PendingDraw > 0)
            return def.Type == top.Type;

        if (def.Type == CardType.Wild || def.Type == CardType.WildDrawFour)
            return true;

        if (def.Color == ActiveColor)
            return true;

        if (def.Type == CardType.Number)
            return top.Type == CardType.Number && def.Number == top.Number;

        return def.Type == top.Type; // symbol match: Skip on Skip, and so on
    }

    /// <summary>Your cards that may be played right now, for highlighting.</summary>
    public List<CardInstance> PlayableCards(CardDatabase database)
    {
        var playable = new List<CardInstance>();
        if (!IsYourTurn)
            return playable;

        foreach (var card in Hand)
        {
            if (CanPlay(card, database))
                playable.Add(card);
        }
        return playable;
    }

    public static CardInstance ToCard(CardMsg msg)
    {
        if (msg == null || msg.IsEmpty)
            return null;

        return new CardInstance(msg.id, msg.defId);
    }

    /// <summary>"BLUE" becomes CardColor.Blue. Unknown values fall back to Wild.</summary>
    public static CardColor ParseColor(string color)
    {
        switch (color)
        {
            case "RED": return CardColor.Red;
            case "YELLOW": return CardColor.Yellow;
            case "GREEN": return CardColor.Green;
            case "BLUE": return CardColor.Blue;
            default: return CardColor.Wild;
        }
    }

    /// <summary>CardColor.Blue becomes "BLUE", for sending back to the server.</summary>
    public static string ColorName(CardColor color)
    {
        return color.ToString().ToUpperInvariant();
    }
}
