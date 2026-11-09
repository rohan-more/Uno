using UnityEngine;

/// <summary>
/// The match's sound effects, played from the presenter's events: card plays
/// (with a layer for Draw Two, Wild Draw Four and wilds), draws, skips,
/// reverses, your turn, the last seconds of your turn, and the end.
///
/// It only listens: MatchView owns the events' StepComplete. Events play as
/// the presenter replays them, so sounds land with their animations. Put it on
/// any object in MatchScene.
/// </summary>
public class MatchAudio : MonoBehaviour
{
    [SerializeField] private MatchPresenter presenter;

    [Tooltip("Tick each second once your turn is down to this many.")]
    [SerializeField] private int tickFromSeconds = 3;

    private int lastTick = -1;

    private static IAudioService Audio => Services.Audio;
    private MatchState State => presenter.State;

    private void OnEnable()
    {
        if (presenter == null)
            return;
        presenter.OnCardPlayed += HandleCardPlayed;
        presenter.OnCardsDrawn += HandleCardsDrawn;
        presenter.OnPlayerSkipped += HandleSkipped;
        presenter.OnDirectionChanged += HandleDirectionChanged;
        presenter.OnTurnStarted += HandleTurnStarted;
        presenter.OnGameOver += HandleGameOver;
    }

    private void OnDisable()
    {
        if (presenter == null)
            return;
        presenter.OnCardPlayed -= HandleCardPlayed;
        presenter.OnCardsDrawn -= HandleCardsDrawn;
        presenter.OnPlayerSkipped -= HandleSkipped;
        presenter.OnDirectionChanged -= HandleDirectionChanged;
        presenter.OnTurnStarted -= HandleTurnStarted;
        presenter.OnGameOver -= HandleGameOver;
    }

    private void HandleCardPlayed(MatchPresenter.CardPlayed played)
    {
        Audio?.Play(Sfx.CardPlayed);

        // Special cards add a second sound on top of the card landing. Skip and
        // Reverse have their own events, so they're played from those.
        var def = played.Card?.GetDefinition(Services.Assets.Cards);
        switch (def?.Type)
        {
            case CardType.DrawTwo:
                Audio?.Play(Sfx.DrawTwo);
                break;
            case CardType.WildDrawFour:
                Audio?.Play(Sfx.WildDrawFour);
                break;
            case CardType.Wild:
                Audio?.Play(Sfx.WildColor);
                break;
        }
    }

    private void HandleCardsDrawn(MatchPresenter.CardsDrawn drawn) => Audio?.Play(Sfx.CardDrawn);

    private void HandleSkipped(int seat) => Audio?.Play(Sfx.Skip);

    private void HandleDirectionChanged(int direction) => Audio?.Play(Sfx.Reverse);

    private void HandleTurnStarted(MatchPresenter.TurnStarted turn)
    {
        lastTick = -1;
        if (turn.Seat == State.YourSeat)
            Audio?.Play(Sfx.YourTurn);
    }

    private void HandleGameOver(int[] ranking)
    {
        var you = State.You;
        Audio?.Play(you != null && you.Place == 1 ? Sfx.Win : Sfx.Lose);
    }

    // The clock ticks through your last few seconds
    private void Update()
    {
        if (presenter == null || !State.IsYourTurn)
        {
            lastTick = -1;
            return;
        }

        int left = Mathf.CeilToInt(presenter.SecondsLeftInTurn);
        if (left >= 1 && left <= tickFromSeconds && left != lastTick)
        {
            lastTick = left;
            Audio?.Play(Sfx.TimerTick);
        }
    }
}
