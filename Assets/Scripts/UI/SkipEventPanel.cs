using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Skip ("Block!") panel: the block sign slams down like a stamp, lands
/// with a wobble and a small shake, and can send a shockwave ring out from it.
/// For a clean stamp, leave the panel's own pop off (Start Scale 1) so only the
/// icon moves.
/// </summary>
public class SkipEventPanel : TableEventPanel
{
    [Tooltip("The block sign that slams down.")]
    [SerializeField] private RectTransform icon;

    [Header("Slam")]
    [Tooltip("How big the icon starts, before it slams down to its own size.")]
    [SerializeField] private float slamFromScale = 2.2f;
    [Tooltip("How tilted it starts, in degrees; it lands straight.")]
    [SerializeField] private float slamFromAngle = -25f;
    [SerializeField] private float slamSeconds = 0.22f;

    [Header("Landing")]
    [Tooltip("Degrees of wobble when it lands.")]
    [SerializeField] private float wobbleDegrees = 14f;
    [Tooltip("How far it shakes when it lands, in UI units.")]
    [SerializeField] private float shakeStrength = 18f;
    [SerializeField] private float landingSeconds = 0.35f;

    [Header("Shockwave (optional)")]
    [Tooltip("A ring image behind the icon that bursts outwards and fades when the icon lands.")]
    [SerializeField] private Image shockwave;
    [SerializeField] private float shockwaveScale = 1.8f;
    [SerializeField] private float shockwaveSeconds = 0.4f;

    private Vector2 iconRestPosition;
    private bool restPositionKnown;

    protected override void Animate(Sequence sequence, TableEventContext context)
    {
        if (icon == null)
            return;

        // Shake around where it was placed, even if the last shake was cut short
        if (!restPositionKnown)
        {
            iconRestPosition = icon.anchoredPosition;
            restPositionKnown = true;
        }
        icon.anchoredPosition = iconRestPosition;
        icon.localScale = Vector3.one * slamFromScale;
        icon.localRotation = Quaternion.Euler(0f, 0f, slamFromAngle);

        // Slam: speeds up as it falls, like it's being pressed down
        sequence.Insert(0f, icon.DOScale(1f, slamSeconds).SetEase(Ease.InQuad));
        sequence.Insert(0f, icon.DOLocalRotate(Vector3.zero, slamSeconds).SetEase(Ease.InQuad));

        // Landing: a wobble and a short shake
        sequence.Insert(slamSeconds, icon.DOPunchRotation(new Vector3(0f, 0f, wobbleDegrees), landingSeconds, 8, 0.6f));
        sequence.Insert(slamSeconds, icon.DOShakeAnchorPos(landingSeconds, shakeStrength, 20, 90f, false, true));

        if (shockwave != null)
        {
            var ring = shockwave.rectTransform;
            ring.localScale = Vector3.one * 0.6f;
            var color = shockwave.color;
            color.a = 0f;
            shockwave.color = color;

            sequence.InsertCallback(slamSeconds, () =>
            {
                var c = shockwave.color;
                c.a = 1f;
                shockwave.color = c;
            });
            sequence.Insert(slamSeconds, ring.DOScale(shockwaveScale, shockwaveSeconds).SetEase(Ease.OutCubic));
            sequence.Insert(slamSeconds, shockwave.DOFade(0f, shockwaveSeconds).SetEase(Ease.InQuad));
        }
    }
}
