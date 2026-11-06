using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Moments the table calls out to everyone.</summary>
public enum TableEvent
{
    DrawTwo,
    WildDrawFour,
    Skip,
    Reverse,
    ColorChange,
    YourTurn,
}

/// <summary>
/// Calls out special moments to everyone at the table, Card Party style: the
/// card lands, then its panel plays, then the next thing happens. Every client
/// gets the same events in the same order, so everyone sees the same panels.
///
/// Each moment has a panel of its own (a TableEventPanel or a subclass), with
/// its own art and animation; this only decides which plays and when. Panels
/// queue, so two in a row play one after the other.
///
/// It only listens: MatchView owns the events' StepComplete. The pacing that
/// gives panels room comes from the server, which pauses longer after a special
/// card (eventPauseSeconds) before the next turn, and starts the clock after it.
/// So keep Card Lands Seconds plus a panel's Duration under that pause.
/// </summary>
public class TableEventsView : MonoBehaviour
{
    [Serializable]
    public class Entry
    {
        public TableEvent tableEvent;
        public TableEventPanel panel;
    }

    [SerializeField] private MatchPresenter presenter;

    [Tooltip("Which panel plays for which moment. A moment with no panel shows nothing.")]
    [SerializeField] private List<Entry> panels = new()
    {
        new Entry { tableEvent = TableEvent.Skip },
        new Entry { tableEvent = TableEvent.Reverse },
        new Entry { tableEvent = TableEvent.DrawTwo },
        new Entry { tableEvent = TableEvent.WildDrawFour },
    };

    [Tooltip("How long a played card takes to reach the pile, so a Draw Two's panel follows it. " +
             "Skip and Reverse arrive as their own events once it has landed, so they don't wait.")]
    [SerializeField] private float cardLandsSeconds = 0.45f;
    [Tooltip("Breath between two panels in a row.")]
    [SerializeField] private float betweenPanelsSeconds = 0.1f;

    private readonly Queue<(TableEventPanel panel, float notBefore, int drawTotal)> queue = new();
    private TableEventPanel current;
    private Coroutine running;

    private void OnEnable()
    {
        HideAll();
        if (presenter == null)
            return;
        presenter.OnCardPlayed += HandleCardPlayed;
        presenter.OnPlayerSkipped += HandleSkipped;
        presenter.OnDirectionChanged += HandleDirectionChanged;
        presenter.OnSnapshot += HandleSnapshot;
    }

    private void OnDisable()
    {
        if (presenter != null)
        {
            presenter.OnCardPlayed -= HandleCardPlayed;
            presenter.OnPlayerSkipped -= HandleSkipped;
            presenter.OnDirectionChanged -= HandleDirectionChanged;
            presenter.OnSnapshot -= HandleSnapshot;
        }
        queue.Clear();
        running = null;
    }

    private void HandleCardPlayed(MatchPresenter.CardPlayed played)
    {
        // The owed total only updates with the TURN_CHANGED after this card,
        // so it's still what was owed before it: add this card on top
        int owedBefore = presenter.State.PendingDraw;
        var def = played.Card?.GetDefinition(Services.Assets.Cards);
        switch (def?.Type)
        {
            case CardType.DrawTwo:
                Show(TableEvent.DrawTwo, cardLandsSeconds, owedBefore + 2);
                break;
            case CardType.WildDrawFour:
                Show(TableEvent.WildDrawFour, cardLandsSeconds, owedBefore + 4);
                break;
        }
    }

    private void HandleSkipped(int seat) => Show(TableEvent.Skip);

    private void HandleDirectionChanged(int direction) => Show(TableEvent.Reverse);

    // A snapshot redraws the whole table: anything still queued is stale
    private void HandleSnapshot(MatchState state)
    {
        queue.Clear();
        if (running != null)
            StopCoroutine(running);
        running = null;
        HideAll();
    }

    /// <summary>
    /// Queues the panel for a moment, shown no sooner than delay seconds from
    /// now. drawTotal is what's owed after a Draw Two / Four, stacks included.
    /// </summary>
    public void Show(TableEvent tableEvent, float delay = 0f, int drawTotal = 0)
    {
        var panel = panels.Find(e => e.tableEvent == tableEvent)?.panel;
        if (panel == null)
            return;

        queue.Enqueue((panel, Time.time + delay, drawTotal));
        running ??= StartCoroutine(PlayQueue());
    }

    private IEnumerator PlayQueue()
    {
        while (queue.Count > 0)
        {
            var (panel, notBefore, drawTotal) = queue.Dequeue();
            while (Time.time < notBefore)
                yield return null;

            // Read when it plays, not when it was queued: a Reverse spins the
            // way play goes now
            var context = new TableEventContext { Direction = presenter.State.Direction, DrawTotal = drawTotal };

            bool done = false;
            current = panel;
            panel.Play(context, () => done = true);
            while (!done)
                yield return null;
            current = null;

            if (betweenPanelsSeconds > 0f)
                yield return new WaitForSeconds(betweenPanelsSeconds);
        }
        running = null;
    }

    private void HideAll()
    {
        current = null;
        foreach (var entry in panels)
        {
            if (entry.panel != null)
                entry.panel.Hide();
        }
    }
}
