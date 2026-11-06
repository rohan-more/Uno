using System;
using DG.Tweening;
using UnityEngine;

/// <summary>What a table event panel may need to know, e.g. which way play now goes.</summary>
public struct TableEventContext
{
    public int Direction; // +1 clockwise on screen, -1 anticlockwise
    public int DrawTotal; // Draw Two / Four: cards now owed, stacked ones included (0 if unknown)
}

/// <summary>
/// The base for every special-card panel ("Block!", "Reverse", "Pickup 2"):
/// each has its own art and animation. TableEventsView switches a panel on when
/// its moment comes and it plays itself: fades in through its CanvasGroup while
/// its content pops from Start Scale, holds, fades out, and switches off again.
///
/// Play is the fixed outline; each kind of panel adds its own motion in Animate.
/// PopEventPanel is the plain pop, ReverseEventPanel spins its arrows. Put one
/// on each panel, with a CanvasGroup.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public abstract class TableEventPanel : MonoBehaviour
{
    [Tooltip("What pops in. The panel itself if empty.")]
    [SerializeField] protected RectTransform content;
    [Tooltip("Where the pop starts: 0 grows from nothing (Block), 0.6 is a gentle pop.")]
    [SerializeField] protected float startScale = 0.6f;
    [SerializeField] protected Ease popEase = Ease.OutBack;

    [Header("Timing")]
    [SerializeField] protected float popInSeconds = 0.3f;
    [SerializeField] protected float holdSeconds = 0.6f;
    [SerializeField] protected float fadeOutSeconds = 0.25f;

    private CanvasGroup group;
    private Sequence playing;

    /// <summary>How long Play takes from start to finish.</summary>
    public float Duration => popInSeconds + holdSeconds + fadeOutSeconds;

    // Set up on first use, not in Awake: the panel starts switched off
    private void Init()
    {
        if (group != null)
            return;
        group = GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false; // never catches a tap meant for the cards
        if (content == null)
            content = (RectTransform)transform;
    }

    /// <summary>Shows the panel and plays its animation, then hides it and calls onDone.</summary>
    public void Play(TableEventContext context, Action onDone = null)
    {
        Init();
        playing?.Kill();

        gameObject.SetActive(true);
        group.alpha = 0f;
        content.localScale = Vector3.one * startScale;

        playing = DOTween.Sequence()
            .Append(group.DOFade(1f, popInSeconds * 0.6f))
            .Join(content.DOScale(1f, popInSeconds).SetEase(popEase))
            .AppendInterval(holdSeconds)
            .Append(group.DOFade(0f, fadeOutSeconds))
            .OnComplete(() =>
            {
                Hide();
                onDone?.Invoke();
            });

        Animate(playing, context);
    }

    /// <summary>
    /// Adds a panel's own motion to the sequence, e.g. Join a spin that lasts
    /// the whole Duration. Insert at time 0 to run alongside the pop.
    /// </summary>
    protected abstract void Animate(Sequence sequence, TableEventContext context);

    /// <summary>Stops at once and switches the panel off.</summary>
    public void Hide()
    {
        playing?.Kill();
        playing = null;
        Init();
        group.alpha = 0f;
        gameObject.SetActive(false);
    }

    private void OnDisable() => playing?.Kill();
}
