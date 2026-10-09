using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pass as a popup's context to give it a countdown. When it reaches zero the
/// popup calls OnTimeout, which decides what happens and closes it.
/// </summary>
public class PopupCountdown
{
    public float TimeoutSeconds;
    public Action OnTimeout;
}

public class PopupView : MonoBehaviour
{
    [SerializeField] private PopupType popupType;
    public PopupType Type => popupType;
    
    [Header("Transition")]
    [SerializeField] private PopupTransitionType transitionType = PopupTransitionType.SlideFromBottom;
    [SerializeField] private float transitionDuration = 0.35f;
    [SerializeField] private Ease easeIn = Ease.OutCubic;
    [SerializeField] private Ease easeOut = Ease.InCubic;

    [Header("Countdown (optional)")]
    [Tooltip("Filled Image that drains while a PopupCountdown runs.")]
    [SerializeField] private Image countdownFill;
    [Tooltip("Whole seconds left.")]
    [SerializeField] private TMP_Text countdownText;

    protected Action onCompleted;
    protected RectTransform rt;
    private float countdownTotal;
    private float countdownEndsAt;
    private Action onTimeout;
    private Vector2 hiddenPos;
    private Vector2 visiblePos;
    private Tween activeTween;

    protected virtual void Awake()
    {
        // Each one hides the popup in Awake, so a second one hides it again the
        // moment Show activates it
        if (GetComponents<PopupView>().Length > 1)
            Debug.LogError($"{name} has more than one PopupView component; keep only one " +
                           "(e.g. just DrawnCardChoicePopup), or it opens hidden", this);

        rt = GetComponent<RectTransform>();
        visiblePos = rt.anchoredPosition;
        hiddenPos = GetHiddenPosition();
        gameObject.SetActive(false);
    }
    
    protected virtual void OnEnable()
    {
        if (rt != null) return;

        rt = GetComponent<RectTransform>();
        visiblePos = rt.anchoredPosition;
    }


    public virtual void Show(object context = null, Action onCompleted = null)
    {
        KillTween();
        gameObject.SetActive(true);
        this.onCompleted = onCompleted;
        rt.anchoredPosition = hiddenPos;
        activeTween = rt.DOAnchorPos(visiblePos, transitionDuration).SetEase(easeIn);

        var countdown = context as PopupCountdown;
        onTimeout = countdown != null && countdown.TimeoutSeconds > 0f ? countdown.OnTimeout : null;
        countdownTotal = countdown?.TimeoutSeconds ?? 0f;
        countdownEndsAt = Time.time + countdownTotal;

        bool counting = onTimeout != null;
        if (countdownFill != null)
            countdownFill.gameObject.SetActive(counting);
        if (countdownText != null)
            countdownText.gameObject.SetActive(counting);
    }

    protected virtual void Update()
    {
        if (onTimeout == null)
            return;

        float left = Mathf.Max(0f, countdownEndsAt - Time.time);
        if (countdownFill != null)
            countdownFill.fillAmount = countdownTotal > 0f ? left / countdownTotal : 0f;
        if (countdownText != null)
            countdownText.text = Mathf.CeilToInt(left).ToString();

        if (left <= 0f)
        {
            var timedOut = onTimeout;
            onTimeout = null; // once
            timedOut.Invoke();
        }
    }
    
    protected void Complete()
    {
        onCompleted?.Invoke();
        onCompleted = null;
    }

    public virtual void Hide()
    {
        onTimeout = null; // answered or closed: the countdown no longer matters
        KillTween();
        activeTween = rt.DOAnchorPos(GetExitPosition(), transitionDuration).SetEase(easeOut).OnComplete(() => gameObject.SetActive(false));
        Complete();
    }
 
    private Vector2 GetHiddenPosition()
    {
        return GetOffsetPosition(-1);
    }

    private Vector2 GetExitPosition()
    {
        return GetOffsetPosition(1);
    }

    private Vector2 GetOffsetPosition(int direction)
    {
        rt = GetComponent<RectTransform>();
        visiblePos = rt.anchoredPosition;
        float h = ((RectTransform)rt.parent).rect.height;
        return visiblePos + GetTransitionDirection() * h * direction;
    }

    private Vector2 GetTransitionDirection()
    {
        if (transitionType == PopupTransitionType.SlideFromBottom)
            return Vector2.up;

        if (transitionType == PopupTransitionType.SlideFromTop)
            return Vector2.down;

        return Vector2.zero;
    }


    // PopupManager destroys a popup as soon as it starts sliding out
    protected virtual void OnDestroy() => KillTween();

    private void KillTween()
    {
        if (activeTween != null && activeTween.IsActive())
            activeTween.Kill();
    }
}


public enum PopupType
{
    None = 0,
    ChooseColor,
    ConfirmAction,
    Info,
    DrawnCardChoice // add new types at the end: prefabs store the number
}

public enum PopupTransitionType
{
    None,
    SlideFromBottom,
    SlideFromTop,
    Fade,
    Scale
}

