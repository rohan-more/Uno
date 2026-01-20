using System;
using DG.Tweening;
using UnityEngine;


public class PopupView : MonoBehaviour
{
    [SerializeField] private PopupType popupType;
    public PopupType Type => popupType;
    
    [Header("Transition")]
    [SerializeField] private PopupTransitionType transitionType = PopupTransitionType.SlideFromBottom;
    [SerializeField] private float transitionDuration = 0.35f;
    [SerializeField] private Ease easeIn = Ease.OutCubic;
    [SerializeField] private Ease easeOut = Ease.InCubic;
    protected Action onCompleted;
    protected RectTransform rt;
    private Vector2 hiddenPos;
    private Vector2 visiblePos;
    private Tween activeTween;

    protected virtual void Awake()
    {
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
    }
    
    protected void Complete()
    {
        onCompleted?.Invoke();
        onCompleted = null;
    }

    public virtual void Hide()
    {
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
    Info
}

public enum PopupTransitionType
{
    None,
    SlideFromBottom,
    SlideFromTop,
    Fade,
    Scale
}

