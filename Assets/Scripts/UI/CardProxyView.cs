using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public class CardProxyView : MonoBehaviour
{
    [SerializeField] private Image cardImage;
    [SerializeField] private float moveDuration = 0.25f;
    [SerializeField] private Ease moveEase = Ease.OutCubic;

    private RectTransform rt;
    private Tween activeTween;

    void Awake()
    {

        gameObject.SetActive(false);
    }

    public void Show(Sprite sprite, Vector2 startPos)
    {
        KillTween();
        rt = GetComponent<RectTransform>();
        cardImage.sprite = sprite;
        rt.anchoredPosition = startPos;
        rt.localRotation = Quaternion.identity;
        rt.localScale = Vector3.one;

        gameObject.SetActive(true);
        LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
    }

    public void AnimateTo(Vector2 targetPos, System.Action onComplete = null)
    {
        KillTween();
        if (!gameObject.activeInHierarchy)
            gameObject.SetActive(true);
        activeTween = rt.DOAnchorPos(targetPos, moveDuration)
            .SetEase(moveEase)
            .OnComplete(() => onComplete?.Invoke());
    }

    public void AnimateToDiscard(Vector2 targetPos, float scaleDown = 0.9f, System.Action onComplete = null)
    {
        KillTween();
        if (!gameObject.activeInHierarchy)
            gameObject.SetActive(true);
        Sequence seq = DOTween.Sequence();
        seq.Join(rt.DOAnchorPos(targetPos, moveDuration).SetEase(Ease.InCubic));
        seq.Join(rt.DOScale(scaleDown, moveDuration));
        seq.OnComplete(() => onComplete?.Invoke());

        activeTween = seq;
    }

    public void Hide()
    {
        KillTween();
        gameObject.SetActive(false);
    }

    private void KillTween()
    {
        if (activeTween != null && activeTween.IsActive())
            activeTween.Kill();
    }
    
    public void AnimateToAndVanish(Vector2 targetPos, float vanishScale = 0.85f, float vanishDuration = 0.08f, System.Action onComplete = null)
    {
        KillTween();
        if (!gameObject.activeInHierarchy)
            gameObject.SetActive(true);
        Sequence seq = DOTween.Sequence();
        seq.Append(rt.DOAnchorPos(targetPos, moveDuration).SetEase(moveEase));
        seq.Append(rt.DOScale(vanishScale, vanishDuration));
        seq.OnComplete(() =>
        {
            Hide();
            onComplete?.Invoke();
        });

        activeTween = seq;
    }
}


public static class RectTransformUtil
{
    public static Vector2 WorldToAnchored(
        RectTransform worldSource,
        RectTransform targetParent)
    {
        Vector2 screenPoint =
            RectTransformUtility.WorldToScreenPoint(
                null,
                worldSource.position
            );

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            targetParent,
            screenPoint,
            null,
            out Vector2 localPoint
        );

        return localPoint;
    }
}