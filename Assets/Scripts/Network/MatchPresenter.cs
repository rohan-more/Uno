using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Drives a networked match: keeps MatchState up to date, plays server events
/// out one at a time, and sends this player's actions.
///
/// It draws nothing. A view subscribes to the events below, animates each one,
/// and calls StepComplete() when its animation is done; the next event waits
/// until then, so a fast run of bot turns can't overlap on screen.
/// </summary>
public class MatchPresenter : MonoBehaviour
{
    // ---- event payloads ----

    public struct CardPlayed
    {
        public int Seat;
        public CardInstance Card;
        public CardColor Color; // the colour to match after this play
    }

    public struct CardsDrawn
    {
        public int Seat;
        public int Count;
        public List<CardInstance> Cards; // only for your own draws; null otherwise
    }

    public struct TurnStarted
    {
        public int Seat;
        public int TurnMs;      // 0 for a bot seat: no countdown to show
        public int PendingDraw; // cards owed by this player
    }

    // ---- events, all raised on the main thread ----

    /// <summary>A full snapshot: redraw everything from State.</summary>
    public event Action<MatchState> OnSnapshot;

    public event Action<CardPlayed> OnCardPlayed;
    public event Action<CardsDrawn> OnCardsDrawn;
    public event Action<int> OnPlayerSkipped;
    public event Action<int> OnDirectionChanged;
    public event Action<TurnStarted> OnTurnStarted;
    public event Action<int> OnTurnTimedOut;
    public event Action<int> OnSeatGivenToBot;
    public event Action<int, bool> OnPlayerConnection;
    public event Action<int, int> OnPlayerFinished; // seat, place
    public event Action<int[]> OnGameOver;          // seats in finishing order
    public event Action<int, int> OnTurnExtended;   // seat, turnMs: their clock restarted

    /// <summary>The server rejected something we sent. Treat as a client bug.</summary>
    public event Action<MatchErrorMsg> OnError;

    /// <summary>You lost your seat (see RemovedReasons). Nothing more will arrive.</summary>
    public event Action<string> OnRemoved;

    [Header("Playback")]
    [Tooltip("Wait for the view to call StepComplete() before playing the next " +
             "event. Turn off to fire events back to back while building UI.")]
    [SerializeField] private bool waitForView = true;

    [Tooltip("Give up waiting for StepComplete() after this long, so a missing " +
             "call can't freeze the match.")]
    [SerializeField] private float stepTimeout = 3f;

    [SerializeField] private CardDatabase database;

    [Tooltip("Log every server event as it plays, to see where a match stalls.")]
    [SerializeField] private bool logEvents = true;

    /// <summary>The table as this client understands it.</summary>
    public MatchState State { get; } = new MatchState();

    private readonly Queue<MatchEventMsg> _pending = new Queue<MatchEventMsg>();
    private readonly Queue<float> _arrivals = new Queue<float>(); // when each pending event arrived

    // When the server's clock for the current turn runs out, by our clock. Timed
    // from when messages arrived, not when they played, so queued animations
    // can't make us think there's more time than there is.
    private float _turnEndsAt;
    private int _turnMs = 8000;
    private bool _busy;
    private float _stepStarted;

    private NakamaConnection Connection => NakamaConnection.Instance;

    private void OnEnable()
    {
        if (Connection != null)
            Connection.OnMatchState += HandleMatchState;
    }

    private void OnDisable()
    {
        if (Connection != null)
            Connection.OnMatchState -= HandleMatchState;
    }

    private async void Start()
    {
        // The snapshot that started this match was probably sent while the home
        // screen was still loaded, so ask for a fresh one now.
        if (Connection != null && !string.IsNullOrEmpty(Connection.CurrentMatchId))
            await RequestStateAsync();
    }

    private void Update()
    {
        if (_busy && waitForView && Time.time - _stepStarted > stepTimeout)
        {
            Debug.LogWarning($"Match view never called StepComplete() within {stepTimeout}s; continuing");
            _busy = false;
        }

        PlayNext();
    }

    /// <summary>Called by the view when it has finished animating an event.</summary>
    public void StepComplete()
    {
        _busy = false;
    }

    // ---- receiving ----

    private void HandleMatchState(long opCode, string json)
    {
        switch (opCode)
        {
            case UnoOpCodes.GameState:
                ApplySnapshot(JsonUtility.FromJson<GameStateMsg>(json));
                break;

            case UnoOpCodes.Events:
                Enqueue(JsonUtility.FromJson<MatchEventsMsg>(json));
                break;

            case UnoOpCodes.Error:
                var error = JsonUtility.FromJson<MatchErrorMsg>(json);
                Debug.LogWarning($"Server rejected an action: {error.code} - {error.message}");
                OnError?.Invoke(error);
                _ = RequestStateAsync(); // whatever we think is wrong, start again
                break;

            case UnoOpCodes.Removed:
                var removed = JsonUtility.FromJson<MatchRemovedMsg>(json);
                Debug.LogWarning($"Removed from the match: {removed?.reason}");
                ClearPending(); // nothing queued matters any more
                _busy = false;
                OnRemoved?.Invoke(removed?.reason);
                break;
        }
    }

    private void ApplySnapshot(GameStateMsg msg)
    {
        if (msg == null)
            return;

        ClearPending(); // anything queued is older than this snapshot
        _busy = false;

        State.ApplySnapshot(msg);
        _turnEndsAt = Time.time + msg.turnMsLeft / 1000f;
        OnSnapshot?.Invoke(State);
    }

    private void Enqueue(MatchEventsMsg batch)
    {
        if (batch?.events == null)
            return;

        // Every batch is one higher than the last. A gap means we missed
        // something, and replaying from here would drift, so ask for the truth.
        if (State.Seq >= 0 && batch.seq != State.Seq + 1)
        {
            if (batch.seq <= State.Seq)
                return; // a duplicate, safe to ignore

            Debug.LogWarning($"Missed events {State.Seq + 1}..{batch.seq - 1}; resyncing");
            ClearPending();
            _ = RequestStateAsync();
            return;
        }

        State.Seq = batch.seq;
        foreach (var e in batch.events)
        {
            _pending.Enqueue(e);
            _arrivals.Enqueue(Time.time);
        }
    }

    private void ClearPending()
    {
        _pending.Clear();
        _arrivals.Clear();
    }

    // ---- playback ----

    private void PlayNext()
    {
        while (!_busy && _pending.Count > 0)
        {
            var e = _pending.Dequeue();
            float arrived = _arrivals.Dequeue();
            if (logEvents)
                Debug.Log($"[MATCH] seq {State.Seq} {e.type} seat {e.seat}" +
                          (e.card != null && !e.card.IsEmpty ? $" card {e.card.defId}" : "") +
                          (e.type == UnoEventTypes.TurnChanged ? $" turnMs {e.turnMs} pending {e.pendingDraw}" : "") +
                          $" ({_pending.Count} queued)");
            // Mark busy before raising: a view that finishes straight away calls
            // StepComplete() inside Raise, and that must not be overwritten after
            if (waitForView)
            {
                _busy = true;
                _stepStarted = Time.time;
            }

            Apply(e, arrived);   // State is updated first, so views can read the result
            Raise(e);
        }
    }

    /// <summary>Updates MatchState for one event.</summary>
    private void Apply(MatchEventMsg e, float arrived)
    {
        var seat = State.SeatAt(e.seat);

        switch (e.type)
        {
            case UnoEventTypes.CardPlayed:
                var played = MatchState.ToCard(e.card);
                if (played != null)
                {
                    State.TopCard = played;
                    State.ActiveColor = MatchState.ParseColor(e.color);

                    if (e.seat == State.YourSeat)
                    {
                        var mine = State.FindInHand(played.InstanceId);
                        if (mine != null)
                            State.Hand.Remove(mine);
                    }
                }
                if (seat != null)
                    seat.CardCount = Mathf.Max(0, seat.CardCount - 1);
                State.DrawnCardId = -1;
                break;

            case UnoEventTypes.CardsDrawn:
                if (seat != null)
                    seat.CardCount += e.count;

                if (e.seat == State.YourSeat && e.cards != null)
                {
                    foreach (var msg in e.cards)
                    {
                        var card = MatchState.ToCard(msg);
                        if (card != null)
                            State.Hand.Add(card);
                    }

                    // One card drawn and the turn stays with us: it's ours to play
                    // or pass. If it can't be played the server ends the turn in
                    // the same batch, so a TURN_CHANGED next means there's no choice.
                    bool turnEndsNow = _pending.Count > 0 && _pending.Peek().type == UnoEventTypes.TurnChanged;
                    if (e.cards.Length == 1 && !turnEndsNow)
                    {
                        State.DrawnCardId = e.cards[0].id;
                        _turnEndsAt = arrived + _turnMs / 1000f; // the server restarts the clock after a move
                    }
                }
                State.DeckCount = Mathf.Max(0, State.DeckCount - e.count);
                break;

            case UnoEventTypes.DirectionChanged:
                State.Direction = e.direction;
                break;

            case UnoEventTypes.TurnChanged:
                State.CurrentSeat = e.seat;
                State.TurnMsLeft = e.turnMs;
                State.PendingDraw = e.pendingDraw;
                State.DrawnCardId = -1;
                if (e.turnMs > 0)
                    _turnMs = e.turnMs;
                _turnEndsAt = arrived + _turnMs / 1000f;
                break;

            case UnoEventTypes.TurnExtended:
                State.TurnMsLeft = e.turnMs;
                _turnEndsAt = arrived + e.turnMs / 1000f;
                break;

            case UnoEventTypes.SeatControl:
                if (seat != null)
                    seat.Kind = e.kind;
                break;

            case UnoEventTypes.PlayerConnection:
                if (seat != null)
                    seat.Connected = e.connected;
                break;

            case UnoEventTypes.PlayerFinished:
                if (seat != null)
                {
                    seat.Place = e.place;
                    seat.CardCount = 0;
                }
                break;

            case UnoEventTypes.GameOver:
                State.Ranking = e.ranking ?? new int[0];
                break;
        }
    }

    /// <summary>Tells the view about one event, after State already reflects it.</summary>
    private void Raise(MatchEventMsg e)
    {
        switch (e.type)
        {
            case UnoEventTypes.CardPlayed:
                OnCardPlayed?.Invoke(new CardPlayed
                {
                    Seat = e.seat,
                    Card = MatchState.ToCard(e.card),
                    Color = MatchState.ParseColor(e.color),
                });
                break;

            case UnoEventTypes.CardsDrawn:
                List<CardInstance> drawn = null;
                if (e.cards != null)
                {
                    drawn = new List<CardInstance>();
                    foreach (var msg in e.cards)
                        drawn.Add(MatchState.ToCard(msg));
                }
                OnCardsDrawn?.Invoke(new CardsDrawn { Seat = e.seat, Count = e.count, Cards = drawn });
                break;

            case UnoEventTypes.PlayerSkipped:
                OnPlayerSkipped?.Invoke(e.seat);
                break;

            case UnoEventTypes.DirectionChanged:
                OnDirectionChanged?.Invoke(e.direction);
                break;

            case UnoEventTypes.TurnChanged:
                OnTurnStarted?.Invoke(new TurnStarted
                {
                    Seat = e.seat,
                    TurnMs = e.turnMs,
                    PendingDraw = e.pendingDraw,
                });
                break;

            case UnoEventTypes.TurnTimedOut:
                OnTurnTimedOut?.Invoke(e.seat);
                break;

            case UnoEventTypes.SeatControl:
                OnSeatGivenToBot?.Invoke(e.seat);
                break;

            case UnoEventTypes.PlayerConnection:
                OnPlayerConnection?.Invoke(e.seat, e.connected);
                break;

            case UnoEventTypes.PlayerFinished:
                OnPlayerFinished?.Invoke(e.seat, e.place);
                break;

            case UnoEventTypes.GameOver:
                OnGameOver?.Invoke(State.Ranking);
                break;

            case UnoEventTypes.TurnExtended:
                OnTurnExtended?.Invoke(e.seat, e.turnMs);
                break;

            default:
                Debug.LogWarning($"Unknown event type from the server: {e.type}");
                break;
        }
    }

    // ---- sending ----

    /// <summary>
    /// Plays one of your cards. `color` is only used for Wild and Wild Draw
    /// Four, and must be picked before calling: a play is one message.
    /// </summary>
    public async Task PlayCardAsync(CardInstance card, CardColor color = CardColor.Wild)
    {
        if (card == null || Connection == null)
            return;

        var request = new PlayCardReq
        {
            cardId = card.InstanceId,
            color = IsWild(card) ? MatchState.ColorName(color) : "",
        };
        await Connection.SendMatchStateAsync(UnoOpCodes.PlayCard, JsonUtility.ToJson(request));
    }

    public async Task DrawAsync()
    {
        if (Connection != null)
            await Connection.SendMatchStateAsync(UnoOpCodes.DrawCard);
    }

    public async Task PassAsync()
    {
        if (Connection != null)
            await Connection.SendMatchStateAsync(UnoOpCodes.Pass);
    }

    /// <summary>
    /// Restarts your clock before choosing the colour of a wild you drew. The
    /// server allows it once a turn, and only while holding a drawn wild.
    /// </summary>
    public async Task ExtendTurnAsync()
    {
        if (Connection != null)
            await Connection.SendMatchStateAsync(UnoOpCodes.ExtendTurn);
    }

    /// <summary>Quits the match: a bot takes the seat at once and REMOVED (LEFT) comes back.</summary>
    public async Task LeaveAsync()
    {
        if (Connection != null)
            await Connection.SendMatchStateAsync(UnoOpCodes.LeaveMatch);
    }

    /// <summary>Asks for a fresh snapshot, e.g. after an error or a missed batch.</summary>
    public async Task RequestStateAsync()
    {
        if (Connection != null)
            await Connection.SendMatchStateAsync(UnoOpCodes.RequestState);
    }

    // ---- helpers for the view ----

    /// <summary>Whether this card may be played now; used to highlight cards.</summary>
    public bool CanPlay(CardInstance card) => State.CanPlay(card, database);

    public List<CardInstance> PlayableCards() => State.PlayableCards(database);

    /// <summary>Seconds until the server's clock for the current turn runs out (our estimate, erring early).</summary>
    public float SecondsLeftInTurn => Mathf.Max(0f, _turnEndsAt - Time.time);

    /// <summary>A full turn, as the server last said.</summary>
    public float TurnSeconds => _turnMs / 1000f;

    /// <summary>True while you drew a card that you may still play or pass on.</summary>
    public bool MustPlayDrawnOrPass => State.IsYourTurn && State.DrawnCardId >= 0;

    private bool IsWild(CardInstance card)
    {
        var def = database != null ? database.GetById(card.CardId) : null;
        return def != null && (def.Type == CardType.Wild || def.Type == CardType.WildDrawFour);
    }
}
