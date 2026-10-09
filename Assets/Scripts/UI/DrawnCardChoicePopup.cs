using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Who hears the answer from the drawn-card popup, and its countdown.</summary>
public class DrawnCardChoice : PopupCountdown
{
    public Action OnPlay;
    public Action OnKeep; // keep the card and end the turn
}

/// <summary>
/// After you draw a card you could play: Play or Keep. The card itself hovers
/// face up over the discard pile meanwhile, so the popup is just the buttons.
/// Opened by MatchView through PopupManager with a DrawnCardChoice context.
/// </summary>
public class DrawnCardChoicePopup : PopupView
{
    [SerializeField] private Button playButton;
    [SerializeField] private Button keepButton;

    private DrawnCardChoice choice;

    public override void Show(object context = null, Action onCompleted = null)
    {
        base.Show(context, onCompleted);

        choice = context as DrawnCardChoice;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        playButton.onClick.AddListener(PlayPressed);
        keepButton.onClick.AddListener(KeepPressed);
    }

    private void OnDisable()
    {
        playButton.onClick.RemoveListener(PlayPressed);
        keepButton.onClick.RemoveListener(KeepPressed);
    }

    private void PlayPressed() => Answer(choice?.OnPlay);

    private void KeepPressed() => Answer(choice?.OnKeep);

    // Close first: playing a wild opens the colour popup, which this close must not hit
    private void Answer(Action answer)
    {
        choice = null;
        PopupManager.Instance.CloseActive();
        answer?.Invoke();
    }
}
